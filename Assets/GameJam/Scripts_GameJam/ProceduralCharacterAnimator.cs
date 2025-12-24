using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Procedural animation system for dual-form characters (Wizard/Slime).
/// Handles velocity-based animations with distinct behaviors for each form.
/// 
/// WIZARD:
/// - Idle: Subtle magical floating effect
/// - Moving: Tilts in movement direction for weight/momentum feel
/// 
/// SLIME:
/// - Idle: Breathing effect with vertical bobbing
/// - Moving: Bouncing/hopping with squash on land, stretch on jump
/// 
/// Attach to Player prefab. References child sprite objects.
/// </summary>
public class ProceduralCharacterAnimator : NetworkBehaviour
{
    #region Inspector Fields
    
    [Header("Visual References")]
    [Tooltip("Child object containing the Wizard sprite")]
    [SerializeField] private Transform wizardVisual;
    
    [Tooltip("Child object containing the Slime sprite")]
    [SerializeField] private Transform slimeVisual;

    [Header("Velocity Source")]
    [Tooltip("Reference to Rigidbody2D for velocity data. Auto-finds if null.")]
    [SerializeField] private Rigidbody2D rb;
    
    [Tooltip("Speed threshold to consider 'moving' vs 'idle'")]
    [SerializeField] private float movementThreshold = 0.1f;

    [Header("=== WIZARD SETTINGS ===")]
    
    [Header("Wizard Idle - Magical Float")]
    [Tooltip("Vertical hover amplitude")]
    [SerializeField] private float wizardFloatAmplitude = 0.08f;
    
    [Tooltip("Float cycle speed")]
    [SerializeField] private float wizardFloatFrequency = 1.5f;
    
    [Tooltip("Subtle rotation wobble while floating")]
    [SerializeField] private float wizardIdleRotation = 3f;
    
    [Tooltip("Secondary wave for organic feel")]
    [SerializeField] private float wizardSecondaryWaveStrength = 0.3f;

    [Header("Wizard Moving - Momentum Tilt")]
    [Tooltip("Maximum tilt angle when moving")]
    [SerializeField] private float wizardMaxTiltAngle = 15f;
    
    [Tooltip("How fast the tilt responds to velocity")]
    [SerializeField] private float wizardTiltSpeed = 8f;
    
    [Tooltip("Slight forward lean amount")]
    [SerializeField] private float wizardForwardLean = 0.1f;
    
    [Tooltip("Subtle bob while moving")]
    [SerializeField] private float wizardMoveBobAmount = 0.03f;
    
    [Tooltip("Bob frequency while moving")]
    [SerializeField] private float wizardMoveBobFrequency = 8f;

    [Header("=== SLIME SETTINGS ===")]
    
    [Header("Slime Idle - Breathing")]
    [Tooltip("Breathing scale amplitude")]
    [SerializeField] private float slimeBreathAmplitude = 0.06f;
    
    [Tooltip("Breathing cycle speed")]
    [SerializeField] private float slimeBreathFrequency = 0.9f;
    
    [Tooltip("Vertical bob amplitude while idle")]
    [SerializeField] private float slimeIdleBobAmplitude = 0.04f;
    
    [Tooltip("Secondary jelly wobble strength")]
    [SerializeField] private float slimeWobbleStrength = 0.02f;
    
    [Tooltip("Wobble frequency")]
    [SerializeField] private float slimeWobbleFrequency = 4f;

    [Header("Slime Moving - Bounce Cycle")]
    [Tooltip("How much the slime squashes on 'landing'")]
    [SerializeField] private float slimeSquashAmount = 0.25f;
    
    [Tooltip("How much the slime stretches on 'jump'")]
    [SerializeField] private float slimeStretchAmount = 0.2f;
    
    [Tooltip("Base bounce cycles per second")]
    [SerializeField] private float slimeBounceBaseFrequency = 3f;
    
    [Tooltip("How much speed increases bounce frequency")]
    [SerializeField] private float slimeBounceSpeedMultiplier = 0.5f;
    
    [Tooltip("Vertical hop height while moving")]
    [SerializeField] private float slimeHopHeight = 0.15f;
    
    [Tooltip("Slight tilt in movement direction")]
    [SerializeField] private float slimeMoveTilt = 8f;

    [Header("Transition & Smoothing")]
    [Tooltip("How fast to blend between idle/moving states")]
    [SerializeField] private float stateBlendSpeed = 5f;
    
    [Tooltip("Scale smoothing speed")]
    [SerializeField] private float scaleSmoothing = 12f;
    
    [Tooltip("Position smoothing speed")]
    [SerializeField] private float positionSmoothing = 15f;

    [Header("Form State (Network Synced)")]
    public NetworkVariable<bool> isSlimeForm = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    #endregion

    #region Private State

    // Base transforms (stored on awake)
    private Vector3 wizardBasePosition;
    private Vector3 wizardBaseScale;
    private Vector3 slimeBasePosition;
    private Vector3 slimeBaseScale;

    // Animation state
    private float timeOffset;
    private float currentMovementBlend = 0f; // 0 = idle, 1 = moving
    private float currentTiltAngle = 0f;
    private Vector3 currentScaleOffset = Vector3.one;
    private Vector3 currentPositionOffset = Vector3.zero;
    
    // Slime bounce state
    private float bouncePhase = 0f;
    private float lastBouncePhase = 0f;

    // Velocity tracking
    private Vector2 currentVelocity;
    private Vector2 smoothedVelocity;
    private float currentSpeed;

    #endregion

    #region Unity Lifecycle

    private void Awake()
    {
        // Auto-find Rigidbody2D if not assigned
        if (rb == null)
        {
            rb = GetComponent<Rigidbody2D>();
        }

        // Store base transforms
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

        // Random offset so players don't animate in sync
        timeOffset = Random.Range(0f, 100f);
    }

    public override void OnNetworkSpawn()
    {
        isSlimeForm.OnValueChanged += OnFormChanged;
        UpdateVisualActiveState(isSlimeForm.Value);
        
        Debug.Log($"[ProceduralAnimator] Spawned as {(isSlimeForm.Value ? "Slime" : "Wizard")}");
    }

    public override void OnNetworkDespawn()
    {
        isSlimeForm.OnValueChanged -= OnFormChanged;
    }

    private void OnFormChanged(bool oldValue, bool newValue)
    {
        Debug.Log($"[ProceduralAnimator] Form changed to {(newValue ? "Slime" : "Wizard")}");
        UpdateVisualActiveState(newValue);
        
        // Reset animation state on form change
        bouncePhase = 0f;
        currentTiltAngle = 0f;
        currentMovementBlend = 0f;
    }

    private void UpdateVisualActiveState(bool isSlime)
    {
        if (wizardVisual != null)
            wizardVisual.gameObject.SetActive(!isSlime);
        
        if (slimeVisual != null)
            slimeVisual.gameObject.SetActive(isSlime);
    }

    private void Update()
    {
        float time = Time.time + timeOffset;
        float deltaTime = Time.deltaTime;

        // Update velocity data
        UpdateVelocityData();

        // Calculate movement blend (0 = idle, 1 = full speed)
        float targetBlend = Mathf.Clamp01(currentSpeed / 5f); // Normalize to ~5 units/sec max
        if (currentSpeed < movementThreshold)
            targetBlend = 0f;
        
        currentMovementBlend = Mathf.Lerp(currentMovementBlend, targetBlend, stateBlendSpeed * deltaTime);

        // Apply form-specific animations
        if (isSlimeForm.Value)
        {
            AnimateSlime(time, deltaTime);
        }
        else
        {
            AnimateWizard(time, deltaTime);
        }
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
            // Fallback: estimate from position change
            currentVelocity = Vector2.zero;
        }

        // Smooth velocity for less jittery animations
        smoothedVelocity = Vector2.Lerp(smoothedVelocity, currentVelocity, Time.deltaTime * 10f);
        currentSpeed = smoothedVelocity.magnitude;
    }

    #endregion

    #region Wizard Animations

    private void AnimateWizard(float time, float deltaTime)
    {
        if (wizardVisual == null) return;

        Vector3 targetPosition = wizardBasePosition;
        Vector3 targetScale = wizardBaseScale;
        float targetRotation = 0f;

        // Blend between idle and moving animations
        float idleWeight = 1f - currentMovementBlend;
        float moveWeight = currentMovementBlend;

        // === IDLE: Magical Floating ===
        if (idleWeight > 0.01f)
        {
            // Primary float wave
            float primaryWave = Mathf.Sin(time * wizardFloatFrequency * Mathf.PI * 2f);
            
            // Secondary wave for organic feel
            float secondaryWave = Mathf.Sin(time * wizardFloatFrequency * 1.7f * Mathf.PI * 2f);
            
            float floatOffset = primaryWave * wizardFloatAmplitude;
            floatOffset += secondaryWave * wizardFloatAmplitude * wizardSecondaryWaveStrength;
            
            targetPosition.y += floatOffset * idleWeight;

            // Subtle rotation wobble
            float rotWobble = Mathf.Sin(time * wizardFloatFrequency * 0.8f * Mathf.PI * 2f) * wizardIdleRotation;
            targetRotation += rotWobble * idleWeight;
        }

        // === MOVING: Momentum Tilt ===
        if (moveWeight > 0.01f)
        {
            // Calculate tilt based on horizontal velocity
            float targetTilt = -smoothedVelocity.x * (wizardMaxTiltAngle / 5f); // Normalize to 5 units/sec
            targetTilt = Mathf.Clamp(targetTilt, -wizardMaxTiltAngle, wizardMaxTiltAngle);
            
            currentTiltAngle = Mathf.Lerp(currentTiltAngle, targetTilt, wizardTiltSpeed * deltaTime);
            targetRotation += currentTiltAngle * moveWeight;

            // Forward lean in movement direction (slight Y offset based on vertical velocity)
            float forwardLean = smoothedVelocity.y * wizardForwardLean * 0.1f;
            targetPosition.y += forwardLean * moveWeight;

            // Subtle movement bob (faster than idle float)
            float moveBob = Mathf.Sin(time * wizardMoveBobFrequency * Mathf.PI * 2f) * wizardMoveBobAmount;
            moveBob *= currentSpeed / 5f; // Scale with speed
            targetPosition.y += moveBob * moveWeight;

            // Slight horizontal sway opposite to movement for weight feel
            float sway = Mathf.Sin(time * wizardMoveBobFrequency * 0.5f * Mathf.PI * 2f) * 0.02f;
            targetPosition.x += sway * moveWeight * (currentSpeed / 5f);
        }

        // Apply transforms with smoothing
        wizardVisual.localPosition = Vector3.Lerp(wizardVisual.localPosition, targetPosition, positionSmoothing * deltaTime);
        wizardVisual.localScale = Vector3.Lerp(wizardVisual.localScale, targetScale, scaleSmoothing * deltaTime);
        
        Quaternion targetRot = Quaternion.Euler(0f, 0f, targetRotation);
        wizardVisual.localRotation = Quaternion.Slerp(wizardVisual.localRotation, targetRot, positionSmoothing * deltaTime);
    }

    #endregion

    #region Slime Animations

    private void AnimateSlime(float time, float deltaTime)
    {
        if (slimeVisual == null) return;

        Vector3 targetPosition = slimeBasePosition;
        Vector3 targetScale = slimeBaseScale;
        float targetRotation = 0f;

        float idleWeight = 1f - currentMovementBlend;
        float moveWeight = currentMovementBlend;

        // === IDLE: Breathing + Subtle Bob ===
        if (idleWeight > 0.01f)
        {
            // Breathing: squash/stretch cycle
            float breathPhase = Mathf.Sin(time * slimeBreathFrequency * Mathf.PI * 2f);
            
            // Secondary jelly wobble
            float wobblePhase = Mathf.Sin(time * slimeWobbleFrequency * Mathf.PI * 2f);
            
            float scaleY = 1f + breathPhase * slimeBreathAmplitude + wobblePhase * slimeWobbleStrength;
            float scaleX = 1f - breathPhase * slimeBreathAmplitude * 0.5f - wobblePhase * slimeWobbleStrength * 0.3f;

            // Apply idle scale
            targetScale.x = slimeBaseScale.x * Mathf.Lerp(1f, scaleX, idleWeight);
            targetScale.y = slimeBaseScale.y * Mathf.Lerp(1f, scaleY, idleWeight);

            // Subtle vertical bob
            float idleBob = Mathf.Sin(time * slimeBreathFrequency * 1.5f * Mathf.PI * 2f) * slimeIdleBobAmplitude;
            targetPosition.y += idleBob * idleWeight;

            // Anchor adjustment (keep bottom grounded)
            float heightDiff = (scaleY - 1f) * slimeBaseScale.y * 0.5f;
            targetPosition.y += heightDiff * idleWeight;
        }

        // === MOVING: Bouncing/Hopping ===
        if (moveWeight > 0.01f)
        {
            // Calculate bounce frequency based on speed
            float bounceFreq = slimeBounceBaseFrequency + currentSpeed * slimeBounceSpeedMultiplier;
            
            // Advance bounce phase
            bouncePhase += bounceFreq * deltaTime * Mathf.PI * 2f;
            if (bouncePhase > Mathf.PI * 2f)
                bouncePhase -= Mathf.PI * 2f;

            // Bounce wave: 0 = top of hop, PI = bottom (squash)
            float bounceWave = Mathf.Sin(bouncePhase);
            float absBounce = Mathf.Abs(bounceWave);

            // Squash at bottom (bounceWave near -1), stretch at top (bounceWave near 1)
            float squashStretch;
            if (bounceWave < 0)
            {
                // Landing/squash phase
                squashStretch = -absBounce * slimeSquashAmount;
            }
            else
            {
                // Jump/stretch phase
                squashStretch = absBounce * slimeStretchAmount;
            }

            // Apply bounce scale (volume preservation)
            float moveScaleY = 1f + squashStretch;
            float moveScaleX = 1f - squashStretch * 0.5f; // Inverse for volume preservation

            targetScale.x = slimeBaseScale.x * Mathf.Lerp(targetScale.x / slimeBaseScale.x, moveScaleX, moveWeight);
            targetScale.y = slimeBaseScale.y * Mathf.Lerp(targetScale.y / slimeBaseScale.y, moveScaleY, moveWeight);

            // Vertical hop (use absolute sine for always-positive hop)
            float hopHeight = (1f - Mathf.Cos(bouncePhase)) * 0.5f * slimeHopHeight;
            hopHeight *= currentSpeed / 3f; // Scale with speed
            targetPosition.y += hopHeight * moveWeight;

            // Anchor adjustment for squash
            float moveHeightDiff = (moveScaleY - 1f) * slimeBaseScale.y * 0.5f;
            targetPosition.y += moveHeightDiff * moveWeight;

            // Slight tilt in movement direction
            float moveTilt = -smoothedVelocity.x * (slimeMoveTilt / 5f);
            moveTilt = Mathf.Clamp(moveTilt, -slimeMoveTilt, slimeMoveTilt);
            targetRotation += moveTilt * moveWeight;

            // Detect "landing" for impact effects
            DetectBounceImpact();
        }

        // Apply transforms with smoothing
        slimeVisual.localPosition = Vector3.Lerp(slimeVisual.localPosition, targetPosition, positionSmoothing * deltaTime);
        slimeVisual.localScale = Vector3.Lerp(slimeVisual.localScale, targetScale, scaleSmoothing * deltaTime);
        
        Quaternion targetRot = Quaternion.Euler(0f, 0f, targetRotation);
        slimeVisual.localRotation = Quaternion.Slerp(slimeVisual.localRotation, targetRot, positionSmoothing * deltaTime);

        lastBouncePhase = bouncePhase;
    }

    private void DetectBounceImpact()
    {
        // Detect when slime "lands" (crosses from top to bottom of bounce cycle)
        // This can be used to trigger particles or sound effects
        float currentSin = Mathf.Sin(bouncePhase);
        float lastSin = Mathf.Sin(lastBouncePhase);
        
        // Crossed from positive to negative = landing
        if (lastSin > 0 && currentSin <= 0 && currentMovementBlend > 0.3f)
        {
            OnSlimeLand();
        }
    }

    /// <summary>
    /// Called when slime "lands" during hop cycle.
    /// Override or extend for particles/sound.
    /// </summary>
    protected virtual void OnSlimeLand()
    {
        // Hook for particle effects, sound, etc.
        // Example: Instantiate dust puff, play squish sound
    }

    #endregion

    #region Public API

    /// <summary>
    /// Server-only: Set the character's form.
    /// </summary>
    public void SetSlimeForm(bool slime)
    {
        if (!IsServer)
        {
            Debug.LogWarning("[ProceduralAnimator] SetSlimeForm must be called on server!");
            return;
        }
        isSlimeForm.Value = slime;
    }

    /// <summary>
    /// Server-only: Toggle between forms.
    /// </summary>
    public void ToggleForm()
    {
        if (!IsServer)
        {
            Debug.LogWarning("[ProceduralAnimator] ToggleForm must be called on server!");
            return;
        }
        isSlimeForm.Value = !isSlimeForm.Value;
    }

    /// <summary>
    /// Trigger a hit reaction (extra wobble/squash).
    /// Works for both forms.
    /// </summary>
    public void TriggerHitReaction()
    {
        StartCoroutine(HitReactionCoroutine());
    }

    private System.Collections.IEnumerator HitReactionCoroutine()
    {
        Transform visual = isSlimeForm.Value ? slimeVisual : wizardVisual;
        if (visual == null) yield break;

        Vector3 baseScale = isSlimeForm.Value ? slimeBaseScale : wizardBaseScale;
        
        // Quick squash
        float elapsed = 0f;
        float squashDuration = 0.1f;
        
        while (elapsed < squashDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / squashDuration;
            float squash = Mathf.Sin(t * Mathf.PI) * 0.2f;
            
            Vector3 hitScale = baseScale;
            hitScale.x *= 1f + squash;
            hitScale.y *= 1f - squash;
            visual.localScale = hitScale;
            
            yield return null;
        }

        // Return to normal (let Update handle it)
    }

    /// <summary>
    /// Get current animation state info (useful for debugging or UI).
    /// </summary>
    public (bool isSlime, float movementBlend, float speed) GetAnimationState()
    {
        return (isSlimeForm.Value, currentMovementBlend, currentSpeed);
    }

    #endregion

    #region Editor Helpers

#if UNITY_EDITOR
    private void OnValidate()
    {
        // Clamp values to sensible ranges
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
        // Visualize movement threshold
        if (rb != null)
        {
            Gizmos.color = currentSpeed > movementThreshold ? Color.green : Color.yellow;
            Gizmos.DrawWireSphere(transform.position, 0.5f);
            
            // Draw velocity direction
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(transform.position, transform.position + (Vector3)smoothedVelocity * 0.3f);
        }
    }
#endif

    #endregion
}