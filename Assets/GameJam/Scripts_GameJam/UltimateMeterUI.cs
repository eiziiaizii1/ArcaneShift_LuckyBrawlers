using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// UI component that displays the local player's ultimate meter.
/// Shows charge progress and ready state with visual feedback.
/// 
/// Place on a Canvas in the GameScene.
/// </summary>
public class UltimateMeterUI : MonoBehaviour
{
    #region Inspector Fields

    [Header("UI References")]
    [SerializeField] private Image fillImage;
    [SerializeField] private Image backgroundImage;
    [SerializeField] private Image glowImage;
    [SerializeField] private TextMeshProUGUI percentText;
    [SerializeField] private TextMeshProUGUI statusText;
    [SerializeField] private GameObject readyIndicator;

    [Header("Visual Settings")]
    [SerializeField] private Color chargingColor = new Color(0.5f, 0.8f, 1f);
    [SerializeField] private Color readyColor = Color.yellow;
    [SerializeField] private Color slimeChargingColor = new Color(0.3f, 1f, 0.3f);
    [SerializeField] private Color slimeReadyColor = new Color(0.5f, 1f, 0.5f);
    
    [Header("Animation")]
    [SerializeField] private float pulseSpeed = 2f;
    [SerializeField] private float pulseIntensity = 0.2f;
    [SerializeField] private float fillSmoothSpeed = 5f;

    [Header("Visibility")]
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private bool hideWhenWizard = false;

    #endregion

    #region Private Fields

    private SlimeController localSlimeController;
    private float currentFillAmount = 0f;
    private float targetFillAmount = 0f;
    private bool isReady = false;
    private bool wasSlime = false;

    #endregion

    #region Unity Lifecycle

    private void Start()
    {
        // Initial state
        if (readyIndicator != null)
            readyIndicator.SetActive(false);
        
        if (canvasGroup != null)
            canvasGroup.alpha = hideWhenWizard ? 0f : 1f;
        
        // Try to find local player
        StartCoroutine(FindLocalPlayerCoroutine());
    }

    private System.Collections.IEnumerator FindLocalPlayerCoroutine()
    {
        // Wait for network to be ready
        while (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsConnectedClient)
        {
            yield return new WaitForSeconds(0.5f);
        }

        // Wait a bit more for player to spawn
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
                    // Subscribe to changes
                    localSlimeController.ultimateMeter.OnValueChanged += OnUltimateChanged;
                    localSlimeController.isSlimeForm.OnValueChanged += OnFormChanged;
                    
                    // Initial update
                    UpdateUI(localSlimeController.UltimateCharge);
                    UpdateVisibility(localSlimeController.IsSlime);
                    
                    Debug.Log("[UltimateMeterUI] Found local player's SlimeController");
                }
            }
        }
    }

    private void OnDestroy()
    {
        if (localSlimeController != null)
        {
            localSlimeController.ultimateMeter.OnValueChanged -= OnUltimateChanged;
            localSlimeController.isSlimeForm.OnValueChanged -= OnFormChanged;
        }
    }

    private void Update()
    {
        // Smooth fill animation
        currentFillAmount = Mathf.Lerp(currentFillAmount, targetFillAmount, fillSmoothSpeed * Time.deltaTime);
        
        if (fillImage != null)
            fillImage.fillAmount = currentFillAmount;
        
        // Pulse animation when ready
        if (isReady)
        {
            AnimateReady();
        }

        // Retry finding player if not found yet
        if (localSlimeController == null && Time.frameCount % 60 == 0)
        {
            FindLocalPlayer();
        }
    }

    #endregion

    #region Event Handlers

    private void OnUltimateChanged(float oldValue, float newValue)
    {
        UpdateUI(newValue);
    }

    private void OnFormChanged(bool oldValue, bool newValue)
    {
        UpdateVisibility(newValue);
        UpdateColors(newValue);
        wasSlime = newValue;
    }

    #endregion

    #region UI Updates

    private void UpdateUI(float chargePercent)
    {
        targetFillAmount = chargePercent / 100f;
        bool nowReady = chargePercent >= 100f;
        
        // Update text
        if (percentText != null)
            percentText.text = $"{Mathf.FloorToInt(chargePercent)}%";
        
        if (statusText != null)
        {
            if (nowReady)
                statusText.text = "READY! [Q]";
            else if (chargePercent > 0)
                statusText.text = "Charging...";
            else
                statusText.text = "Ultimate";
        }
        
        // Ready state changed
        if (nowReady != isReady)
        {
            isReady = nowReady;
            OnReadyStateChanged(isReady);
        }
    }

    private void UpdateVisibility(bool isSlime)
    {
        if (canvasGroup == null) return;
        
        if (hideWhenWizard)
        {
            canvasGroup.alpha = isSlime ? 1f : 0f;
        }
        else
        {
            canvasGroup.alpha = 1f;
        }
    }

    private void UpdateColors(bool isSlime)
    {
        Color charging = isSlime ? slimeChargingColor : chargingColor;
        Color ready = isSlime ? slimeReadyColor : readyColor;
        
        if (fillImage != null)
            fillImage.color = isReady ? ready : charging;
        
        if (glowImage != null)
            glowImage.color = ready;
    }

    private void OnReadyStateChanged(bool ready)
    {
        // Show/hide ready indicator
        if (readyIndicator != null)
            readyIndicator.SetActive(ready);
        
        // Update colors
        bool isSlime = localSlimeController != null && localSlimeController.IsSlime;
        Color readyCol = isSlime ? slimeReadyColor : readyColor;
        Color chargingCol = isSlime ? slimeChargingColor : chargingColor;
        
        if (fillImage != null)
            fillImage.color = ready ? readyCol : chargingCol;
        
        // Play sound or effect when becoming ready
        if (ready)
        {
            Debug.Log("[UltimateMeterUI] Ultimate is READY!");
        }
    }

    private void AnimateReady()
    {
        // Pulse the glow
        if (glowImage != null)
        {
            float pulse = Mathf.Sin(Time.time * pulseSpeed) * pulseIntensity + (1f - pulseIntensity);
            Color col = glowImage.color;
            col.a = pulse;
            glowImage.color = col;
        }
        
        // Pulse the fill slightly
        if (fillImage != null)
        {
            float pulse = Mathf.Sin(Time.time * pulseSpeed * 0.5f) * 0.05f;
            fillImage.transform.localScale = Vector3.one * (1f + pulse);
        }
    }

    #endregion

    #region Public API

    /// <summary>
    /// Force refresh the UI (useful after scene load)
    /// </summary>
    public void Refresh()
    {
        FindLocalPlayer();
    }

    /// <summary>
    /// Manually set visibility
    /// </summary>
    public void SetVisible(bool visible)
    {
        if (canvasGroup != null)
            canvasGroup.alpha = visible ? 1f : 0f;
    }

    #endregion
}