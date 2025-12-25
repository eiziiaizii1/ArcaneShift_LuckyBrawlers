using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Health system with SlimeController integration.
/// When in slime form, taking damage triggers adaptive scaling (smaller but faster).
/// 
/// Features:
/// - Network-synced health
/// - Respawn system
/// - SlimeController damage notification
/// - Ultimate charge for attacker
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

    private SpriteRenderer spriteRenderer;
    private Collider2D col;
    private PlayerController controller;
    private SlimeController slimeController;
    private ProceduralCharacterAnimator animator;
    private Canvas nameCanvas;
    private Rigidbody2D rb;

    #endregion

    #region Unity Lifecycle

    private void Awake()
    {
        // Find components - check both parent and children
        spriteRenderer = GetComponent<SpriteRenderer>();
        if (spriteRenderer == null)
            spriteRenderer = GetComponentInChildren<SpriteRenderer>();
        
        col = GetComponent<Collider2D>();
        controller = GetComponent<PlayerController>();
        slimeController = GetComponent<SlimeController>();
        animator = GetComponent<ProceduralCharacterAnimator>();
        nameCanvas = GetComponentInChildren<Canvas>();
        rb = GetComponent<Rigidbody2D>();
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
        
        // Trigger hit reaction on animator
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
            
            // Color based on health percentage
            healthBarFill.color = healthPercent <= lowHealthThreshold 
                ? lowHealthColor 
                : Color.Lerp(lowHealthColor, normalHealthColor, (healthPercent - lowHealthThreshold) / (1f - lowHealthThreshold));
        }
    }

    #endregion

    #region Damage System

    /// <summary>
    /// RPC to take damage. Can be called by anyone (projectiles, etc.)
    /// </summary>
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void TakeDamageServerRpc(int amount, ulong attackerId)
    {
        if (!IsServer) return;

        // Apply damage
        int previousHealth = currentHealth.Value;
        currentHealth.Value -= amount;

        // Notify SlimeController for adaptive scaling
        if (slimeController != null && slimeController.IsSlime)
        {
            slimeController.OnDamageTaken(amount);
        }

        // Award ultimate charge to attacker
        AwardAttackerUltimate(attackerId, amount);

        // Check for death
        if (currentHealth.Value <= 0 && previousHealth > 0)
        {
            currentHealth.Value = 0;
            HandleDeath(attackerId);
        }

        Debug.Log($"[Health] Player {OwnerClientId} took {amount} damage from {attackerId}. Health: {currentHealth.Value}/{maxHealth}");
    }

    /// <summary>
    /// Convenience method that calls the ServerRpc
    /// </summary>
    public void TakeDamage(int amount, ulong attackerId)
    {
        TakeDamageServerRpc(amount, attackerId);
    }

    private void AwardAttackerUltimate(ulong attackerId, int damageDealt)
    {
        if (!IsServer) return;
        if (attackerId == OwnerClientId) return; // Don't award for self-damage
        
        // Find attacker's SlimeController and award ultimate charge
        if (NetworkManager.Singleton.ConnectedClients.TryGetValue(attackerId, out NetworkClient attackerClient))
        {
            if (attackerClient.PlayerObject != null)
            {
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
        // Award score to attacker
        if (attackerId != OwnerClientId)
        {
            AwardKillScore(attackerId);
        }

        // Start respawn sequence
        StartCoroutine(RespawnRoutine());
    }

    private void AwardKillScore(ulong attackerId)
    {
        // Find attacker and increment their score
        if (NetworkManager.Singleton.ConnectedClients.TryGetValue(attackerId, out NetworkClient attackerClient))
        {
            if (attackerClient.PlayerObject != null)
            {
                PlayerController attackerController = attackerClient.PlayerObject.GetComponent<PlayerController>();
                if (attackerController != null)
                {
                    attackerController.score.Value += 1;
                    
                    // Update leaderboard
                    LeaderboardManager lb = Object.FindFirstObjectByType<LeaderboardManager>();
                    if (lb != null)
                    {
                        lb.UpdateScore(attackerId, 100); // 100 points per kill
                    }
                }
            }
        }
        
        Debug.Log($"[Health] Player {attackerId} got a kill on player {OwnerClientId}");
    }

    private IEnumerator RespawnRoutine()
    {
        // 1. Hide player
        SetPlayerState(false);

        // 2. Wait for respawn delay
        yield return new WaitForSeconds(respawnDelay);

        // 3. Teleport to random spawn point
        GameObject[] spawnPoints = GameObject.FindGameObjectsWithTag("Respawn");
        if (spawnPoints.Length > 0)
        {
            Vector3 newPos = spawnPoints[Random.Range(0, spawnPoints.Length)].transform.position;
            TeleportClientRpc(newPos);
        }

        // 4. Reset health and show player
        currentHealth.Value = maxHealth;
        
        // 5. Reset slime scale (if in slime form)
        if (slimeController != null)
        {
            slimeController.ResetState();
        }
        
        SetPlayerState(true);
        
        Debug.Log($"[Health] Player {OwnerClientId} respawned");
    }

    [ClientRpc]
    private void TeleportClientRpc(Vector3 newPos)
    {
        if (rb != null) 
            rb.linearVelocity = Vector2.zero;

        if (IsOwner)
        {
            // Disable physics simulation during teleport
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

        // Non-owners also update position immediately
        transform.position = newPos;
        if (rb != null) 
            rb.position = newPos;
    }

    #endregion

    #region Player State Management

    private void SetPlayerState(bool isActive)
    {
        ToggleComponents(isActive);
        SetPlayerStateClientRpc(isActive);
    }

    [ClientRpc]
    private void SetPlayerStateClientRpc(bool isActive)
    {
        if (!IsServer) 
            ToggleComponents(isActive);
    }

    private void ToggleComponents(bool isActive)
    {
        if (spriteRenderer != null) 
            spriteRenderer.enabled = isActive;
        
        if (col != null) 
            col.enabled = isActive;
        
        if (controller != null) 
        {
            controller.enabled = isActive;
            controller.SetArrowVisibility(isActive);
        }
        
        if (nameCanvas != null) 
            nameCanvas.enabled = isActive;
        
        // Hide health bar when dead
        if (healthBarFill != null)
            healthBarFill.transform.parent.gameObject.SetActive(isActive);
    }

    #endregion

    #region Healing

    /// <summary>
    /// Server-only: Heal the player
    /// </summary>
    public void Heal(int amount)
    {
        if (!IsServer) return;
        
        currentHealth.Value = Mathf.Min(maxHealth, currentHealth.Value + amount);
        Debug.Log($"[Health] Player {OwnerClientId} healed for {amount}. Health: {currentHealth.Value}/{maxHealth}");
    }

    /// <summary>
    /// Server-only: Set health to max
    /// </summary>
    public void FullHeal()
    {
        if (!IsServer) return;
        
        currentHealth.Value = maxHealth;
    }

    #endregion

    #region Cleanup

    public override void OnDestroy()
    {
        currentHealth.OnValueChanged -= OnHealthChanged;
        base.OnDestroy();
    }

    #endregion

    #region Public Properties

    public int MaxHealth => maxHealth;
    public int CurrentHealth => currentHealth.Value;
    public float HealthPercent => (float)currentHealth.Value / maxHealth;
    public bool IsDead => currentHealth.Value <= 0;

    #endregion
}