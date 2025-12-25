using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Handles SizeChange LuckyBox event scaling with separate modifiers for:
/// - Wizard visual
/// - Slime visual  
/// - Colliders
/// 
/// IMPORTANT: This script does NOT control slime adaptive scaling (damage → smaller).
/// That is handled by SlimeController. This script only handles the LuckyBox SizeChange event
/// and MULTIPLIES on top of the adaptive scaling, not replacing it.
/// 
/// Scaling Priority:
/// 1. Base Scale (from prefab)
/// 2. × Adaptive Scale (SlimeController - damage based, slime only)
/// 3. × LuckyBox SizeChange (this script - event based, all players)
/// 
/// Attach to Player prefab.
/// </summary>
public class SizeChangeHandler : NetworkBehaviour
{
    #region Inspector Fields

    [Header("Visual References")]
    [SerializeField] private Transform wizardVisual;
    [SerializeField] private Transform slimeVisual;

    [Header("Collider References")]
    [SerializeField] private Collider2D mainCollider;

    [Header("=== WIZARD SCALING (LuckyBox Event Only) ===")]
    [Tooltip("How much the Wizard visual scales during SizeChange event")]
    [SerializeField] private float wizardVisualScaleMultiplier = 1.0f;
    
    [Tooltip("How much the Wizard collider scales during SizeChange event")]
    [SerializeField] private float wizardColliderScaleMultiplier = 0.9f;

    [Header("=== SLIME SCALING (LuckyBox Event Only) ===")]
    [Tooltip("How much the Slime visual scales during SizeChange event (ON TOP of adaptive scaling)")]
    [SerializeField] private float slimeVisualScaleMultiplier = 1.1f;
    
    [Tooltip("How much the Slime collider scales during SizeChange event (ON TOP of adaptive scaling)")]
    [SerializeField] private float slimeColliderScaleMultiplier = 0.85f;

    [Header("=== GENERAL SETTINGS ===")]
    [SerializeField] private float minScale = 0.3f;
    [SerializeField] private float maxScale = 2.5f;
    [SerializeField] private float scaleTransitionSpeed = 8f;

    #endregion

    #region Private Fields

    // Base scales (from prefab, never changes)
    private Vector3 wizardBaseScale;
    private Vector3 slimeBaseScale;
    
    // Base collider sizes
    private Vector2 baseBoxColliderSize;
    private float baseCircleColliderRadius;
    private Vector2 baseCapsuleColliderSize;
    
    // Current LuckyBox size multiplier (1.0 when no event)
    private float currentLuckyBoxMultiplier = 1f;
    private float targetLuckyBoxMultiplier = 1f;
    
    // Component references
    private SlimeController slimeController;
    private ProceduralCharacterAnimator animator;
    
    // Collider type tracking
    private BoxCollider2D boxCollider;
    private CircleCollider2D circleCollider;
    private CapsuleCollider2D capsuleCollider;
    
    // Track last adaptive scale to detect changes
    private float lastAdaptiveScale = 1f;

    #endregion

    #region Unity Lifecycle

    private void Awake()
    {
        // Auto-find references
        if (wizardVisual == null)
            wizardVisual = transform.Find("WizardVisual");
        
        if (slimeVisual == null)
            slimeVisual = transform.Find("SlimeVisual");
        
        if (mainCollider == null)
            mainCollider = GetComponent<Collider2D>();
        
        slimeController = GetComponent<SlimeController>();
        animator = GetComponent<ProceduralCharacterAnimator>();
        
        // Store base scales (these NEVER change)
        if (wizardVisual != null)
            wizardBaseScale = wizardVisual.localScale;
        
        if (slimeVisual != null)
            slimeBaseScale = slimeVisual.localScale;
        
        CaptureBaseColliderSize();
    }

    private void CaptureBaseColliderSize()
    {
        if (mainCollider == null) return;
        
        boxCollider = mainCollider as BoxCollider2D;
        circleCollider = mainCollider as CircleCollider2D;
        capsuleCollider = mainCollider as CapsuleCollider2D;
        
        if (boxCollider != null)
        {
            baseBoxColliderSize = boxCollider.size;
        }
        else if (circleCollider != null)
        {
            baseCircleColliderRadius = circleCollider.radius;
        }
        else if (capsuleCollider != null)
        {
            baseCapsuleColliderSize = capsuleCollider.size;
        }
    }

    public override void OnNetworkSpawn()
    {
        // Subscribe to LuckyBox events only
        if (LuckyBox.SizeMultiplier != null)
        {
            LuckyBox.SizeMultiplier.OnValueChanged += OnLuckyBoxSizeChanged;
        }
        
        if (LuckyBox.ActiveGlobalEvent != null)
        {
            LuckyBox.ActiveGlobalEvent.OnValueChanged += OnGlobalEventChanged;
        }
        
        // Subscribe to slime adaptive scale changes
        if (slimeController != null)
        {
            slimeController.currentScale.OnValueChanged += OnAdaptiveScaleChanged;
            slimeController.isSlimeForm.OnValueChanged += OnFormChanged;
        }
        
        // Initial state
        targetLuckyBoxMultiplier = LuckyBox.IsSizeChangeActive() ? LuckyBox.GetSizeMultiplier() : 1f;
        currentLuckyBoxMultiplier = targetLuckyBoxMultiplier;
        
        ApplyAllScaling();
        
        Debug.Log("[SizeChangeHandler] Spawned and subscribed to events");
    }

    public override void OnNetworkDespawn()
    {
        if (LuckyBox.SizeMultiplier != null)
        {
            LuckyBox.SizeMultiplier.OnValueChanged -= OnLuckyBoxSizeChanged;
        }
        
        if (LuckyBox.ActiveGlobalEvent != null)
        {
            LuckyBox.ActiveGlobalEvent.OnValueChanged -= OnGlobalEventChanged;
        }
        
        if (slimeController != null)
        {
            slimeController.currentScale.OnValueChanged -= OnAdaptiveScaleChanged;
            slimeController.isSlimeForm.OnValueChanged -= OnFormChanged;
        }
    }

    private void Update()
    {
        // Smooth LuckyBox size transition
        if (!Mathf.Approximately(currentLuckyBoxMultiplier, targetLuckyBoxMultiplier))
        {
            currentLuckyBoxMultiplier = Mathf.Lerp(currentLuckyBoxMultiplier, targetLuckyBoxMultiplier, scaleTransitionSpeed * Time.deltaTime);
            
            if (Mathf.Abs(currentLuckyBoxMultiplier - targetLuckyBoxMultiplier) < 0.001f)
            {
                currentLuckyBoxMultiplier = targetLuckyBoxMultiplier;
            }
            
            ApplyAllScaling();
        }
    }

    #endregion

    #region Event Handlers

    private void OnLuckyBoxSizeChanged(float oldValue, float newValue)
    {
        if (LuckyBox.ActiveGlobalEvent.Value == ModifierType.SizeChange)
        {
            targetLuckyBoxMultiplier = newValue;
            Debug.Log($"[SizeChangeHandler] LuckyBox size changed: {newValue:F2}x");
        }
    }

    private void OnGlobalEventChanged(ModifierType oldEvent, ModifierType newEvent)
    {
        if (newEvent == ModifierType.SizeChange)
        {
            targetLuckyBoxMultiplier = LuckyBox.GetSizeMultiplier();
            Debug.Log($"[SizeChangeHandler] SizeChange event started: {targetLuckyBoxMultiplier:F2}x");
        }
        else if (oldEvent == ModifierType.SizeChange)
        {
            targetLuckyBoxMultiplier = 1f;
            Debug.Log("[SizeChangeHandler] SizeChange event ended");
        }
    }

    private void OnAdaptiveScaleChanged(float oldScale, float newScale)
    {
        // Slime took damage and shrunk - reapply scaling
        lastAdaptiveScale = newScale;
        ApplyAllScaling();
        Debug.Log($"[SizeChangeHandler] Adaptive scale changed: {newScale:F2}");
    }

    private void OnFormChanged(bool oldForm, bool newForm)
    {
        // Form changed - reapply scaling with correct multipliers
        ApplyAllScaling();
        Debug.Log($"[SizeChangeHandler] Form changed to {(newForm ? "Slime" : "Wizard")}");
    }

    #endregion

    #region Scale Application

    /// <summary>
    /// Apply all scaling: Base × Adaptive (slime only) × LuckyBox
    /// </summary>
    private void ApplyAllScaling()
    {
        bool isSlime = IsCurrentlySlime();
        
        if (isSlime)
        {
            ApplySlimeScaling();
        }
        else
        {
            ApplyWizardScaling();
        }
        
        ApplyColliderScaling(isSlime);
    }

    private void ApplyWizardScaling()
    {
        if (wizardVisual == null) return;
        if (!wizardVisual.gameObject.activeSelf) return;
        
        // Wizard: Base × LuckyBox (no adaptive scaling)
        float luckyBoxScale = currentLuckyBoxMultiplier * wizardVisualScaleMultiplier;
        luckyBoxScale = Mathf.Clamp(luckyBoxScale, minScale, maxScale);
        
        Vector3 finalScale = wizardBaseScale * luckyBoxScale;
        wizardVisual.localScale = finalScale;
    }

    private void ApplySlimeScaling()
    {
        if (slimeVisual == null) return;
        if (!slimeVisual.gameObject.activeSelf) return;
        
        // Get adaptive scale from SlimeController (damage-based shrinking)
        float adaptiveScale = 1f;
        if (slimeController != null)
        {
            adaptiveScale = slimeController.CurrentScale; // 0.3 to 1.0
        }
        
        // Slime: Base × Adaptive × LuckyBox
        // Adaptive handles damage shrinking
        // LuckyBox handles event-based size change
        float luckyBoxScale = currentLuckyBoxMultiplier * slimeVisualScaleMultiplier;
        float totalScale = adaptiveScale * luckyBoxScale;
        totalScale = Mathf.Clamp(totalScale, minScale, maxScale);
        
        Vector3 finalScale = slimeBaseScale * totalScale;
        slimeVisual.localScale = finalScale;
        
        // Debug
        if (Mathf.Abs(adaptiveScale - lastAdaptiveScale) > 0.01f || 
            Mathf.Abs(currentLuckyBoxMultiplier - 1f) > 0.01f)
        {
            Debug.Log($"[SizeChangeHandler] Slime scale: Base × {adaptiveScale:F2} (adaptive) × {luckyBoxScale:F2} (lucky) = {totalScale:F2}");
        }
        
        lastAdaptiveScale = adaptiveScale;
    }

    private void ApplyColliderScaling(bool isSlime)
    {
        if (mainCollider == null) return;
        
        // Get adaptive scale for slime
        float adaptiveScale = 1f;
        if (isSlime && slimeController != null)
        {
            adaptiveScale = slimeController.CurrentScale;
        }
        
        // Choose form-specific collider multiplier
        float formColliderMult = isSlime ? slimeColliderScaleMultiplier : wizardColliderScaleMultiplier;
        
        // Total collider scale: Adaptive × LuckyBox × FormMultiplier
        float totalColliderScale = adaptiveScale * currentLuckyBoxMultiplier * formColliderMult;
        totalColliderScale = Mathf.Clamp(totalColliderScale, minScale, maxScale);
        
        // Apply to collider
        if (boxCollider != null)
        {
            boxCollider.size = baseBoxColliderSize * totalColliderScale;
        }
        else if (circleCollider != null)
        {
            circleCollider.radius = baseCircleColliderRadius * totalColliderScale;
        }
        else if (capsuleCollider != null)
        {
            capsuleCollider.size = baseCapsuleColliderSize * totalColliderScale;
        }
    }

    #endregion

    #region Helpers

    private bool IsCurrentlySlime()
    {
        if (slimeController != null)
            return slimeController.IsSlime;
        
        if (animator != null)
            return animator.IsCurrentlySlime;
        
        return false;
    }

    /// <summary>
    /// Force refresh all scaling (call after respawn or major state change)
    /// </summary>
    public void RefreshScale()
    {
        ApplyAllScaling();
    }

    /// <summary>
    /// Reset LuckyBox scaling to 1.0 (called when SizeChange event ends)
    /// </summary>
    public void ResetToBaseScale()
    {
        targetLuckyBoxMultiplier = 1f;
        // Don't reset currentLuckyBoxMultiplier - let it transition smoothly
    }

    #endregion

    #region Public Properties

    /// <summary>
    /// Current LuckyBox size multiplier (1.0 when no event active)
    /// </summary>
    public float CurrentLuckyBoxMultiplier => currentLuckyBoxMultiplier;
    
    /// <summary>
    /// Get the total visual scale for the current form
    /// </summary>
    public float GetTotalVisualScale()
    {
        bool isSlime = IsCurrentlySlime();
        float adaptive = (isSlime && slimeController != null) ? slimeController.CurrentScale : 1f;
        float formMult = isSlime ? slimeVisualScaleMultiplier : wizardVisualScaleMultiplier;
        return adaptive * currentLuckyBoxMultiplier * formMult;
    }
    
    /// <summary>
    /// Get the total collider scale for the current form
    /// </summary>
    public float GetTotalColliderScale()
    {
        bool isSlime = IsCurrentlySlime();
        float adaptive = (isSlime && slimeController != null) ? slimeController.CurrentScale : 1f;
        float formMult = isSlime ? slimeColliderScaleMultiplier : wizardColliderScaleMultiplier;
        return adaptive * currentLuckyBoxMultiplier * formMult;
    }

    #endregion

    #region Debug

#if UNITY_EDITOR
    [Header("Debug")]
    [SerializeField] private bool showDebugInfo = true;

    private void OnDrawGizmosSelected()
    {
        if (!showDebugInfo || !Application.isPlaying) return;
        
        if (mainCollider != null)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawWireCube(mainCollider.bounds.center, mainCollider.bounds.size);
        }
        
        bool isSlime = IsCurrentlySlime();
        float adaptive = (isSlime && slimeController != null) ? slimeController.CurrentScale : 1f;
        
        Vector3 labelPos = transform.position + Vector3.up * 2f;
        string info = $"Form: {(isSlime ? "Slime" : "Wizard")}\n" +
                      $"Adaptive: {adaptive:F2}x\n" +
                      $"LuckyBox: {currentLuckyBoxMultiplier:F2}x\n" +
                      $"Total Visual: {GetTotalVisualScale():F2}x";
        
        UnityEditor.Handles.Label(labelPos, info);
    }

    [ContextMenu("Test LuckyBox 0.5x")]
    private void TestHalfSize() { targetLuckyBoxMultiplier = 0.5f; }

    [ContextMenu("Test LuckyBox 1.5x")]
    private void TestLargeSize() { targetLuckyBoxMultiplier = 1.5f; }

    [ContextMenu("Reset LuckyBox Size")]
    private void TestResetSize() { targetLuckyBoxMultiplier = 1f; }
#endif

    #endregion
}