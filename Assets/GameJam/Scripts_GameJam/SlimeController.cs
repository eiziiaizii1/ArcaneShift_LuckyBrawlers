using Unity.Netcode;
using UnityEngine;

/// <summary>
/// SlimeController - Fixed Version
/// 
/// Handles the Slime form's unique mechanics:
/// - When slime takes damage: gets SMALLER, moves FASTER, deals LESS damage
/// - Works with SizeChangeHandler for LuckyBox scale events
/// 
/// STRUCTURE EXPECTED:
/// Player (has Collider2D, this script)
///   ├── WizardVisual (child with SpriteRenderer)
///   └── SlimeVisual (child with SpriteRenderer)
/// 
/// Attach to Player prefab.
/// </summary>
public class SlimeController : NetworkBehaviour
{
    #region Network Variables
    
    /// <summary>
    /// Current adaptive scale multiplier (1.0 = full size, 0.3 = minimum)
    /// This is the DAMAGE-BASED scaling, separate from LuckyBox scaling
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

    [Header("=== REFERENCES ===")]
    [SerializeField] private Transform slimeVisual;
    [SerializeField] private Transform wizardVisual;

    [Header("=== ADAPTIVE SCALE SETTINGS ===")]
    [Tooltip("Minimum scale when fully damaged (0.3 = 30% size)")]
    [SerializeField] private float minScale = 0.3f;
    
    [Tooltip("Maximum/starting scale (1.0 = 100% size)")]
    [SerializeField] private float maxScale = 1.0f;
    
    [Tooltip("How much scale is lost per point of damage")]
    [SerializeField] private float scalePerDamage = 0.007f;

    [Header("=== SPEED SETTINGS (Smaller = Faster) ===")]
    [SerializeField] private float speedAtMinScale = 2.0f;
    [SerializeField] private float speedAtMaxScale = 1.0f;

    [Header("=== DAMAGE SETTINGS (Smaller = Weaker) ===")]
    [SerializeField] private float damageAtMinScale = 0.4f;
    [SerializeField] private float damageAtMaxScale = 1.0f;
    [SerializeField] private int gloopBaseDamage = 25;

    [Header("=== GLOOP PROJECTILE ===")]
    [SerializeField] private GameObject gloopPrefab;
    [SerializeField] private float gloopSpeed = 7f;

    [Header("=== EFFECTS ===")]
    [SerializeField] private GameObject transformVFX;
    [SerializeField] private AudioClip transformSound;

    #endregion

    #region Private Fields

    private Vector3 slimeOriginalScale;
    private Vector3 wizardOriginalScale;
    private AudioSource audioSource;
    private ProceduralCharacterAnimator animator;
    private SizeChangeHandler sizeChangeHandler;

    #endregion

    #region Unity Lifecycle

    private void Awake()
    {
        // Auto-find references
        if (slimeVisual == null)
            slimeVisual = transform.Find("SlimeVisual");
        
        if (wizardVisual == null)
            wizardVisual = transform.Find("WizardVisual");
        
        animator = GetComponent<ProceduralCharacterAnimator>();
        sizeChangeHandler = GetComponent<SizeChangeHandler>();
        
        audioSource = GetComponent<AudioSource>();
        if (audioSource == null)
            audioSource = gameObject.AddComponent<AudioSource>();
        
        // Store original scales
        if (slimeVisual != null)
            slimeOriginalScale = slimeVisual.localScale;
        
        if (wizardVisual != null)
            wizardOriginalScale = wizardVisual.localScale;
    }

    public override void OnNetworkSpawn()
    {
        currentScale.OnValueChanged += OnScaleChanged;
        isSlimeForm.OnValueChanged += OnFormChanged;
        
        // Apply initial state
        ApplyFormState(isSlimeForm.Value);
        
        Debug.Log($"[SlimeController] Spawned. IsSlime: {isSlimeForm.Value}, Scale: {currentScale.Value:F2}");
    }

    public override void OnNetworkDespawn()
    {
        currentScale.OnValueChanged -= OnScaleChanged;
        isSlimeForm.OnValueChanged -= OnFormChanged;
    }

    #endregion

    #region Network Callbacks

    private void OnScaleChanged(float oldScale, float newScale)
    {
        Debug.Log($"[SlimeController] Adaptive scale changed: {oldScale:F2} -> {newScale:F2}");
        
        // Notify SizeChangeHandler to refresh (it handles the actual scaling)
        if (sizeChangeHandler != null)
        {
            sizeChangeHandler.RefreshScale();
        }
        
        // Update leaderboard
        UpdateLeaderboardState();
    }

    private void OnFormChanged(bool oldValue, bool newValue)
    {
        Debug.Log($"[SlimeController] Form changed: {(oldValue ? "Slime" : "Wizard")} -> {(newValue ? "Slime" : "Wizard")}");
        
        ApplyFormState(newValue);
        
        // Notify SizeChangeHandler to refresh scaling for new form
        if (sizeChangeHandler != null)
        {
            sizeChangeHandler.RefreshScale();
        }
        
        UpdateLeaderboardState();
    }

    private void ApplyFormState(bool isSlime)
    {
        // Update animator
        if (animator != null)
        {
            animator.SetFormState(isSlime);
        }
        else
        {
            // Fallback: directly control visuals
            if (wizardVisual != null)
                wizardVisual.gameObject.SetActive(!isSlime);
            
            if (slimeVisual != null)
                slimeVisual.gameObject.SetActive(isSlime);
        }
    }

    #endregion

    #region Damage Handling

    /// <summary>
    /// Called by Health.cs when slime takes damage.
    /// Reduces adaptive scale, making slime smaller but faster.
    /// </summary>
    public void OnDamageTaken(int damageAmount)
    {
        if (!IsServer) return;
        if (!isSlimeForm.Value) return;
        
        float scaleReduction = damageAmount * scalePerDamage;
        float newScale = Mathf.Max(minScale, currentScale.Value - scaleReduction);
        
        currentScale.Value = newScale;
        
        Debug.Log($"[SlimeController] Took {damageAmount} damage. Adaptive scale: {newScale:F2}");
    }

    /// <summary>
    /// Called when dealing damage (for ultimate charging etc)
    /// </summary>
    public void OnDamageDealt(int damageAmount)
    {
        // Can be used for ultimate charging if needed
    }

    #endregion

    #region Speed & Damage Multipliers

    /// <summary>
    /// Returns speed multiplier based on current adaptive scale.
    /// Smaller = Faster
    /// </summary>
    public float GetSpeedMultiplier()
    {
        if (!isSlimeForm.Value) return 1f;
        
        float t = (currentScale.Value - minScale) / (maxScale - minScale);
        return Mathf.Lerp(speedAtMinScale, speedAtMaxScale, t);
    }

    /// <summary>
    /// Returns damage multiplier based on current adaptive scale.
    /// Smaller = Weaker
    /// </summary>
    public float GetDamageMultiplier()
    {
        if (!isSlimeForm.Value) return 1f;
        
        float t = (currentScale.Value - minScale) / (maxScale - minScale);
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

    public (GameObject prefab, float speed, int damage) GetProjectileSettings(GameObject defaultFireball, float defaultSpeed)
    {
        if (isSlimeForm.Value && gloopPrefab != null)
        {
            return (gloopPrefab, gloopSpeed, GetScaledGloopDamage());
        }
        return (defaultFireball, defaultSpeed, 25);
    }

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
        currentScale.Value = maxScale;
        
        PlayTransformEffectClientRpc();
        Debug.Log($"[SlimeController] Player {OwnerClientId} transformed to SLIME");
    }

    /// <summary>
    /// Transform to wizard form (Server only)
    /// </summary>
    public void TransformToWizard()
    {
        if (!IsServer) return;
        
        isSlimeForm.Value = false;
        currentScale.Value = maxScale;
        
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

    #region Debug

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (!Application.isPlaying) return;
        if (!isSlimeForm.Value) return;

        UnityEditor.Handles.Label(
            transform.position + Vector3.up * 2f,
            $"Adaptive Scale: {currentScale.Value:F2}\n" +
            $"Speed Mult: {GetSpeedMultiplier():F2}x\n" +
            $"Damage Mult: {GetDamageMultiplier():F2}x"
        );
    }
#endif

    #endregion
}