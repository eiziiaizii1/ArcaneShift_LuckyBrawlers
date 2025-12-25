using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// UI component that displays the local player's scale and speed status.
/// 
/// Updated features:
/// - Shows speed modifiers for BOTH Wizard and Slime forms
/// - LuckyBox speed boost is reflected in Wizard form
/// - Clean text formatting (just values, no prefixes)
/// - Adaptive scaling speed for Slime form
/// - Speed bar: 1.0x = 50% fill, 2.0x = 100% fill, 0.5x = 25% fill
/// 
/// Place on a Canvas in the GameScene.
/// </summary>
public class SlimeScaleIndicatorUI : MonoBehaviour
{
    #region Inspector Fields

    [Header("UI References - Fill Bars")]
    [SerializeField] private Image scaleFillImage;
    [SerializeField] private Image speedFillImage;
    
    [Header("UI References - Text (Value Only)")]
    [Tooltip("Shows scale value like '100%' or '45%'")]
    [SerializeField] private TextMeshProUGUI scaleValueText;
    
    [Tooltip("Shows speed value like '1.00x' or '2.00x'")]
    [SerializeField] private TextMeshProUGUI speedValueText;
    
    [Tooltip("Shows current form like 'WIZARD' or 'SLIME'")]
    [SerializeField] private TextMeshProUGUI formText;
    
    [Header("UI References - Labels (Optional)")]
    [Tooltip("Static label showing 'Size' - leave empty if not using")]
    [SerializeField] private TextMeshProUGUI scaleLabelText;
    
    [Tooltip("Static label showing 'Speed' - leave empty if not using")]
    [SerializeField] private TextMeshProUGUI speedLabelText;
    
    [Header("UI References - Icon")]
    [SerializeField] private Image formIcon;
    [SerializeField] private Sprite wizardIconSprite;
    [SerializeField] private Sprite slimeIconSprite;

    [Header("Visual Settings - Scale Bar")]
    [SerializeField] private Color fullScaleColor = Color.green;
    [SerializeField] private Color minScaleColor = Color.red;
    
    [Header("Visual Settings - Speed Bar")]
    [SerializeField] private Color normalSpeedColor = Color.white;
    [SerializeField] private Color boostedSpeedColor = Color.cyan;
    [SerializeField] private Color slimeSpeedColor = new Color(0.5f, 1f, 0.5f);
    [SerializeField] private Color slowedSpeedColor = Color.red;
    
    [Header("Visual Settings - Form")]
    [SerializeField] private Color wizardFormColor = Color.cyan;
    [SerializeField] private Color slimeFormColor = Color.green;

    [Header("Visibility")]
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private bool alwaysVisible = true;

    [Header("Animation")]
    [SerializeField] private float fillSmoothSpeed = 8f;

    #endregion

    #region Private Fields

    private SlimeController localSlimeController;
    private PlayerController localPlayerController;
    private float currentScaleFill = 1f;
    private float targetScaleFill = 1f;
    private float currentSpeedFill = 0.5f;  // Start at 50% for 1.0x
    private float targetSpeedFill = 0.5f;
    
    // Cache previous values to detect changes
    private ModifierType lastKnownModifier = ModifierType.None;
    private bool lastKnownSlimeState = false;

    #endregion

    #region Unity Lifecycle

    private void Start()
    {
        if (canvasGroup != null)
            canvasGroup.alpha = alwaysVisible ? 1f : 0f;
        
        StartCoroutine(FindLocalPlayerCoroutine());
    }

    private System.Collections.IEnumerator FindLocalPlayerCoroutine()
    {
        while (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsConnectedClient)
        {
            yield return new WaitForSeconds(0.5f);
        }

        yield return new WaitForSeconds(1f);
        FindLocalPlayer();
    }

    private void FindLocalPlayer()
    {
        if (NetworkManager.Singleton == null) return;
        
        ulong localClientId = NetworkManager.Singleton.LocalClientId;
        
        if (NetworkManager.Singleton.ConnectedClients.TryGetValue(localClientId, out NetworkClient client))
        {
            if (client.PlayerObject != null)
            {
                localSlimeController = client.PlayerObject.GetComponent<SlimeController>();
                localPlayerController = client.PlayerObject.GetComponent<PlayerController>();
                
                if (localSlimeController != null)
                {
                    localSlimeController.currentScale.OnValueChanged += OnScaleChanged;
                    localSlimeController.isSlimeForm.OnValueChanged += OnFormChanged;
                }
                
                // Subscribe to LuckyBox events
                if (LuckyBox.ActiveGlobalEvent != null)
                {
                    LuckyBox.ActiveGlobalEvent.OnValueChanged += OnLuckyBoxEventChanged;
                }
                
                UpdateUI();
                UpdateVisibility();
                
                Debug.Log("[SlimeScaleIndicatorUI] Found local player");
            }
        }
    }

    private void OnDestroy()
    {
        if (localSlimeController != null)
        {
            localSlimeController.currentScale.OnValueChanged -= OnScaleChanged;
            localSlimeController.isSlimeForm.OnValueChanged -= OnFormChanged;
        }
        
        if (LuckyBox.ActiveGlobalEvent != null)
        {
            LuckyBox.ActiveGlobalEvent.OnValueChanged -= OnLuckyBoxEventChanged;
        }
    }

    private void Update()
    {
        // Smooth fill animations
        currentScaleFill = Mathf.Lerp(currentScaleFill, targetScaleFill, fillSmoothSpeed * Time.deltaTime);
        currentSpeedFill = Mathf.Lerp(currentSpeedFill, targetSpeedFill, fillSmoothSpeed * Time.deltaTime);
        
        if (scaleFillImage != null)
            scaleFillImage.fillAmount = currentScaleFill;
        
        if (speedFillImage != null)
            speedFillImage.fillAmount = currentSpeedFill;

        // Retry finding player
        if (localSlimeController == null && localPlayerController == null && Time.frameCount % 60 == 0)
        {
            FindLocalPlayer();
        }
        
        // Check for LuckyBox changes (backup polling in case events missed)
        if (Time.frameCount % 10 == 0)
        {
            CheckForModifierChanges();
        }
    }

    #endregion

    #region Event Handlers

    private void OnScaleChanged(float oldValue, float newValue)
    {
        UpdateUI();
    }

    private void OnFormChanged(bool oldValue, bool newValue)
    {
        UpdateUI();
        UpdateVisibility();
    }

    private void OnLuckyBoxEventChanged(ModifierType oldValue, ModifierType newValue)
    {
        UpdateUI();
    }

    private void CheckForModifierChanges()
    {
        ModifierType currentModifier = LuckyBox.ActiveGlobalEvent.Value;
        bool isSlime = localSlimeController != null && localSlimeController.IsSlime;
        
        if (currentModifier != lastKnownModifier || isSlime != lastKnownSlimeState)
        {
            lastKnownModifier = currentModifier;
            lastKnownSlimeState = isSlime;
            UpdateUI();
        }
    }

    #endregion

    #region UI Updates

    private void UpdateUI()
    {
        bool isSlime = localSlimeController != null && localSlimeController.IsSlime;
        
        UpdateScaleDisplay(isSlime);
        UpdateSpeedDisplay(isSlime);
        UpdateFormDisplay(isSlime);
    }

    private void UpdateScaleDisplay(bool isSlime)
    {
        float scalePercent;
        float normalizedScale;
        
        if (isSlime && localSlimeController != null)
        {
            // Slime form: show adaptive scale (0.3 to 1.0)
            float scale = localSlimeController.CurrentScale;
            scalePercent = scale * 100f;
            normalizedScale = (scale - 0.3f) / 0.7f; // Normalize 0.3-1.0 to 0-1
        }
        else
        {
            // Wizard form: always 100% scale (unless SizeChange event active)
            float sizeMultiplier = LuckyBox.GetSizeMultiplier();
            scalePercent = sizeMultiplier * 100f;
            normalizedScale = Mathf.Clamp01(sizeMultiplier); // 0.5-1.5 range, normalize roughly
        }
        
        // Update fill target
        targetScaleFill = Mathf.Clamp01(normalizedScale);
        
        // Update text (VALUE ONLY - no prefix)
        if (scaleValueText != null)
        {
            scaleValueText.text = $"{scalePercent:F0}%";
        }
        
        // Update color based on scale
        if (scaleFillImage != null)
        {
            scaleFillImage.color = Color.Lerp(minScaleColor, fullScaleColor, normalizedScale);
        }
    }

    private void UpdateSpeedDisplay(bool isSlime)
    {
        float speedMultiplier = 1f;
        Color speedColor = normalSpeedColor;
        
        if (isSlime && localSlimeController != null)
        {
            // Slime form: adaptive scaling speed (smaller = faster)
            speedMultiplier = localSlimeController.GetSpeedMultiplier();
            speedColor = slimeSpeedColor;
            
            // Also add LuckyBox speed boost if active
            if (LuckyBox.ActiveGlobalEvent.Value == ModifierType.SpeedBoost)
            {
                speedMultiplier *= 2f; // SpeedBoost doubles speed
                speedColor = Color.Lerp(slimeSpeedColor, boostedSpeedColor, 0.5f);
            }
        }
        else
        {
            // Wizard form: check for LuckyBox speed modifiers
            if (LuckyBox.ActiveGlobalEvent.Value == ModifierType.SpeedBoost)
            {
                speedMultiplier = 2f; // SpeedBoost modifier
                speedColor = boostedSpeedColor;
            }
            else
            {
                speedMultiplier = 1f;
                speedColor = normalSpeedColor;
            }
        }
        
        // Also check for SlowDebuff
        if (localPlayerController != null)
        {
            SlowDebuff slowDebuff = localPlayerController.GetComponent<SlowDebuff>();
            if (slowDebuff != null && slowDebuff.IsActive)
            {
                speedMultiplier *= slowDebuff.GetMultiplier();
                speedColor = Color.Lerp(speedColor, slowedSpeedColor, 0.5f); // Tint red when slowed
            }
        }
        
        // === NEW SPEED BAR LOGIC ===
        // 1.0x = 50% fill (half bar)
        // 2.0x = 100% fill (full bar)
        // 0.5x = 25% fill (quarter bar)
        // Formula: fillAmount = speedMultiplier / 2.0
        // This means: 0x = 0%, 1x = 50%, 2x = 100%, 4x = 200% (clamped to 100%)
        float normalizedSpeed = speedMultiplier / 2f;
        normalizedSpeed = Mathf.Clamp01(normalizedSpeed);
        
        targetSpeedFill = normalizedSpeed;
        
        // Update text (VALUE ONLY - no prefix)
        if (speedValueText != null)
        {
            speedValueText.text = $"{speedMultiplier:F2}x";
        }
        
        // Update color
        if (speedFillImage != null)
        {
            speedFillImage.color = speedColor;
        }
    }

    private void UpdateFormDisplay(bool isSlime)
    {
        // Update form text (VALUE ONLY)
        if (formText != null)
        {
            formText.text = isSlime ? "SLIME" : "WIZARD";
            formText.color = isSlime ? slimeFormColor : wizardFormColor;
        }
        
        // Update icon
        if (formIcon != null)
        {
            if (isSlime && slimeIconSprite != null)
            {
                formIcon.sprite = slimeIconSprite;
                formIcon.color = slimeFormColor;
            }
            else if (!isSlime && wizardIconSprite != null)
            {
                formIcon.sprite = wizardIconSprite;
                formIcon.color = wizardFormColor;
            }
            else
            {
                // No sprite assigned, just change color
                formIcon.color = isSlime ? slimeFormColor : wizardFormColor;
            }
        }
    }

    private void UpdateVisibility()
    {
        if (canvasGroup == null) return;
        
        if (alwaysVisible)
        {
            canvasGroup.alpha = 1f;
        }
        else
        {
            // Only show when slime or when speed is modified
            bool isSlime = localSlimeController != null && localSlimeController.IsSlime;
            bool hasSpeedModifier = LuckyBox.ActiveGlobalEvent.Value == ModifierType.SpeedBoost;
            
            canvasGroup.alpha = (isSlime || hasSpeedModifier) ? 1f : 0f;
        }
    }

    #endregion

    #region Public API

    /// <summary>
    /// Force refresh the UI
    /// </summary>
    public void Refresh()
    {
        FindLocalPlayer();
        UpdateUI();
    }

    /// <summary>
    /// Set visibility mode
    /// </summary>
    public void SetAlwaysVisible(bool visible)
    {
        alwaysVisible = visible;
        UpdateVisibility();
    }

    #endregion
}