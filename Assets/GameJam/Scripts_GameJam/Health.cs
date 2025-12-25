using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Health system with proper visual state management.
/// 
/// IMPORTANT: This script does NOT directly control wizard/slime visuals.
/// Instead, it uses a master "isVisible" flag and lets ProceduralCharacterAnimator
/// handle which specific visual (wizard vs slime) should be shown.
/// 
/// This prevents conflicts when:
/// - Player dies as slime
/// - Player respawns
/// - SlimeShift event triggers again
/// </summary>
public class Health : NetworkBehaviour
{
    #region Inspector Fields

    [Header("Health Settings")]
    [SerializeField] private int maxHealth = 100;
    public NetworkVariable<int> currentHealth = new NetworkVariable<int>(100);

    [Header("UI References")]
    [SerializeField] private Image healthBarFill;
    [SerializeField] private Image healthBarBackground;

    [Header("Respawn Settings")]
    [SerializeField] private float respawnDelay = 3f;

    [Header("Visual Feedback")]
    [SerializeField] private Color normalHealthColor = Color.green;
    [SerializeField] private Color lowHealthColor = Color.red;
    [SerializeField] private float lowHealthThreshold = 0.3f;

    #endregion

    #region Private Fields

    private Collider2D col;
    private PlayerController controller;
    private SlimeController slimeController;
    private LaserBeamUltimate laserUltimate;
    private ProceduralCharacterAnimator animator;
    private Canvas nameCanvas;
    private Rigidbody2D rb;
    private Canvas healthBarCanvas;

    // Track visibility state
    private bool isPlayerVisible = true;

    #endregion

    #region Unity Lifecycle

    private void Awake()
    {
        col = GetComponent<Collider2D>();
        controller = GetComponent<PlayerController>();
        slimeController = GetComponent<SlimeController>();
        laserUltimate = GetComponent<LaserBeamUltimate>();
        animator = GetComponent<ProceduralCharacterAnimator>();
        rb = GetComponent<Rigidbody2D>();
        
        nameCanvas = GetComponentInChildren<Canvas>();
        
        if (healthBarFill != null)
        {
            healthBarCanvas = healthBarFill.GetComponentInParent<Canvas>();
        }
    }

    public override void OnNetworkSpawn()
    {
        currentHealth.OnValueChanged += OnHealthChanged;
        UpdateHealthBar(currentHealth.Value);
    }

    #endregion

    #region Health Changes

    private void OnHealthChanged(int oldHealth, int newHealth)
    {
        UpdateHealthBar(newHealth);
        
        if (newHealth < oldHealth && animator != null)
        {
            animator.TriggerHitReaction();
        }
    }

    private void UpdateHealthBar(int current)
    {
        if (healthBarFill != null)
        {
            float healthPercent = (float)current / maxHealth;
            healthBarFill.fillAmount = healthPercent;
            
            healthBarFill.color = healthPercent <= lowHealthThreshold 
                ? lowHealthColor 
                : Color.Lerp(lowHealthColor, normalHealthColor, (healthPercent - lowHealthThreshold) / (1f - lowHealthThreshold));
        }
    }

    #endregion

    #region Damage System

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void TakeDamageServerRpc(int amount, ulong attackerId)
    {
        if (!IsServer) return;

        int previousHealth = currentHealth.Value;
        currentHealth.Value -= amount;

        if (slimeController != null && slimeController.IsSlime)
        {
            slimeController.OnDamageTaken(amount);
        }

        AwardAttackerUltimate(attackerId, amount);

        if (currentHealth.Value <= 0 && previousHealth > 0)
        {
            currentHealth.Value = 0;
            HandleDeath(attackerId);
        }

        Debug.Log($"[Health] Player {OwnerClientId} took {amount} damage. Health: {currentHealth.Value}/{maxHealth}");
    }

    public void TakeDamage(int amount, ulong attackerId)
    {
        TakeDamageServerRpc(amount, attackerId);
    }

    private void AwardAttackerUltimate(ulong attackerId, int damageDealt)
    {
        if (!IsServer) return;
        if (attackerId == OwnerClientId) return;
        
        if (NetworkManager.Singleton.ConnectedClients.TryGetValue(attackerId, out NetworkClient attackerClient))
        {
            if (attackerClient.PlayerObject != null)
            {
                LaserBeamUltimate attackerLaser = attackerClient.PlayerObject.GetComponent<LaserBeamUltimate>();
                if (attackerLaser != null)
                {
                    attackerLaser.AddUltimateCharge(damageDealt);
                }
                
                SlimeController attackerSlime = attackerClient.PlayerObject.GetComponent<SlimeController>();
                if (attackerSlime != null)
                {
                    attackerSlime.OnDamageDealt(damageDealt);
                }
            }
        }
    }

    #endregion

    #region Death & Respawn

    private void HandleDeath(ulong attackerId)
    {
        if (attackerId != OwnerClientId)
        {
            AwardKillScore(attackerId);
        }

        StartCoroutine(RespawnRoutine());
    }

    private void AwardKillScore(ulong attackerId)
    {
        if (NetworkManager.Singleton.ConnectedClients.TryGetValue(attackerId, out NetworkClient attackerClient))
        {
            if (attackerClient.PlayerObject != null)
            {
                PlayerController attackerController = attackerClient.PlayerObject.GetComponent<PlayerController>();
                if (attackerController != null)
                {
                    attackerController.score.Value += 1;
                    
                    LeaderboardManager lb = Object.FindFirstObjectByType<LeaderboardManager>();
                    if (lb != null)
                    {
                        lb.UpdateScore(attackerId, 100);
                    }
                }
            }
        }
        
        Debug.Log($"[Health] Player {attackerId} killed player {OwnerClientId}");
    }

    private IEnumerator RespawnRoutine()
    {
        // 1. Hide player completely
        SetPlayerVisible(false);

        // 2. Wait for respawn delay
        yield return new WaitForSeconds(respawnDelay);

        // 3. Teleport to random spawn point
        GameObject[] spawnPoints = GameObject.FindGameObjectsWithTag("Respawn");
        if (spawnPoints.Length > 0)
        {
            Vector3 newPos = spawnPoints[Random.Range(0, spawnPoints.Length)].transform.position;
            TeleportClientRpc(newPos);
        }

        // 4. Reset health
        currentHealth.Value = maxHealth;
        
        // 5. Reset slime scale
        if (slimeController != null)
        {
            slimeController.ResetState();
        }
        
        // 6. Show player again - this will trigger proper visual state
        SetPlayerVisible(true);
        
        Debug.Log($"[Health] Player {OwnerClientId} respawned");
    }

    [ClientRpc]
    private void TeleportClientRpc(Vector3 newPos)
    {
        if (rb != null) 
            rb.linearVelocity = Vector2.zero;

        if (IsOwner)
        {
            if (rb != null) 
                rb.simulated = false;

            transform.position = newPos;

            if (rb != null)
            {
                rb.position = newPos;
                rb.simulated = true;
            }
            return;
        }

        transform.position = newPos;
        if (rb != null) 
            rb.position = newPos;
    }

    #endregion

    #region Visibility Management

    /// <summary>
    /// Master visibility control. When false, EVERYTHING is hidden.
    /// When true, the animator decides which visual (wizard/slime) to show.
    /// </summary>
    private void SetPlayerVisible(bool visible)
    {
        isPlayerVisible = visible;
        
        // Apply on server
        ApplyVisibility(visible);
        
        // Broadcast to clients
        SetPlayerVisibleClientRpc(visible);
    }

    [ClientRpc]
    private void SetPlayerVisibleClientRpc(bool visible)
    {
        if (!IsServer)
        {
            ApplyVisibility(visible);
        }
    }

    private void ApplyVisibility(bool visible)
    {
        isPlayerVisible = visible;
        
        // === KEY FIX: Let the animator handle visual switching ===
        if (animator != null)
        {
            animator.SetMasterVisibility(visible);
        }
        else
        {
            // Fallback: No animator, try to handle visuals directly
            FallbackVisibilityControl(visible);
        }
        
        // Collider
        if (col != null) 
            col.enabled = visible;
        
        // Player controller + aim arrow
        if (controller != null) 
        {
            controller.enabled = visible;
            controller.SetArrowVisibility(visible);
        }
        
        // Name canvas
        if (nameCanvas != null) 
            nameCanvas.enabled = visible;
        
        // Health bar
        if (healthBarFill != null && healthBarFill.transform.parent != null)
        {
            healthBarFill.transform.parent.gameObject.SetActive(visible);
        }
        
        Debug.Log($"[Health] ApplyVisibility: visible={visible}");
    }

    /// <summary>
    /// Fallback if no ProceduralCharacterAnimator exists
    /// </summary>
    private void FallbackVisibilityControl(bool visible)
    {
        // Try to find and control visuals directly
        Transform wizardVisual = transform.Find("WizardVisual");
        Transform slimeVisual = transform.Find("SlimeVisual");
        
        bool isSlime = slimeController != null && slimeController.IsSlime;
        
        if (wizardVisual != null)
        {
            wizardVisual.gameObject.SetActive(visible && !isSlime);
        }
        
        if (slimeVisual != null)
        {
            slimeVisual.gameObject.SetActive(visible && isSlime);
        }
        
        // Legacy single sprite
        SpriteRenderer sr = GetComponent<SpriteRenderer>();
        if (sr != null && wizardVisual == null && slimeVisual == null)
        {
            sr.enabled = visible;
        }
    }

    #endregion

    #region Public API

    public void Heal(int amount)
    {
        if (!IsServer) return;
        currentHealth.Value = Mathf.Min(maxHealth, currentHealth.Value + amount);
    }

    public void FullHeal()
    {
        if (!IsServer) return;
        currentHealth.Value = maxHealth;
    }

    public int MaxHealth => maxHealth;
    public int CurrentHealth => currentHealth.Value;
    public float HealthPercent => (float)currentHealth.Value / maxHealth;
    public bool IsDead => currentHealth.Value <= 0;
    public bool IsVisible => isPlayerVisible;

    #endregion

    #region Cleanup

    public override void OnDestroy()
    {
        currentHealth.OnValueChanged -= OnHealthChanged;
        base.OnDestroy();
    }

    #endregion
}