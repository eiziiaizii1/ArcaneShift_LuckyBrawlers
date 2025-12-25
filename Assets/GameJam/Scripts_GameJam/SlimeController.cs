using Unity.Netcode;
using UnityEngine;
using System.Collections;

/// <summary>
/// SlimeController handles the Slime form's unique mechanics:
/// - Adaptive Scaling: As damage is taken, the slime becomes smaller but faster
/// - Gloop Projectile: Different projectile type when in slime form
/// - Ultimate Meter: Builds up on damage dealt, triggers AOE attack
/// 
/// Attach to Player prefab alongside PlayerController.
/// </summary>
public class SlimeController : NetworkBehaviour
{
    #region Network Variables
    
    /// <summary>
    /// Current scale of the slime (1.0 = full size, 0.3 = minimum)
    /// Decreases as damage is taken, increases speed
    /// </summary>
    public NetworkVariable<float> currentScale = new NetworkVariable<float>(
        1.0f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    /// <summary>
    /// Ultimate meter (0-100). When full, player can trigger AOE attack.
    /// Builds up when dealing damage to other players.
    /// </summary>
    public NetworkVariable<float> ultimateMeter = new NetworkVariable<float>(
        0f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    /// <summary>
    /// Whether the player is currently in slime form
    /// </summary>
    public NetworkVariable<bool> isSlimeForm = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    #endregion

    #region Inspector Fields

    [Header("Adaptive Scaling Settings")]
    [Tooltip("Minimum scale the slime can shrink to")]
    [SerializeField] private float minScale = 0.3f;
    
    [Tooltip("Maximum scale (starting scale)")]
    [SerializeField] private float maxScale = 1.0f;
    
    [Tooltip("How much scale decreases per damage point taken")]
    [SerializeField] private float scalePerDamage = 0.007f; // ~70 damage to reach min scale
    
    [Tooltip("Speed multiplier at minimum scale")]
    [SerializeField] private float maxSpeedMultiplier = 2.0f;
    
    [Tooltip("Speed multiplier at maximum scale")]
    [SerializeField] private float minSpeedMultiplier = 1.0f;

    [Header("Ultimate Settings")]
    [Tooltip("Ultimate charge per damage dealt")]
    [SerializeField] private float ultimateChargePerDamage = 2f;
    
    [Tooltip("Maximum ultimate charge")]
    [SerializeField] private float maxUltimateCharge = 100f;
    
    [Tooltip("AOE damage when ultimate is used")]
    [SerializeField] private int ultimateAOEDamage = 50;
    
    [Tooltip("AOE radius when ultimate is used")]
    [SerializeField] private float ultimateAOERadius = 5f;
    
    [Tooltip("Cooldown after using ultimate (seconds)")]
    [SerializeField] private float ultimateCooldown = 1f;

    [Header("Gloop Projectile")]
    [Tooltip("Prefab for slime's gloop projectile (optional, uses fireball if null)")]
    [SerializeField] private GameObject gloopPrefab;
    
    [Tooltip("Speed of gloop projectile (slower than fireball)")]
    [SerializeField] private float gloopSpeed = 7f;
    
    [Tooltip("Damage of gloop projectile")]
    [SerializeField] private int gloopDamage = 20;

    [Header("Visual Effects")]
    [Tooltip("Particle effect for slime transformation")]
    [SerializeField] private GameObject transformVFX;
    
    [Tooltip("Particle effect for ultimate AOE")]
    [SerializeField] private GameObject ultimateVFX;
    
    [Tooltip("Sound effect for transformation")]
    [SerializeField] private AudioClip transformSound;
    
    [Tooltip("Sound effect for ultimate")]
    [SerializeField] private AudioClip ultimateSound;

    [Header("References")]
    [SerializeField] private Transform visualTransform; // The child sprite to scale
    [SerializeField] private PlayerController playerController;
    [SerializeField] private ProceduralCharacterAnimator animator;

    #endregion

    #region Private Fields

    private float baseSpeed;
    private bool ultimateReady = true;
    private AudioSource audioSource;

    #endregion

    #region Unity Lifecycle

    private void Awake()
    {
        // Get references
        if (playerController == null)
            playerController = GetComponent<PlayerController>();
        
        if (animator == null)
            animator = GetComponent<ProceduralCharacterAnimator>();
        
        audioSource = GetComponent<AudioSource>();
        if (audioSource == null)
            audioSource = gameObject.AddComponent<AudioSource>();
    }

    public override void OnNetworkSpawn()
    {
        // Subscribe to value changes
        currentScale.OnValueChanged += OnScaleChanged;
        isSlimeForm.OnValueChanged += OnFormChanged;
        ultimateMeter.OnValueChanged += OnUltimateChanged;

        // Initial visual update
        UpdateVisualScale(currentScale.Value);
        
        // Sync animator's form state if available
        if (animator != null && IsServer)
        {
            animator.isSlimeForm.Value = isSlimeForm.Value;
        }

        Debug.Log($"[SlimeController] Spawned. Form: {(isSlimeForm.Value ? "Slime" : "Wizard")}, Scale: {currentScale.Value}");
    }

    public override void OnNetworkDespawn()
    {
        currentScale.OnValueChanged -= OnScaleChanged;
        isSlimeForm.OnValueChanged -= OnFormChanged;
        ultimateMeter.OnValueChanged -= OnUltimateChanged;
    }

    private void Update()
    {
        if (!IsOwner) return;

        // Check for ultimate input (Q key by default)
        if (Input.GetKeyDown(KeyCode.Q) && isSlimeForm.Value)
        {
            TryUseUltimate();
        }
    }

    #endregion

    #region Form Management

    /// <summary>
    /// Server-only: Transform player into slime form
    /// </summary>
    public void TransformToSlime()
    {
        if (!IsServer) return;
        
        isSlimeForm.Value = true;
        currentScale.Value = maxScale; // Reset scale on transformation
        
        // Sync with animator
        if (animator != null)
            animator.SetSlimeForm(true);
        
        // Trigger VFX on all clients
        PlayTransformEffectClientRpc();
        
        Debug.Log($"[SlimeController] Player {OwnerClientId} transformed to SLIME");
    }

    /// <summary>
    /// Server-only: Transform player back to wizard form
    /// </summary>
    public void TransformToWizard()
    {
        if (!IsServer) return;
        
        isSlimeForm.Value = false;
        currentScale.Value = maxScale; // Reset scale
        
        // Sync with animator
        if (animator != null)
            animator.SetSlimeForm(false);
        
        // Trigger VFX on all clients
        PlayTransformEffectClientRpc();
        
        Debug.Log($"[SlimeController] Player {OwnerClientId} transformed to WIZARD");
    }

    /// <summary>
    /// Server-only: Toggle between forms
    /// </summary>
    public void ToggleForm()
    {
        if (!IsServer) return;
        
        if (isSlimeForm.Value)
            TransformToWizard();
        else
            TransformToSlime();
    }

    private void OnFormChanged(bool oldValue, bool newValue)
    {
        Debug.Log($"[SlimeController] Form changed: {(oldValue ? "Slime" : "Wizard")} -> {(newValue ? "Slime" : "Wizard")}");
        
        // Update leaderboard state
        UpdateLeaderboardState();
    }

    [ClientRpc]
    private void PlayTransformEffectClientRpc()
    {
        // Play VFX
        if (transformVFX != null)
        {
            Instantiate(transformVFX, transform.position, Quaternion.identity);
        }
        
        // Play sound
        if (transformSound != null && audioSource != null)
        {
            audioSource.PlayOneShot(transformSound);
        }
    }

    #endregion

    #region Adaptive Scaling

    /// <summary>
    /// Called when the slime takes damage. Reduces scale and increases speed.
    /// Server-only.
    /// </summary>
    public void OnDamageTaken(int damageAmount)
    {
        if (!IsServer || !isSlimeForm.Value) return;

        // Calculate new scale
        float scaleReduction = damageAmount * scalePerDamage;
        float newScale = Mathf.Max(minScale, currentScale.Value - scaleReduction);
        currentScale.Value = newScale;
        
        Debug.Log($"[SlimeController] Damage taken: {damageAmount}, New scale: {newScale:F2}");
    }

    /// <summary>
    /// Called when the slime deals damage. Charges ultimate meter.
    /// Server-only.
    /// </summary>
    public void OnDamageDealt(int damageAmount)
    {
        if (!IsServer) return;

        float chargeGain = damageAmount * ultimateChargePerDamage;
        ultimateMeter.Value = Mathf.Min(maxUltimateCharge, ultimateMeter.Value + chargeGain);
        
        Debug.Log($"[SlimeController] Damage dealt: {damageAmount}, Ultimate charge: {ultimateMeter.Value:F0}%");
    }

    private void OnScaleChanged(float oldScale, float newScale)
    {
        UpdateVisualScale(newScale);
        UpdateLeaderboardState();
        
        Debug.Log($"[SlimeController] Scale changed: {oldScale:F2} -> {newScale:F2}");
    }

    private void UpdateVisualScale(float scale)
    {
        if (visualTransform != null)
        {
            visualTransform.localScale = Vector3.one * scale;
        }
        else
        {
            // Fallback: scale the main transform's children
            foreach (Transform child in transform)
            {
                if (child.GetComponent<Canvas>() == null) // Don't scale UI
                {
                    child.localScale = Vector3.one * scale;
                }
            }
        }
    }

    /// <summary>
    /// Get the current speed multiplier based on scale
    /// </summary>
    public float GetSpeedMultiplier()
    {
        if (!isSlimeForm.Value) return 1f;
        
        // Inverse relationship: smaller = faster
        float scaleNormalized = (currentScale.Value - minScale) / (maxScale - minScale);
        float speedMultiplier = Mathf.Lerp(maxSpeedMultiplier, minSpeedMultiplier, scaleNormalized);
        
        return speedMultiplier;
    }

    #endregion

    #region Ultimate Ability

    private void TryUseUltimate()
    {
        if (!IsOwner) return;
        
        if (ultimateMeter.Value >= maxUltimateCharge && ultimateReady)
        {
            UseUltimateServerRpc();
        }
        else
        {
            Debug.Log($"[SlimeController] Ultimate not ready. Charge: {ultimateMeter.Value:F0}%, Ready: {ultimateReady}");
        }
    }

    [ServerRpc]
    private void UseUltimateServerRpc()
    {
        if (ultimateMeter.Value < maxUltimateCharge) return;
        
        // Reset meter
        ultimateMeter.Value = 0f;
        
        // Find all players in AOE radius
        Collider2D[] hits = Physics2D.OverlapCircleAll(transform.position, ultimateAOERadius);
        
        foreach (var hit in hits)
        {
            // Skip self
            NetworkObject netObj = hit.GetComponent<NetworkObject>();
            if (netObj != null && netObj.OwnerClientId == OwnerClientId) continue;
            
            // Apply damage
            Health health = hit.GetComponent<Health>();
            if (health != null)
            {
                health.TakeDamage(ultimateAOEDamage, OwnerClientId);
                Debug.Log($"[SlimeController] Ultimate hit player {netObj?.OwnerClientId}");
            }
        }
        
        // Trigger VFX on all clients
        PlayUltimateEffectClientRpc(transform.position, ultimateAOERadius);
        
        // Start cooldown
        StartCoroutine(UltimateCooldownCoroutine());
        
        Debug.Log($"[SlimeController] Player {OwnerClientId} used ULTIMATE!");
    }

    private IEnumerator UltimateCooldownCoroutine()
    {
        ultimateReady = false;
        yield return new WaitForSeconds(ultimateCooldown);
        ultimateReady = true;
    }

    [ClientRpc]
    private void PlayUltimateEffectClientRpc(Vector3 position, float radius)
    {
        // Play VFX
        if (ultimateVFX != null)
        {
            GameObject vfx = Instantiate(ultimateVFX, position, Quaternion.identity);
            vfx.transform.localScale = Vector3.one * radius * 0.4f; // Scale VFX to match AOE
            Destroy(vfx, 2f);
        }
        
        // Play sound
        if (ultimateSound != null && audioSource != null)
        {
            audioSource.PlayOneShot(ultimateSound);
        }
    }

    private void OnUltimateChanged(float oldValue, float newValue)
    {
        // Could trigger UI update event here
        Debug.Log($"[SlimeController] Ultimate meter: {newValue:F0}%");
    }

    #endregion

    #region Gloop Projectile

    /// <summary>
    /// Get the appropriate projectile prefab and settings based on form
    /// </summary>
    public (GameObject prefab, float speed, int damage) GetProjectileSettings(GameObject defaultFireball, float defaultSpeed)
    {
        if (isSlimeForm.Value && gloopPrefab != null)
        {
            return (gloopPrefab, gloopSpeed, gloopDamage);
        }
        
        // Return default fireball settings
        return (defaultFireball, defaultSpeed, 25); // 25 is default fireball damage
    }

    /// <summary>
    /// Check if we should use gloop (slime form with gloop prefab available)
    /// </summary>
    public bool ShouldUseGloop()
    {
        return isSlimeForm.Value && gloopPrefab != null;
    }

    #endregion

    #region Leaderboard Integration

    private void UpdateLeaderboardState()
    {
        if (!IsServer) return;
        
        LeaderboardManager lb = Object.FindFirstObjectByType<LeaderboardManager>();
        if (lb != null)
        {
            // Update the player's state in the leaderboard
            for (int i = 0; i < lb.playerRoster.Count; i++)
            {
                if (lb.playerRoster[i].ClientId == OwnerClientId)
                {
                    var data = lb.playerRoster[i];
                    data.IsSlimeForm = isSlimeForm.Value;
                    data.CurrentScale = currentScale.Value;
                    lb.playerRoster[i] = data;
                    break;
                }
            }
        }
    }

    #endregion

    #region Public API

    /// <summary>
    /// Reset the slime state (called on respawn)
    /// </summary>
    public void ResetState()
    {
        if (!IsServer) return;
        
        currentScale.Value = maxScale;
        // Don't reset ultimate meter on death - it persists
        
        Debug.Log($"[SlimeController] State reset for player {OwnerClientId}");
    }

    /// <summary>
    /// Check if player is in slime form
    /// </summary>
    public bool IsSlime => isSlimeForm.Value;

    /// <summary>
    /// Get current scale (0.3 to 1.0)
    /// </summary>
    public float CurrentScale => currentScale.Value;

    /// <summary>
    /// Get ultimate charge percentage (0-100)
    /// </summary>
    public float UltimateCharge => ultimateMeter.Value;

    /// <summary>
    /// Check if ultimate is ready to use
    /// </summary>
    public bool IsUltimateReady => ultimateMeter.Value >= maxUltimateCharge && ultimateReady;

    #endregion

    #region Debug

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        // Draw ultimate AOE radius
        Gizmos.color = new Color(0, 1, 0, 0.3f);
        Gizmos.DrawWireSphere(transform.position, ultimateAOERadius);
    }
#endif

    #endregion
}