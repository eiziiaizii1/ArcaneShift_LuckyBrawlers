using Unity.Netcode;
using UnityEngine;

/// <summary>
/// SlimeController handles the Slime form's unique mechanics:
/// - When slime takes damage: gets SMALLER, moves FASTER, deals LESS damage
/// - Directly scales the SlimeVisual child object
/// - Directly scales the BoxCollider2D on the parent
/// 
/// STRUCTURE EXPECTED:
/// Player (has BoxCollider2D, this script)
///   └── SlimeVisual (child object with SpriteRenderer)
///   └── WizardVisual (child object with SpriteRenderer)
/// 
/// Attach to Player prefab.
/// </summary>
public class SlimeController : NetworkBehaviour
{
    #region Network Variables
    
    /// <summary>
    /// Current scale multiplier (1.0 = full size, 0.3 = minimum)
    /// Synced across network so all clients see the same size
    /// </summary>
    public NetworkVariable<float> currentScale = new NetworkVariable<float>(
        1.0f,
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

    [Header("=== REFERENCES (Assign in Inspector) ===")]
    [Tooltip("The SlimeVisual child object that will be scaled")]
    [SerializeField] private Transform slimeVisual;
    
    [Tooltip("The BoxCollider2D on the player (usually on parent)")]
    [SerializeField] private BoxCollider2D boxCollider;

    [Header("=== SCALE SETTINGS ===")]
    [Tooltip("Minimum scale when fully damaged (0.3 = 30% size)")]
    [SerializeField] private float minScale = 0.3f;
    
    [Tooltip("Maximum/starting scale (1.0 = 100% size)")]
    [SerializeField] private float maxScale = 1.0f;
    
    [Tooltip("How much scale is lost per point of damage")]
    [SerializeField] private float scalePerDamage = 0.007f;
    
    [Tooltip("How fast the visual scales (for smooth transition)")]
    [SerializeField] private float scaleSpeed = 10f;

    [Header("=== SPEED SETTINGS (Smaller = Faster) ===")]
    [Tooltip("Speed multiplier at minimum scale (smallest slime)")]
    [SerializeField] private float speedAtMinScale = 2.0f;
    
    [Tooltip("Speed multiplier at maximum scale (largest slime)")]
    [SerializeField] private float speedAtMaxScale = 1.0f;

    [Header("=== DAMAGE SETTINGS (Smaller = Weaker) ===")]
    [Tooltip("Damage multiplier at minimum scale")]
    [SerializeField] private float damageAtMinScale = 0.4f;
    
    [Tooltip("Damage multiplier at maximum scale")]
    [SerializeField] private float damageAtMaxScale = 1.0f;
    
    [Tooltip("Base gloop damage before scaling")]
    [SerializeField] private int gloopBaseDamage = 25;

    [Header("=== GLOOP PROJECTILE ===")]
    [SerializeField] private GameObject gloopPrefab;
    [SerializeField] private float gloopSpeed = 7f;

    [Header("=== EFFECTS ===")]
    [SerializeField] private GameObject transformVFX;
    [SerializeField] private AudioClip transformSound;

    #endregion

    #region Private Fields

    // Store original values to scale from
    private Vector3 originalVisualScale;
    private Vector2 originalColliderSize;
    private Vector2 originalColliderOffset;
    
    // Current visual scale (for smooth lerping)
    private float displayScale = 1f;
    
    private AudioSource audioSource;
    private ProceduralCharacterAnimator animator;

    #endregion

    #region Unity Lifecycle

    private void Awake()
    {
        // Auto-find references if not assigned
        if (slimeVisual == null)
            slimeVisual = transform.Find("SlimeVisual");
        
        if (boxCollider == null)
            boxCollider = GetComponent<BoxCollider2D>();
        
        animator = GetComponent<ProceduralCharacterAnimator>();
        
        audioSource = GetComponent<AudioSource>();
        if (audioSource == null)
            audioSource = gameObject.AddComponent<AudioSource>();
        
        // Store original scales
        if (slimeVisual != null)
            originalVisualScale = slimeVisual.localScale;
        
        if (boxCollider != null)
        {
            originalColliderSize = boxCollider.size;
            originalColliderOffset = boxCollider.offset;
        }
    }

    public override void OnNetworkSpawn()
    {
        // Subscribe to network variable changes
        currentScale.OnValueChanged += OnScaleChanged;
        isSlimeForm.OnValueChanged += OnFormChanged;
        
        // Initialize display scale
        displayScale = currentScale.Value;
        
        // Apply initial state
        if (isSlimeForm.Value)
        {
            ApplyScale(currentScale.Value);
        }
        
        Debug.Log($"[SlimeController] Spawned. IsSlime: {isSlimeForm.Value}, Scale: {currentScale.Value:F2}");
    }

    public override void OnNetworkDespawn()
    {
        currentScale.OnValueChanged -= OnScaleChanged;
        isSlimeForm.OnValueChanged -= OnFormChanged;
    }

    private void Update()
    {
        // Smoothly interpolate visual scale
        if (isSlimeForm.Value && slimeVisual != null)
        {
            displayScale = Mathf.Lerp(displayScale, currentScale.Value, scaleSpeed * Time.deltaTime);
            ApplyScale(displayScale);
        }
    }

    #endregion

    #region Scale Application (THE KEY PART)

    /// <summary>
    /// Directly applies scale to the visual child and collider
    /// </summary>
    private void ApplyScale(float scale)
    {
        // 1. Scale the SlimeVisual child object
        if (slimeVisual != null && slimeVisual.gameObject.activeSelf)
        {
            slimeVisual.localScale = originalVisualScale * scale;
        }
        
        // 2. Scale the BoxCollider2D
        if (boxCollider != null && isSlimeForm.Value)
        {
            boxCollider.size = originalColliderSize * scale;
            // Adjust offset to keep collider grounded (optional)
            // boxCollider.offset = originalColliderOffset * scale;
        }
    }

    /// <summary>
    /// Called when the network scale value changes
    /// </summary>
    private void OnScaleChanged(float oldScale, float newScale)
    {
        Debug.Log($"[SlimeController] Scale changed: {oldScale:F2} -> {newScale:F2} | Speed: {GetSpeedMultiplier():F2}x | Damage: {GetDamageMultiplier():F2}x");
        
        // Update leaderboard
        UpdateLeaderboardState();
    }

    #endregion

    #region Damage Handling (Called by Health.cs)

    /// <summary>
    /// Called by Health.cs when slime takes damage.
    /// Reduces scale, making slime smaller but faster.
    /// </summary>
    public void OnDamageTaken(int damageAmount)
    {
        if (!IsServer) return;
        if (!isSlimeForm.Value) return; // Only affects slime form
        
        // Calculate new scale
        float scaleReduction = damageAmount * scalePerDamage;
        float newScale = Mathf.Max(minScale, currentScale.Value - scaleReduction);
        
        // Update network variable (this triggers OnScaleChanged on all clients)
        currentScale.Value = newScale;
        
        Debug.Log($"[SlimeController] Took {damageAmount} damage. New scale: {newScale:F2}");
    }

    /// <summary>
    /// Called when dealing damage (for ultimate charging etc)
    /// </summary>
    public void OnDamageDealt(int damageAmount)
    {
        // Can be used for ultimate charging if needed
    }

    #endregion

    #region Speed Multiplier (Smaller = Faster)

    /// <summary>
    /// Returns speed multiplier based on current scale.
    /// Called by PlayerController for movement speed.
    /// </summary>
    public float GetSpeedMultiplier()
    {
        if (!isSlimeForm.Value) return 1f;
        
        // Normalize: 0 at minScale, 1 at maxScale
        float t = (currentScale.Value - minScale) / (maxScale - minScale);
        
        // Interpolate: small = fast, big = normal
        return Mathf.Lerp(speedAtMinScale, speedAtMaxScale, t);
    }

    #endregion

    #region Damage Multiplier (Smaller = Weaker)

    /// <summary>
    /// Returns damage multiplier based on current scale.
    /// </summary>
    public float GetDamageMultiplier()
    {
        if (!isSlimeForm.Value) return 1f;
        
        // Normalize: 0 at minScale, 1 at maxScale
        float t = (currentScale.Value - minScale) / (maxScale - minScale);
        
        // Interpolate: small = weak, big = strong
        return Mathf.Lerp(damageAtMinScale, damageAtMaxScale, t);
    }

    /// <summary>
    /// Returns the actual gloop damage after scaling
    /// </summary>
    public int GetScaledGloopDamage()
    {
        return Mathf.RoundToInt(gloopBaseDamage * GetDamageMultiplier());
    }

    #endregion

    #region Projectile Settings

    /// <summary>
    /// Get projectile settings for shooting
    /// </summary>
    public (GameObject prefab, float speed, int damage) GetProjectileSettings(GameObject defaultFireball, float defaultSpeed)
    {
        if (isSlimeForm.Value && gloopPrefab != null)
        {
            return (gloopPrefab, gloopSpeed, GetScaledGloopDamage());
        }
        return (defaultFireball, defaultSpeed, 25);
    }

    /// <summary>
    /// Check if should use gloop projectile
    /// </summary>
    public bool ShouldUseGloop()
    {
        return isSlimeForm.Value && gloopPrefab != null;
    }

    #endregion

    #region Form Switching

    /// <summary>
    /// Transform to slime form (Server only)
    /// </summary>
    public void TransformToSlime()
    {
        if (!IsServer) return;
        
        isSlimeForm.Value = true;
        currentScale.Value = maxScale; // Reset scale
        displayScale = maxScale;
        
        PlayTransformEffectClientRpc();
        Debug.Log($"[SlimeController] Player {OwnerClientId} transformed to SLIME");
    }

    /// <summary>
    /// Transform to wizard form (Server only)
    /// </summary>
    public void TransformToWizard()
    {
        if (!IsServer) return;
        
        // Reset collider to original size before switching
        if (boxCollider != null)
        {
            boxCollider.size = originalColliderSize;
            boxCollider.offset = originalColliderOffset;
        }
        
        isSlimeForm.Value = false;
        currentScale.Value = maxScale;
        displayScale = maxScale;
        
        PlayTransformEffectClientRpc();
        Debug.Log($"[SlimeController] Player {OwnerClientId} transformed to WIZARD");
    }

    /// <summary>
    /// Toggle between forms (Server only)
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
        
        // Reset display scale when changing forms
        displayScale = currentScale.Value;
        
        // Sync with animator for visual switching
        if (animator != null)
        {
            animator.SetFormState(newValue);
        }
        else
        {
            // Fallback: directly control visuals
            Transform wizardVis = transform.Find("WizardVisual");
            if (wizardVis != null) wizardVis.gameObject.SetActive(!newValue);
            if (slimeVisual != null) slimeVisual.gameObject.SetActive(newValue);
        }
        
        // Apply or reset collider
        if (newValue)
        {
            ApplyScale(currentScale.Value);
        }
        else
        {
            // Reset collider when becoming wizard
            if (boxCollider != null)
            {
                boxCollider.size = originalColliderSize;
                boxCollider.offset = originalColliderOffset;
            }
        }
        
        UpdateLeaderboardState();
    }

    [ClientRpc]
    private void PlayTransformEffectClientRpc()
    {
        if (transformVFX != null)
            Instantiate(transformVFX, transform.position, Quaternion.identity);
        
        if (transformSound != null && audioSource != null)
            audioSource.PlayOneShot(transformSound);
    }

    #endregion

    #region State Management

    /// <summary>
    /// Reset to full size (called on respawn)
    /// </summary>
    public void ResetState()
    {
        if (!IsServer) return;
        
        currentScale.Value = maxScale;
        displayScale = maxScale;
        
        // Reset collider
        if (boxCollider != null)
        {
            boxCollider.size = originalColliderSize;
            boxCollider.offset = originalColliderOffset;
        }
        
        Debug.Log($"[SlimeController] State reset for player {OwnerClientId}");
    }

    private void UpdateLeaderboardState()
    {
        if (!IsServer) return;
        
        LeaderboardManager lb = Object.FindFirstObjectByType<LeaderboardManager>();
        if (lb != null)
        {
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

    #region Public Properties

    public bool IsSlime => isSlimeForm.Value;
    public float CurrentScale => currentScale.Value;
    public float MinScale => minScale;
    public float MaxScale => maxScale;
    public float SpeedMultiplier => GetSpeedMultiplier();
    public float DamageMultiplier => GetDamageMultiplier();

    #endregion

    #region Debug Gizmos

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (!Application.isPlaying) return;
        if (!isSlimeForm.Value) return;

        // Draw scale info
        UnityEditor.Handles.Label(
            transform.position + Vector3.up * 2f,
            $"Scale: {currentScale.Value:F2}\n" +
            $"Speed: {GetSpeedMultiplier():F2}x\n" +
            $"Damage: {GetDamageMultiplier():F2}x"
        );
    }
#endif

    #endregion
}