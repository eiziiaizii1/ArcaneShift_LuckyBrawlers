using Unity.Netcode;
using UnityEngine;
using TMPro;
using UnityEngine.SceneManagement;

public class MatchManager : NetworkBehaviour
{
    public NetworkVariable<float> RemainingTime = new NetworkVariable<float>(300f);
    public NetworkVariable<bool> IsMatchEnded = new NetworkVariable<bool>(false);

    [Header("UI References")]
    [SerializeField] private TextMeshProUGUI timerText;
    [SerializeField] private GameObject endGamePanel;
    [SerializeField] private GameObject mainMenuButton;

    public override void OnNetworkSpawn()
    {
        // IMPORTANT: Reset timeScale when match starts!
        Time.timeScale = 1f;
        
        if (IsServer)
        {
            RemainingTime.Value = 300f; // Reset timer
            IsMatchEnded.Value = false;
            StartCoroutine(MatchTimerRoutine());
        }
        
        IsMatchEnded.OnValueChanged += OnMatchEndedChanged;
        
        // Hide end game UI initially
        if (endGamePanel != null) endGamePanel.SetActive(false);
        if (mainMenuButton != null) mainMenuButton.SetActive(false);
    }

    public override void OnNetworkDespawn()
    {
        IsMatchEnded.OnValueChanged -= OnMatchEndedChanged;
        
        // Always restore timeScale when leaving!
        Time.timeScale = 1f;
    }

    private void OnMatchEndedChanged(bool oldVal, bool newVal)
    {
        if (newVal)
        {
            StopGameAndShowUI();
        }
    }

    private System.Collections.IEnumerator MatchTimerRoutine()
    {
        while (RemainingTime.Value > 0)
        {
            yield return new WaitForSeconds(1f);
            RemainingTime.Value -= 1f;
        }
        
        if (IsServer)
        {
            IsMatchEnded.Value = true;
        }
    }

    private void StopGameAndShowUI()
    {
        // DON'T USE Time.timeScale = 0f - it breaks networking!
        // Instead, disable player controls
        
        // Show UI
        if (endGamePanel != null) endGamePanel.SetActive(true);
        if (mainMenuButton != null) mainMenuButton.SetActive(true);

        // Disable local player controls (not all players - just local)
        DisableLocalPlayerControls();
        
        // Stop LuckyBox events
        if (LuckyBox.Instance != null)
        {
            LuckyBox.Instance.StopAllCoroutines();
        }
    }

    private void DisableLocalPlayerControls()
    {
        if (NetworkManager.Singleton == null) return;
        if (NetworkManager.Singleton.LocalClient == null) return;
        
        var localPlayer = NetworkManager.Singleton.LocalClient.PlayerObject;
        if (localPlayer != null)
        {
            // Disable movement/shooting
            var pc = localPlayer.GetComponent<PlayerController>();
            if (pc != null) pc.enabled = false;
            
            // Stop any velocity
            var rb = localPlayer.GetComponent<Rigidbody2D>();
            if (rb != null) rb.linearVelocity = Vector2.zero;
        }
    }

    private void Update()
    {
        if (timerText != null && RemainingTime.Value >= 0)
        {
            int minutes = Mathf.FloorToInt(RemainingTime.Value / 60);
            int seconds = Mathf.FloorToInt(RemainingTime.Value % 60);
            timerText.text = $"{minutes:00}:{seconds:00}";
            
            // Optional: Red color in last 30 seconds
            if (RemainingTime.Value <= 30f)
            {
                timerText.color = Color.red;
            }
        }
    }

    // Button OnClick handler
    public void ExitToMenu()
    {
        // CRITICAL: Always restore timeScale!
        Time.timeScale = 1f;
        
        // Shutdown network
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.Shutdown();
        }
        
        // Load menu
        SceneManager.LoadScene("MainMenu");
    }
    
    // Also add this as a safety net
    private void OnDestroy()
    {
        Time.timeScale = 1f;
    }
    
    private void OnApplicationQuit()
    {
        Time.timeScale = 1f;
    }
}