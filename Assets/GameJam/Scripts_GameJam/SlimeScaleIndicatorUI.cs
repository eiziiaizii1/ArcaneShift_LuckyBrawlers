using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// UI component that displays the local player's scale, speed, and damage status.
/// 
/// Updated features:
/// - Shows speed modifiers for BOTH Wizard and Slime forms
/// - Shows DAMAGE multiplier for Slime form (smaller = weaker)
/// - LuckyBox speed boost is reflected in Wizard form
/// - Clean text formatting (just values, no prefixes)
/// - Adaptive scaling for Slime form
/// - Speed bar: 1.0x = 50% fill, 2.0x = 100% fill
/// - Damage bar: 1.0x = 100% fill, 0.4x = 40% fill
/// 
/// Place on a Canvas in the GameScene.
/// </summary>
public class SlimeScaleIndicatorUI : MonoBehaviour
{
    #region Inspector Fields

    [Header("UI References - Fill Bars")]
    [SerializeField] private Image scaleFillImage;
    [SerializeField] private Image speedFillImage;
    [SerializeField] private Image damageFillImage; // NEW: Damage bar
    
    [Header("UI References - Text (Value Only)")]
    [Tooltip("Shows scale value like '100%' or '45%'")]
    [SerializeField] private TextMeshProUGUI scaleValueText;
    
    [Tooltip("Shows speed value like '1.00x' or '2.00x'")]
    [SerializeField] private TextMeshProUGUI speedValueText;
    
    [Tooltip("Shows damage value like '1.00x' or '0.40x'")]
    [SerializeField] private TextMeshProUGUI damageValueText; // NEW: Damage text
    
    [Tooltip("Shows current form like 'WIZARD' or 'SLIME'")]
    [SerializeField] private TextMeshProUGUI formText;
    
    [Header("UI References - Labels (Optional)")]
    [Tooltip("Static label showing 'Size' - leave empty if not using")]
    [SerializeField] private TextMeshProUGUI scaleLabelText;
    
    [Tooltip("Static label showing 'Speed' - leave empty if not using")]
    [SerializeField] private TextMeshProUGUI speedLabelText;
    
    [Tooltip("Static label showing 'Damage' - leave empty if not using")]
    [SerializeField] private TextMeshProUGUI damageLabelText; // NEW: Damage label
    
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
    
    [Header("Visual Settings - Damage Bar")]
    [SerializeField] private Color fullDamageColor = new Color(1f, 0.5f, 0f); // Orange
    [SerializeField] private Color lowDamageColor = new Color(0.5f, 0.25f, 0f); // Dark orange
    [SerializeField] private Color wizardDamageColor = Color.yellow;
    
    [Header("Visual Settings - Form")]
    [SerializeField] private Color wizardFormColor = Color.cyan;
    [SerializeField] private Color slimeFormColor = Color.green;

    [Header("Visibility")]
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private bool alwaysVisible = true;
    [SerializeField] private GameObject damageBarContainer; // Container to hide/show damage bar

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
    private float currentDamageFill = 1f;   // Start at 100% for 1.0x
    private float targetDamageFill = 1f;
    
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
        currentDamageFill = Mathf.Lerp(currentDamageFill, targetDamageFill, fillSmoothSpeed * Time.deltaTime);
        
        if (scaleFillImage != null)
            scaleFillImage.fillAmount = currentScaleFill;
        
        if (speedFillImage != null)
            speedFillImage.fillAmount = currentSpeedFill;
        
        if (damageFillImage != null)
            damageFillImage.fillAmount = currentDamageFill;

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
        UpdateDamageDisplay(isSlime); // NEW
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
            normalizedScale = Mathf.Clamp01(sizeMultiplier);
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
                speedColor = Color.Lerp(speedColor, slowedSpeedColor, 0.5f);
            }
        }
        
        // Speed bar: 1.0x = 50%, 2.0x = 100%
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

    /// <summary>
    /// NEW: Display damage multiplier (only relevant for slime form)
    /// </summary>
    private void UpdateDamageDisplay(bool isSlime)
    {
        // Show/hide damage bar container based on form
        if (damageBarContainer != null)
        {
            damageBarContainer.SetActive(isSlime);
        }
        
        float damageMultiplier = 1f;
        Color damageColor = wizardDamageColor;
        
        if (isSlime && localSlimeController != null)
        {
            // Slime form: damage scales with size (smaller = weaker)
            damageMultiplier = localSlimeController.GetDamageMultiplier();
            
            // Color goes from orange (full) to dark orange (low)
            float normalized = (damageMultiplier - 0.4f) / 0.6f; // Assuming min 0.4, max 1.0
            damageColor = Color.Lerp(lowDamageColor, fullDamageColor, Mathf.Clamp01(normalized));
        }
        else
        {
            // Wizard form: always full damage
            damageMultiplier = 1f;
            damageColor = wizardDamageColor;
        }
        
        // Damage bar: 1.0x = 100%, 0.4x = 40%
        targetDamageFill = Mathf.Clamp01(damageMultiplier);
        
        // Update text
        if (damageValueText != null)
        {
            damageValueText.text = $"{damageMultiplier:F2}x";
            
            // Show actual damage value in parentheses for clarity
            if (isSlime && localSlimeController != null)
            {
                int actualDamage = localSlimeController.GetScaledGloopDamage();
                damageValueText.text = $"{damageMultiplier:F2}x ({actualDamage})";
            }
        }
        
        // Update color
        if (damageFillImage != null)
        {
            damageFillImage.color = damageColor;
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