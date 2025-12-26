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
    [SerializeField] private Image damageFillImage;
    
    [Header("UI References - Text (Value Only)")]
    [Tooltip("Shows scale value like '100%' or '45%'")]
    [SerializeField] private TextMeshProUGUI scaleValueText;
    
    [Tooltip("Shows speed value like '1.00x' or '2.00x'")]
    [SerializeField] private TextMeshProUGUI speedValueText;
    
    [Tooltip("Shows damage value like '1.00x' or '0.40x'")]
    [SerializeField] private TextMeshProUGUI damageValueText;
    
    [Tooltip("Shows current form like 'WIZARD' or 'SLIME'")]
    [SerializeField] private TextMeshProUGUI formText;
    
    [Header("UI References - Labels (Optional)")]
    [Tooltip("Static label showing 'Size' - leave empty if not using")]
    [SerializeField] private TextMeshProUGUI scaleLabelText;
    
    [Tooltip("Static label showing 'Speed' - leave empty if not using")]
    [SerializeField] private TextMeshProUGUI speedLabelText;
    
    [Tooltip("Static label showing 'Damage' - leave empty if not using")]
    [SerializeField] private TextMeshProUGUI damageLabelText;
    
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
    [SerializeField] private Color fullDamageColor = new Color(1f, 0.5f, 0f);
    [SerializeField] private Color lowDamageColor = new Color(0.5f, 0.25f, 0f);
    [SerializeField] private Color wizardDamageColor = Color.yellow;
    
    [Header("Visual Settings - Form")]
    [SerializeField] private Color wizardFormColor = Color.cyan;
    [SerializeField] private Color slimeFormColor = Color.green;

    [Header("Visibility")]
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private bool alwaysVisible = true;
    [SerializeField] private GameObject damageBarContainer;

    [Header("Animation")]
    [SerializeField] private float fillSmoothSpeed = 8f;

    #endregion

    #region Private Fields

    private SlimeController localSlimeController;
    private PlayerController localPlayerController;
    private float currentScaleFill = 1f;
    private float targetScaleFill = 1f;
    private float currentSpeedFill = 0.5f;
    private float targetSpeedFill = 0.5f;
    private float currentDamageFill = 1f;
    private float targetDamageFill = 1f;
    
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
                if (LuckyBox.Instance != null)
                {
                    LuckyBox.Instance.ActiveGlobalEvent.OnValueChanged += OnLuckyBoxEventChanged;
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
        
        if (LuckyBox.Instance != null)
        {
            LuckyBox.Instance.ActiveGlobalEvent.OnValueChanged -= OnLuckyBoxEventChanged;
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
        if (LuckyBox.Instance == null) return;
        
        ModifierType currentModifier = LuckyBox.Instance.ActiveGlobalEvent.Value;
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
        UpdateDamageDisplay(isSlime);
        UpdateFormDisplay(isSlime);
    }

    private void UpdateScaleDisplay(bool isSlime)
    {
        float scalePercent;
        float normalizedScale;
        
        if (isSlime && localSlimeController != null)
        {
            float scale = localSlimeController.CurrentScale;
            scalePercent = scale * 100f;
            normalizedScale = (scale - 0.3f) / 0.7f;
        }
        else
        {
            float sizeMultiplier = LuckyBox.GetSizeMultiplier();
            scalePercent = sizeMultiplier * 100f;
            normalizedScale = Mathf.Clamp01(sizeMultiplier);
        }
        
        targetScaleFill = Mathf.Clamp01(normalizedScale);
        
        if (scaleValueText != null)
        {
            scaleValueText.text = $"{scalePercent:F0}%";
        }
        
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
            speedMultiplier = localSlimeController.GetSpeedMultiplier();
            speedColor = slimeSpeedColor;
            
            if (LuckyBox.Instance != null && LuckyBox.Instance.ActiveGlobalEvent.Value == ModifierType.SpeedBoost)
            {
                speedMultiplier *= 2f;
                speedColor = Color.Lerp(slimeSpeedColor, boostedSpeedColor, 0.5f);
            }
        }
        else
        {
            if (LuckyBox.Instance != null && LuckyBox.Instance.ActiveGlobalEvent.Value == ModifierType.SpeedBoost)
            {
                speedMultiplier = 2f;
                speedColor = boostedSpeedColor;
            }
            else
            {
                speedMultiplier = 1f;
                speedColor = normalSpeedColor;
            }
        }
        
        if (localPlayerController != null)
        {
            SlowDebuff slowDebuff = localPlayerController.GetComponent<SlowDebuff>();
            if (slowDebuff != null && slowDebuff.IsActive)
            {
                speedMultiplier *= slowDebuff.GetMultiplier();
                speedColor = Color.Lerp(speedColor, slowedSpeedColor, 0.5f);
            }
        }
        
        float normalizedSpeed = speedMultiplier / 2f;
        normalizedSpeed = Mathf.Clamp01(normalizedSpeed);
        
        targetSpeedFill = normalizedSpeed;
        
        if (speedValueText != null)
        {
            speedValueText.text = $"{speedMultiplier:F2}x";
        }
        
        if (speedFillImage != null)
        {
            speedFillImage.color = speedColor;
        }
    }

    private void UpdateDamageDisplay(bool isSlime)
    {
        if (damageBarContainer != null)
        {
            damageBarContainer.SetActive(isSlime);
        }
        
        float damageMultiplier = 1f;
        Color damageColor = wizardDamageColor;
        
        if (isSlime && localSlimeController != null)
        {
            damageMultiplier = localSlimeController.GetDamageMultiplier();
            
            float normalized = (damageMultiplier - 0.4f) / 0.6f;
            damageColor = Color.Lerp(lowDamageColor, fullDamageColor, Mathf.Clamp01(normalized));
        }
        else
        {
            damageMultiplier = 1f;
            damageColor = wizardDamageColor;
        }
        
        targetDamageFill = Mathf.Clamp01(damageMultiplier);
        
        if (damageValueText != null)
        {
            damageValueText.text = $"{damageMultiplier:F2}x";
            
            if (isSlime && localSlimeController != null)
            {
                int actualDamage = localSlimeController.GetScaledGloopDamage();
                damageValueText.text = $"{damageMultiplier:F2}x ({actualDamage})";
            }
        }
        
        if (damageFillImage != null)
        {
            damageFillImage.color = damageColor;
        }
    }

    private void UpdateFormDisplay(bool isSlime)
    {
        if (formText != null)
        {
            formText.text = isSlime ? "SLIME" : "WIZARD";
            formText.color = isSlime ? slimeFormColor : wizardFormColor;
        }
        
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
            bool isSlime = localSlimeController != null && localSlimeController.IsSlime;
            bool hasSpeedModifier = LuckyBox.Instance != null && 
                                    LuckyBox.Instance.ActiveGlobalEvent.Value == ModifierType.SpeedBoost;
            
            canvasGroup.alpha = (isSlime || hasSpeedModifier) ? 1f : 0f;
        }
    }

    #endregion

    #region Public API

    public void Refresh()
    {
        FindLocalPlayer();
        UpdateUI();
    }

    public void SetAlwaysVisible(bool visible)
    {
        alwaysVisible = visible;
        UpdateVisibility();
    }

    #endregion
}