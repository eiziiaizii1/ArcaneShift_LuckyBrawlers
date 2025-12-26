using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Procedural animation system for dual-form characters (Wizard/Slime).
/// 
/// FIXED VERSION: Now respects external scale multipliers from SizeChangeHandler
/// 
/// The animator applies its animation scales ON TOP of the external scale multiplier.
/// This prevents the "scale fight" where animator would overwrite LuckyBox scaling.
/// </summary>
public class ProceduralCharacterAnimator : NetworkBehaviour
{
    #region Inspector Fields
    
    [Header("Visual References")]
    [SerializeField] private Transform wizardVisual;
    [SerializeField] private Transform slimeVisual;

    [Header("Velocity Source")]
    [SerializeField] private Rigidbody2D rb;
    [SerializeField] private float movementThreshold = 0.1f;

    [Header("=== WIZARD SETTINGS ===")]
    [Header("Wizard Idle - Magical Float")]
    [SerializeField] private float wizardFloatAmplitude = 0.08f;
    [SerializeField] private float wizardFloatFrequency = 1.5f;
    [SerializeField] private float wizardIdleRotation = 3f;
    [SerializeField] private float wizardSecondaryWaveStrength = 0.3f;

    [Header("Wizard Moving - Momentum Tilt")]
    [SerializeField] private float wizardMaxTiltAngle = 15f;
    [SerializeField] private float wizardTiltSpeed = 8f;
    [SerializeField] private float wizardForwardLean = 0.1f;
    [SerializeField] private float wizardMoveBobAmount = 0.03f;
    [SerializeField] private float wizardMoveBobFrequency = 8f;

    [Header("=== SLIME SETTINGS ===")]
    [Header("Slime Idle - Breathing")]
    [SerializeField] private float slimeBreathAmplitude = 0.06f;
    [SerializeField] private float slimeBreathFrequency = 0.9f;
    [SerializeField] private float slimeIdleBobAmplitude = 0.04f;
    [SerializeField] private float slimeWobbleStrength = 0.02f;
    [SerializeField] private float slimeWobbleFrequency = 4f;

    [Header("Slime Moving - Bounce Cycle")]
    [SerializeField] private float slimeSquashAmount = 0.25f;
    [SerializeField] private float slimeStretchAmount = 0.2f;
    [SerializeField] private float slimeBounceBaseFrequency = 3f;
    [SerializeField] private float slimeBounceSpeedMultiplier = 0.5f;
    [SerializeField] private float slimeHopHeight = 0.15f;
    [SerializeField] private float slimeMoveTilt = 8f;

    [Header("Transition & Smoothing")]
    [SerializeField] private float stateBlendSpeed = 5f;
    [SerializeField] private float scaleSmoothing = 12f;
    [SerializeField] private float positionSmoothing = 15f;

    [Header("Form State (Network Synced)")]
    public NetworkVariable<bool> isSlimeForm = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    #endregion

    #region Private State

    // Base transforms (original prefab scale)
    private Vector3 wizardBasePosition;
    private Vector3 wizardBaseScale;
    private Vector3 slimeBasePosition;
    private Vector3 slimeBaseScale;

    // Animation state
    private float timeOffset;
    private float currentMovementBlend = 0f;
    private float currentTiltAngle = 0f;
    private float bouncePhase = 0f;
    private float lastBouncePhase = 0f;

    // Velocity tracking
    private Vector2 currentVelocity;
    private Vector2 smoothedVelocity;
    private float currentSpeed;

    // Master visibility state
    private bool masterVisibility = true;
    private bool currentFormIsSlime = false;

    // === EXTERNAL SCALE MULTIPLIER ===
    // This is set by SizeChangeHandler and incorporated into our scale calculations
    private float externalScaleMultiplier = 1f;

    #endregion

    #region Unity Lifecycle

    private void Awake()
    {
        if (rb == null)
            rb = GetComponent<Rigidbody2D>();

        if (wizardVisual == null)
            wizardVisual = transform.Find("WizardVisual");
        
        if (slimeVisual == null)
            slimeVisual = transform.Find("SlimeVisual");

        // Store base transforms (these are the ORIGINAL prefab scales)
        if (wizardVisual != null)
        {
            wizardBasePosition = wizardVisual.localPosition;
            wizardBaseScale = wizardVisual.localScale;
        }

        if (slimeVisual != null)
        {
            slimeBasePosition = slimeVisual.localPosition;
            slimeBaseScale = slimeVisual.localScale;
        }

        timeOffset = Random.Range(0f, 100f);
    }

    public override void OnNetworkSpawn()
    {
        isSlimeForm.OnValueChanged += OnNetworkFormChanged;
        
        currentFormIsSlime = isSlimeForm.Value;
        UpdateVisualActiveState();
        
        Debug.Log($"[ProceduralAnimator] Spawned as {(isSlimeForm.Value ? "Slime" : "Wizard")}");
    }

    public override void OnNetworkDespawn()
    {
        isSlimeForm.OnValueChanged -= OnNetworkFormChanged;
    }

    private void OnNetworkFormChanged(bool oldValue, bool newValue)
    {
        currentFormIsSlime = newValue;
        UpdateVisualActiveState();
        
        bouncePhase = 0f;
        currentTiltAngle = 0f;
        currentMovementBlend = 0f;
        
        Debug.Log($"[ProceduralAnimator] Network form changed to {(newValue ? "Slime" : "Wizard")}");
    }

    private void Update()
    {
        if (!masterVisibility) return;
        
        float time = Time.time + timeOffset;
        float deltaTime = Time.deltaTime;

        UpdateVelocityData();

        float targetBlend = Mathf.Clamp01(currentSpeed / 5f);
        if (currentSpeed < movementThreshold)
            targetBlend = 0f;
        
        currentMovementBlend = Mathf.Lerp(currentMovementBlend, targetBlend, stateBlendSpeed * deltaTime);

        if (currentFormIsSlime)
        {
            AnimateSlime(time, deltaTime);
        }
        else
        {
            AnimateWizard(time, deltaTime);
        }
    }

    #endregion

    #region External Scale Control (Called by SizeChangeHandler)

    /// <summary>
    /// Set the external scale multiplier from SizeChangeHandler.
    /// This multiplier is applied ON TOP of animation scales.
    /// </summary>
    public void SetExternalScaleMultiplier(float multiplier)
    {
        externalScaleMultiplier = multiplier;
        Debug.Log($"[ProceduralAnimator] External scale multiplier set to: {multiplier}x");
    }

    /// <summary>
    /// Get current external scale multiplier
    /// </summary>
    public float GetExternalScaleMultiplier()
    {
        return externalScaleMultiplier;
    }

    #endregion

    #region Visual State Control

    public void SetMasterVisibility(bool visible)
    {
        masterVisibility = visible;
        UpdateVisualActiveState();
        
        Debug.Log($"[ProceduralAnimator] Master visibility set to {visible}");
    }

    public void SetFormState(bool isSlime)
    {
        currentFormIsSlime = isSlime;
        
        if (IsServer && isSlimeForm.Value != isSlime)
        {
            isSlimeForm.Value = isSlime;
        }
        
        UpdateVisualActiveState();
        
        Debug.Log($"[ProceduralAnimator] Form state set to {(isSlime ? "Slime" : "Wizard")}");
    }

    public void SetSlimeForm(bool slime)
    {
        if (!IsServer)
        {
            Debug.LogWarning("[ProceduralAnimator] SetSlimeForm must be called on server!");
            return;
        }
        isSlimeForm.Value = slime;
    }

    public void ToggleForm()
    {
        if (!IsServer)
        {
            Debug.LogWarning("[ProceduralAnimator] ToggleForm must be called on server!");
            return;
        }
        isSlimeForm.Value = !isSlimeForm.Value;
    }

    private void UpdateVisualActiveState()
    {
        if (wizardVisual != null)
        {
            bool shouldShowWizard = masterVisibility && !currentFormIsSlime;
            wizardVisual.gameObject.SetActive(shouldShowWizard);
            
            if (shouldShowWizard)
            {
                wizardVisual.localPosition = wizardBasePosition;
                wizardVisual.localScale = wizardBaseScale * externalScaleMultiplier;
                wizardVisual.localRotation = Quaternion.identity;
            }
        }
        
        if (slimeVisual != null)
        {
            bool shouldShowSlime = masterVisibility && currentFormIsSlime;
            slimeVisual.gameObject.SetActive(shouldShowSlime);
            
            if (shouldShowSlime)
            {
                slimeVisual.localPosition = slimeBasePosition;
                slimeVisual.localScale = slimeBaseScale * externalScaleMultiplier;
                slimeVisual.localRotation = Quaternion.identity;
            }
        }
    }

    public void ForceRefreshVisuals()
    {
        UpdateVisualActiveState();
    }

    #endregion

    #region Velocity Tracking

    private void UpdateVelocityData()
    {
        if (rb != null)
        {
            currentVelocity = rb.linearVelocity;
        }
        else
        {
            currentVelocity = Vector2.zero;
        }

        smoothedVelocity = Vector2.Lerp(smoothedVelocity, currentVelocity, 10f * Time.deltaTime);
        currentSpeed = smoothedVelocity.magnitude;
    }

    #endregion

    #region Wizard Animation

    private void AnimateWizard(float time, float deltaTime)
    {
        if (wizardVisual == null || !wizardVisual.gameObject.activeSelf) return;

        Vector3 targetPosition = wizardBasePosition;
        
        // Animation scale is relative to 1.0 (no change)
        Vector3 animationScale = Vector3.one;
        float targetRotation = 0f;

        float idleWeight = 1f - currentMovementBlend;
        float moveWeight = currentMovementBlend;

        // Idle animation
        if (idleWeight > 0.01f)
        {
            float primaryWave = Mathf.Sin(time * wizardFloatFrequency * Mathf.PI * 2f);
            float secondaryWave = Mathf.Sin(time * wizardFloatFrequency * 1.7f * Mathf.PI * 2f);
            
            float floatOffset = primaryWave * wizardFloatAmplitude;
            floatOffset += secondaryWave * wizardFloatAmplitude * wizardSecondaryWaveStrength;
            
            targetPosition.y += floatOffset * idleWeight;
            
            float idleRotation = primaryWave * wizardIdleRotation;
            targetRotation += idleRotation * idleWeight;
        }

        // Moving animation
        if (moveWeight > 0.01f)
        {
            float tiltTarget = -smoothedVelocity.x * (wizardMaxTiltAngle / 5f);
            tiltTarget = Mathf.Clamp(tiltTarget, -wizardMaxTiltAngle, wizardMaxTiltAngle);
            currentTiltAngle = Mathf.Lerp(currentTiltAngle, tiltTarget, wizardTiltSpeed * deltaTime);
            targetRotation += currentTiltAngle * moveWeight;

            float moveBob = Mathf.Sin(time * wizardMoveBobFrequency) * wizardMoveBobAmount;
            targetPosition.y += moveBob * moveWeight;

            targetPosition.x += smoothedVelocity.normalized.x * wizardForwardLean * moveWeight;
        }

        // === APPLY FINAL SCALE: Base × External × Animation ===
        Vector3 finalTargetScale = wizardBaseScale * externalScaleMultiplier;
        // Note: animationScale is Vector3.one for wizard (no squash/stretch), so we just use finalTargetScale

        // Apply transforms with smoothing
        wizardVisual.localPosition = Vector3.Lerp(wizardVisual.localPosition, targetPosition, positionSmoothing * deltaTime);
        wizardVisual.localScale = Vector3.Lerp(wizardVisual.localScale, finalTargetScale, scaleSmoothing * deltaTime);
        
        Quaternion targetRot = Quaternion.Euler(0f, 0f, targetRotation);
        wizardVisual.localRotation = Quaternion.Slerp(wizardVisual.localRotation, targetRot, positionSmoothing * deltaTime);
    }

    #endregion

    #region Slime Animation

    private void AnimateSlime(float time, float deltaTime)
    {
        if (slimeVisual == null || !slimeVisual.gameObject.activeSelf) return;

        Vector3 targetPosition = slimeBasePosition;
        
        // Animation scale modifiers (relative to 1.0)
        float animScaleX = 1f;
        float animScaleY = 1f;
        float targetRotation = 0f;

        float idleWeight = 1f - currentMovementBlend;
        float moveWeight = currentMovementBlend;

        // Idle animation
        if (idleWeight > 0.01f)
        {
            float breathPhase = Mathf.Sin(time * slimeBreathFrequency * Mathf.PI * 2f);
            float wobblePhase = Mathf.Sin(time * slimeWobbleFrequency * Mathf.PI * 2f);
            
            float idleScaleY = 1f + breathPhase * slimeBreathAmplitude + wobblePhase * slimeWobbleStrength;
            float idleScaleX = 1f - breathPhase * slimeBreathAmplitude * 0.5f - wobblePhase * slimeWobbleStrength * 0.3f;

            animScaleX = Mathf.Lerp(1f, idleScaleX, idleWeight);
            animScaleY = Mathf.Lerp(1f, idleScaleY, idleWeight);

            float idleBob = Mathf.Sin(time * slimeBreathFrequency * 1.5f * Mathf.PI * 2f) * slimeIdleBobAmplitude;
            targetPosition.y += idleBob * idleWeight;

            float heightDiff = (idleScaleY - 1f) * slimeBaseScale.y * externalScaleMultiplier * 0.5f;
            targetPosition.y += heightDiff * idleWeight;
        }

        // Moving animation
        if (moveWeight > 0.01f)
        {
            float bounceFreq = slimeBounceBaseFrequency + currentSpeed * slimeBounceSpeedMultiplier;
            
            bouncePhase += bounceFreq * deltaTime * Mathf.PI * 2f;
            if (bouncePhase > Mathf.PI * 2f)
                bouncePhase -= Mathf.PI * 2f;

            float bounceWave = Mathf.Sin(bouncePhase);
            float absBounce = Mathf.Abs(bounceWave);

            float squashStretch;
            if (bounceWave < 0)
            {
                squashStretch = -absBounce * slimeSquashAmount;
            }
            else
            {
                squashStretch = absBounce * slimeStretchAmount;
            }

            float moveScaleY = 1f + squashStretch;
            float moveScaleX = 1f - squashStretch * 0.5f;

            animScaleX = Mathf.Lerp(animScaleX, moveScaleX, moveWeight);
            animScaleY = Mathf.Lerp(animScaleY, moveScaleY, moveWeight);

            float hopHeight = (1f - Mathf.Cos(bouncePhase)) * 0.5f * slimeHopHeight;
            hopHeight *= currentSpeed / 3f;
            targetPosition.y += hopHeight * moveWeight;

            float moveHeightDiff = (moveScaleY - 1f) * slimeBaseScale.y * externalScaleMultiplier * 0.5f;
            targetPosition.y += moveHeightDiff * moveWeight;

            float moveTilt = -smoothedVelocity.x * (slimeMoveTilt / 5f);
            moveTilt = Mathf.Clamp(moveTilt, -slimeMoveTilt, slimeMoveTilt);
            targetRotation += moveTilt * moveWeight;

            DetectBounceImpact();
        }

        // === APPLY FINAL SCALE: Base × External × Animation ===
        Vector3 finalTargetScale = new Vector3(
            slimeBaseScale.x * externalScaleMultiplier * animScaleX,
            slimeBaseScale.y * externalScaleMultiplier * animScaleY,
            slimeBaseScale.z * externalScaleMultiplier
        );

        // Apply transforms with smoothing
        slimeVisual.localPosition = Vector3.Lerp(slimeVisual.localPosition, targetPosition, positionSmoothing * deltaTime);
        slimeVisual.localScale = Vector3.Lerp(slimeVisual.localScale, finalTargetScale, scaleSmoothing * deltaTime);
        
        Quaternion targetRot = Quaternion.Euler(0f, 0f, targetRotation);
        slimeVisual.localRotation = Quaternion.Slerp(slimeVisual.localRotation, targetRot, positionSmoothing * deltaTime);

        lastBouncePhase = bouncePhase;
    }

    private void DetectBounceImpact()
    {
        float currentSin = Mathf.Sin(bouncePhase);
        float lastSin = Mathf.Sin(lastBouncePhase);
        
        if (lastSin > 0 && currentSin <= 0 && currentMovementBlend > 0.3f)
        {
            OnSlimeLand();
        }
    }

    protected virtual void OnSlimeLand()
    {
        // Hook for particles/sound
    }

    #endregion

    #region Hit Reaction

    public void TriggerHitReaction()
    {
        StartCoroutine(HitReactionCoroutine());
    }

    private System.Collections.IEnumerator HitReactionCoroutine()
    {
        Transform visual = currentFormIsSlime ? slimeVisual : wizardVisual;
        if (visual == null || !visual.gameObject.activeSelf) yield break;

        Vector3 baseScale = currentFormIsSlime ? slimeBaseScale : wizardBaseScale;
        
        float elapsed = 0f;
        float squashDuration = 0.1f;
        
        while (elapsed < squashDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / squashDuration;
            float squash = Mathf.Sin(t * Mathf.PI) * 0.2f;
            
            Vector3 hitScale = baseScale * externalScaleMultiplier;
            hitScale.x *= 1f + squash;
            hitScale.y *= 1f - squash;
            visual.localScale = hitScale;
            
            yield return null;
        }
    }

    #endregion

    #region Public API

    public (bool isSlime, float movementBlend, float speed) GetAnimationState()
    {
        return (currentFormIsSlime, currentMovementBlend, currentSpeed);
    }

    public bool IsMasterVisible => masterVisibility;
    public bool IsCurrentlySlime => currentFormIsSlime;

    #endregion

    #region Editor Helpers

#if UNITY_EDITOR
    private void OnValidate()
    {
        wizardFloatAmplitude = Mathf.Clamp(wizardFloatAmplitude, 0f, 0.5f);
        wizardFloatFrequency = Mathf.Clamp(wizardFloatFrequency, 0.1f, 5f);
        wizardMaxTiltAngle = Mathf.Clamp(wizardMaxTiltAngle, 0f, 45f);
        
        slimeBreathAmplitude = Mathf.Clamp(slimeBreathAmplitude, 0f, 0.3f);
        slimeSquashAmount = Mathf.Clamp(slimeSquashAmount, 0f, 0.5f);
        slimeStretchAmount = Mathf.Clamp(slimeStretchAmount, 0f, 0.5f);
        slimeHopHeight = Mathf.Clamp(slimeHopHeight, 0f, 0.5f);
    }

    private void OnDrawGizmosSelected()
    {
        if (rb != null)
        {
            Gizmos.color = currentSpeed > movementThreshold ? Color.green : Color.yellow;
            Gizmos.DrawWireSphere(transform.position, 0.5f);
            
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(transform.position, transform.position + (Vector3)smoothedVelocity * 0.3f);
        }
    }
#endif

    #endregion
}