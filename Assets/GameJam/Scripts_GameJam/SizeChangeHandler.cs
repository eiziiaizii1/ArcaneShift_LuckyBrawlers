using Unity.Netcode;
using UnityEngine;

/// <summary>
/// SizeChangeHandler - Fixed Version 2
/// 
/// Handles the LuckyBox SizeChange event by:
/// 1. Telling ProceduralCharacterAnimator the scale multiplier (it handles sprite scaling)
/// 2. Directly scaling the collider on the parent object
/// 
/// This prevents the "scale fight" where animator was overwriting our scale changes.
/// 
/// Scale values: 0.25x, 0.5x, 1.5x, 2x (set by LuckyBox)
/// 
/// Attach to Player prefab.
/// </summary>
public class SizeChangeHandler : NetworkBehaviour
{
    #region Inspector Fields

    [Header("Collider Reference (Auto-found if not assigned)")]
    [SerializeField] private Collider2D playerCollider;

    [Header("Scale Settings")]
    [SerializeField] private float scaleTransitionSpeed = 10f;

    #endregion

    #region Private Fields

    // Original collider sizes
    private Vector2 originalBoxSize;
    private float originalCircleRadius;
    private Vector2 originalCapsuleSize;
    
    // Current target scale from LuckyBox
    private float targetScaleMultiplier = 1f;
    private float currentScaleMultiplier = 1f;
    
    // Collider type references
    private BoxCollider2D boxCollider;
    private CircleCollider2D circleCollider;
    private CapsuleCollider2D capsuleCollider;

    // References
    private SlimeController slimeController;
    private ProceduralCharacterAnimator animator;

    #endregion

    #region Unity Lifecycle

    private void Awake()
    {
        // Auto-find collider
        if (playerCollider == null)
            playerCollider = GetComponent<Collider2D>();
        
        // Get references
        slimeController = GetComponent<SlimeController>();
        animator = GetComponent<ProceduralCharacterAnimator>();
        
        // Store original collider size
        StoreOriginalColliderSize();
        
        Debug.Log($"[SizeChangeHandler] Initialized - Collider: {playerCollider != null}, Animator: {animator != null}");
    }

    private void StoreOriginalColliderSize()
    {
        if (playerCollider == null) return;
        
        boxCollider = playerCollider as BoxCollider2D;
        circleCollider = playerCollider as CircleCollider2D;
        capsuleCollider = playerCollider as CapsuleCollider2D;
        
        if (boxCollider != null)
        {
            originalBoxSize = boxCollider.size;
            Debug.Log($"[SizeChangeHandler] BoxCollider2D original size: {originalBoxSize}");
        }
        else if (circleCollider != null)
        {
            originalCircleRadius = circleCollider.radius;
            Debug.Log($"[SizeChangeHandler] CircleCollider2D original radius: {originalCircleRadius}");
        }
        else if (capsuleCollider != null)
        {
            originalCapsuleSize = capsuleCollider.size;
            Debug.Log($"[SizeChangeHandler] CapsuleCollider2D original size: {originalCapsuleSize}");
        }
    }

    public override void OnNetworkSpawn()
    {
        // Subscribe to LuckyBox size changes
        if (LuckyBox.Instance != null)
        {
            LuckyBox.Instance.SizeMultiplier.OnValueChanged += OnLuckyBoxSizeChanged;
            LuckyBox.Instance.ActiveGlobalEvent.OnValueChanged += OnEventChanged;
            
            // Check if size change is already active
            if (LuckyBox.Instance.ActiveGlobalEvent.Value == ModifierType.SizeChange)
            {
                ApplyLuckyBoxScale(LuckyBox.Instance.SizeMultiplier.Value);
            }
        }
        
        // Subscribe to slime adaptive scale changes
        if (slimeController != null)
        {
            slimeController.currentScale.OnValueChanged += OnAdaptiveScaleChanged;
        }
        
        Debug.Log("[SizeChangeHandler] Network spawned");
    }

    public override void OnNetworkDespawn()
    {
        if (LuckyBox.Instance != null)
        {
            LuckyBox.Instance.SizeMultiplier.OnValueChanged -= OnLuckyBoxSizeChanged;
            LuckyBox.Instance.ActiveGlobalEvent.OnValueChanged -= OnEventChanged;
        }
        
        if (slimeController != null)
        {
            slimeController.currentScale.OnValueChanged -= OnAdaptiveScaleChanged;
        }
    }

    private void Update()
    {
        // Smoothly interpolate to target scale
        if (!Mathf.Approximately(currentScaleMultiplier, targetScaleMultiplier))
        {
            currentScaleMultiplier = Mathf.Lerp(currentScaleMultiplier, targetScaleMultiplier, scaleTransitionSpeed * Time.deltaTime);
            
            // Snap if close enough
            if (Mathf.Abs(currentScaleMultiplier - targetScaleMultiplier) < 0.01f)
            {
                currentScaleMultiplier = targetScaleMultiplier;
            }
            
            ApplyCurrentScale();
        }
    }

    #endregion

    #region Event Handlers

    private void OnLuckyBoxSizeChanged(float oldValue, float newValue)
    {
        if (LuckyBox.Instance != null && LuckyBox.Instance.ActiveGlobalEvent.Value == ModifierType.SizeChange)
        {
            targetScaleMultiplier = newValue;
            Debug.Log($"[SizeChangeHandler] LuckyBox size changed to: {newValue}x");
        }
    }

    private void OnEventChanged(ModifierType oldEvent, ModifierType newEvent)
    {
        if (newEvent == ModifierType.SizeChange)
        {
            targetScaleMultiplier = LuckyBox.Instance.SizeMultiplier.Value;
            Debug.Log($"[SizeChangeHandler] SizeChange event started: {targetScaleMultiplier}x");
        }
        else if (oldEvent == ModifierType.SizeChange)
        {
            targetScaleMultiplier = 1f;
            Debug.Log("[SizeChangeHandler] SizeChange event ended, resetting to 1x");
        }
    }

    private void OnAdaptiveScaleChanged(float oldValue, float newValue)
    {
        // When slime adaptive scale changes, refresh everything
        ApplyCurrentScale();
        Debug.Log($"[SizeChangeHandler] Adaptive scale changed: {newValue}");
    }

    #endregion

    #region Scale Application

    /// <summary>
    /// Apply scale from LuckyBox event
    /// </summary>
    public void ApplyLuckyBoxScale(float scaleMultiplier)
    {
        targetScaleMultiplier = scaleMultiplier;
        Debug.Log($"[SizeChangeHandler] ApplyLuckyBoxScale: {scaleMultiplier}x");
    }

    /// <summary>
    /// Reset scale back to original
    /// </summary>
    public void ResetScale()
    {
        targetScaleMultiplier = 1f;
        Debug.Log("[SizeChangeHandler] ResetScale called");
    }

    /// <summary>
    /// Apply the current scale multiplier
    /// </summary>
    private void ApplyCurrentScale()
    {
        // Calculate total scale (LuckyBox × Adaptive for slime)
        float totalScale = GetTotalScale();
        
        // Tell the animator to use this scale multiplier
        // The animator will apply it to the visuals
        if (animator != null)
        {
            animator.SetExternalScaleMultiplier(totalScale);
        }
        
        // Scale the collider directly
        ScaleCollider(totalScale);
    }

    private void ScaleCollider(float totalMultiplier)
    {
        if (playerCollider == null) return;
        
        if (boxCollider != null)
        {
            boxCollider.size = originalBoxSize * totalMultiplier;
        }
        else if (circleCollider != null)
        {
            circleCollider.radius = originalCircleRadius * totalMultiplier;
        }
        else if (capsuleCollider != null)
        {
            capsuleCollider.size = originalCapsuleSize * totalMultiplier;
        }
    }

    #endregion

    #region Public API

    /// <summary>
    /// Get current LuckyBox scale multiplier
    /// </summary>
    public float CurrentLuckyBoxScale => currentScaleMultiplier;

    /// <summary>
    /// Get total scale (LuckyBox × Adaptive for slime)
    /// </summary>
    public float GetTotalScale()
    {
        float total = currentScaleMultiplier;
        
        // If slime, multiply by adaptive scale
        if (slimeController != null && slimeController.IsSlime)
        {
            total *= slimeController.CurrentScale;
        }
        
        return total;
    }

    /// <summary>
    /// Force refresh the scale
    /// </summary>
    public void RefreshScale()
    {
        ApplyCurrentScale();
    }

    #endregion

    #region Debug

#if UNITY_EDITOR
    [Header("Debug")]
    [SerializeField] private bool showDebugGizmos = true;

    private void OnDrawGizmosSelected()
    {
        if (!showDebugGizmos || !Application.isPlaying) return;
        
        if (playerCollider != null)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawWireCube(playerCollider.bounds.center, playerCollider.bounds.size);
        }
        
        Vector3 labelPos = transform.position + Vector3.up * 2f;
        bool isSlime = slimeController != null && slimeController.IsSlime;
        float adaptive = isSlime ? slimeController.CurrentScale : 1f;
        
        string info = $"LuckyBox: {currentScaleMultiplier:F2}x\n" +
                      $"Adaptive: {adaptive:F2}x\n" +
                      $"Total: {GetTotalScale():F2}x";
        
        UnityEditor.Handles.Label(labelPos, info);
    }

    [ContextMenu("Test Scale 0.25x")]
    private void TestQuarterScale() { ApplyLuckyBoxScale(0.25f); }

    [ContextMenu("Test Scale 0.5x")]
    private void TestHalfScale() { ApplyLuckyBoxScale(0.5f); }

    [ContextMenu("Test Scale 1.5x")]
    private void TestOneAndHalfScale() { ApplyLuckyBoxScale(1.5f); }

    [ContextMenu("Test Scale 2x")]
    private void TestDoubleScale() { ApplyLuckyBoxScale(2f); }

    [ContextMenu("Reset Scale")]
    private void TestResetScale() { ResetScale(); }
#endif

    #endregion
}