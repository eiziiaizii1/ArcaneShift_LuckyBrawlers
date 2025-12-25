using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// UI component that displays the local player's slime scale status.
/// Shows current size and speed multiplier.
/// 
/// Place on a Canvas in the GameScene.
/// </summary>
public class SlimeScaleIndicatorUI : MonoBehaviour
{
    #region Inspector Fields

    [Header("UI References")]
    [SerializeField] private Image scaleFillImage;
    [SerializeField] private Image speedFillImage;
    [SerializeField] private TextMeshProUGUI scaleText;
    [SerializeField] private TextMeshProUGUI speedText;
    [SerializeField] private TextMeshProUGUI formText;
    [SerializeField] private Image slimeIcon;

    [Header("Visual Settings")]
    [SerializeField] private Color fullScaleColor = Color.green;
    [SerializeField] private Color minScaleColor = Color.red;
    [SerializeField] private Color speedBoostColor = Color.cyan;
    [SerializeField] private Sprite wizardIcon;
    [SerializeField] private Sprite slimeIconSprite;

    [Header("Visibility")]
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private bool showWhenWizard = true;

    [Header("Animation")]
    [SerializeField] private float fillSmoothSpeed = 8f;

    #endregion

    #region Private Fields

    private SlimeController localSlimeController;
    private float currentScaleFill = 1f;
    private float targetScaleFill = 1f;
    private float currentSpeedFill = 0f;
    private float targetSpeedFill = 0f;

    #endregion

    #region Unity Lifecycle

    private void Start()
    {
        if (canvasGroup != null)
            canvasGroup.alpha = 0f; // Start hidden until we find player
        
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
                
                if (localSlimeController != null)
                {
                    localSlimeController.currentScale.OnValueChanged += OnScaleChanged;
                    localSlimeController.isSlimeForm.OnValueChanged += OnFormChanged;
                    
                    UpdateUI();
                    UpdateVisibility();
                    
                    Debug.Log("[SlimeScaleIndicatorUI] Found local player's SlimeController");
                }
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
        if (localSlimeController == null && Time.frameCount % 60 == 0)
        {
            FindLocalPlayer();
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

    #endregion

    #region UI Updates

    private void UpdateUI()
    {
        if (localSlimeController == null) return;
        
        bool isSlime = localSlimeController.IsSlime;
        float scale = localSlimeController.CurrentScale;
        float speedMultiplier = localSlimeController.GetSpeedMultiplier();
        
        // Normalize scale to 0-1 range (where min=0.3, max=1.0)
        float normalizedScale = (scale - 0.3f) / 0.7f;
        targetScaleFill = normalizedScale;
        
        // Speed multiplier (1.0 = 0%, 2.0 = 100%)
        float normalizedSpeed = (speedMultiplier - 1f) / 1f;
        targetSpeedFill = Mathf.Clamp01(normalizedSpeed);
        
        // Update text
        if (scaleText != null)
            scaleText.text = isSlime ? $"Size: {scale:P0}" : "Size: 100%";
        
        if (speedText != null)
            speedText.text = isSlime ? $"Speed: {speedMultiplier:F1}x" : "Speed: 1.0x";
        
        if (formText != null)
            formText.text = isSlime ? "SLIME FORM" : "WIZARD FORM";
        
        // Update colors based on scale
        if (scaleFillImage != null)
            scaleFillImage.color = Color.Lerp(minScaleColor, fullScaleColor, normalizedScale);
        
        if (speedFillImage != null)
            speedFillImage.color = speedBoostColor;
        
        // Update icon
        if (slimeIcon != null)
        {
            slimeIcon.sprite = isSlime ? slimeIconSprite : wizardIcon;
            slimeIcon.color = isSlime ? Color.green : Color.cyan;
        }
    }

    private void UpdateVisibility()
    {
        if (canvasGroup == null) return;
        
        bool isSlime = localSlimeController != null && localSlimeController.IsSlime;
        
        if (showWhenWizard)
        {
            canvasGroup.alpha = 1f;
        }
        else
        {
            canvasGroup.alpha = isSlime ? 1f : 0f;
        }
    }

    #endregion
}