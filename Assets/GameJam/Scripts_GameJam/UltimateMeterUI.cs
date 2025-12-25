using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// UI component that displays the local player's ultimate meter.
/// Works with LaserBeamUltimate component.
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
    [SerializeField] private Color firingColor = Color.red;
    [SerializeField] private Color slimeChargingColor = new Color(0.3f, 1f, 0.3f);
    [SerializeField] private Color slimeReadyColor = new Color(0.5f, 1f, 0.5f);
    
    [Header("Animation")]
    [SerializeField] private float pulseSpeed = 2f;
    [SerializeField] private float pulseIntensity = 0.2f;
    [SerializeField] private float fillSmoothSpeed = 5f;

    [Header("Visibility")]
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private bool alwaysVisible = true;

    #endregion

    #region Private Fields

    private LaserBeamUltimate localLaserUltimate;
    private SlimeController localSlimeController;
    private float currentFillAmount = 0f;
    private float targetFillAmount = 0f;
    private bool isReady = false;
    private bool isFiring = false;

    #endregion

    #region Unity Lifecycle

    private void Start()
    {
        // Initial state
        if (readyIndicator != null)
            readyIndicator.SetActive(false);
        
        if (canvasGroup != null)
            canvasGroup.alpha = alwaysVisible ? 1f : 0f;
        
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
                localLaserUltimate = client.PlayerObject.GetComponent<LaserBeamUltimate>();
                localSlimeController = client.PlayerObject.GetComponent<SlimeController>();
                
                if (localLaserUltimate != null)
                {
                    // Subscribe to changes
                    localLaserUltimate.ultimateMeter.OnValueChanged += OnUltimateChanged;
                    localLaserUltimate.isFiringLaser.OnValueChanged += OnFiringChanged;
                    
                    // Initial update
                    UpdateUI(localLaserUltimate.UltimateCharge);
                    
                    Debug.Log("[UltimateMeterUI] Found local player's LaserBeamUltimate");
                }
                
                if (localSlimeController != null)
                {
                    localSlimeController.isSlimeForm.OnValueChanged += OnFormChanged;
                }
                
                UpdateVisibility();
            }
        }
    }

    private void OnDestroy()
    {
        if (localLaserUltimate != null)
        {
            localLaserUltimate.ultimateMeter.OnValueChanged -= OnUltimateChanged;
            localLaserUltimate.isFiringLaser.OnValueChanged -= OnFiringChanged;
        }
        
        if (localSlimeController != null)
        {
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
        if (isReady && !isFiring)
        {
            AnimateReady();
        }
        
        // Firing animation
        if (isFiring)
        {
            AnimateFiring();
        }

        // Retry finding player if not found yet
        if (localLaserUltimate == null && Time.frameCount % 60 == 0)
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

    private void OnFiringChanged(bool oldValue, bool newValue)
    {
        isFiring = newValue;
        UpdateColors();
        
        if (newValue)
        {
            // Reset fill immediately when firing
            currentFillAmount = 0f;
            targetFillAmount = 0f;
        }
    }

    private void OnFormChanged(bool oldValue, bool newValue)
    {
        UpdateColors();
    }

    #endregion

    #region UI Updates

    private void UpdateUI(float chargePercent)
    {
        targetFillAmount = chargePercent / 100f;
        bool nowReady = chargePercent >= 100f;
        
        // Update text (VALUE ONLY)
        if (percentText != null)
            percentText.text = $"{Mathf.FloorToInt(chargePercent)}%";
        
        if (statusText != null)
        {
            if (isFiring)
                statusText.text = "FIRING!";
            else if (nowReady)
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
        
        UpdateColors();
    }

    private void UpdateVisibility()
    {
        if (canvasGroup == null) return;
        canvasGroup.alpha = alwaysVisible ? 1f : 0f;
    }

    private void UpdateColors()
    {
        bool isSlime = localSlimeController != null && localSlimeController.IsSlime;
        
        Color charging = isSlime ? slimeChargingColor : chargingColor;
        Color ready = isSlime ? slimeReadyColor : readyColor;
        
        if (fillImage != null)
        {
            if (isFiring)
                fillImage.color = firingColor;
            else if (isReady)
                fillImage.color = ready;
            else
                fillImage.color = charging;
        }
        
        if (glowImage != null)
        {
            glowImage.color = ready;
            glowImage.gameObject.SetActive(isReady && !isFiring);
        }
    }

    private void OnReadyStateChanged(bool ready)
    {
        // Show/hide ready indicator
        if (readyIndicator != null)
            readyIndicator.SetActive(ready);
        
        UpdateColors();
        
        // Play sound or effect when becoming ready
        if (ready)
        {
            Debug.Log("[UltimateMeterUI] Ultimate is READY! Press Q to fire laser!");
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

    private void AnimateFiring()
    {
        // Flash effect while firing
        if (fillImage != null)
        {
            float flash = Mathf.PingPong(Time.time * 10f, 1f);
            fillImage.color = Color.Lerp(firingColor, Color.white, flash);
        }
        
        // Scale pulse
        if (fillImage != null)
        {
            float pulse = Mathf.Sin(Time.time * 20f) * 0.1f;
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