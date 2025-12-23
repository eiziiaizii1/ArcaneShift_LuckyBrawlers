using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class MainMenuUI : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private Button createGameButton;
    [SerializeField] private Button joinGameButton;
    [SerializeField] private TMP_InputField joinCodeInput;
    [SerializeField] private TextMeshProUGUI statusText;

    private void Start()
    {
        // Register button listeners
        createGameButton.onClick.AddListener(OnCreateGameClicked);
        joinGameButton.onClick.AddListener(OnJoinGameClicked);
        Debug.Log("[MainMenuUI] Button listeners registered.");

        // Default disabled until Unity Services/Auth ready
        createGameButton.interactable = false;
        joinGameButton.interactable = false;
        statusText.text = "Connecting to Unity Services...";

        // Handle scene reloads + timing: if already ready, enable immediately; otherwise wait for event
        if (RelayManager.Instance != null && RelayManager.Instance.IsRelayReady)
        {
            EnableMenuInteractions();
        }
        else if (RelayManager.Instance != null)
        {
            RelayManager.Instance.OnRelayReady += EnableMenuInteractions;
        }
        else
        {
            // RelayManager missing in scene (helpful log)
            statusText.text = "RelayManager not found. Check scene setup.";
            Debug.LogError("[MainMenuUI] RelayManager.Instance is null. Ensure RelayManager exists in the scene.");
        }
    }

    private void EnableMenuInteractions()
    {
        // Defensive: if object is being destroyed, ignore
        if (!this) return;

        createGameButton.interactable = true;
        joinGameButton.interactable = true;

        string playerName = PlayerPrefs.GetString(BootstrapUI.PlayerNameKey, "Wizard");
        statusText.text = $"Welcome, {playerName}!";
        Debug.Log("[MainMenuUI] Unity Services Ready. Buttons enabled.");

        // Unsubscribe after first fire (prevents duplicate calls if event fired again)
        if (RelayManager.Instance != null)
            RelayManager.Instance.OnRelayReady -= EnableMenuInteractions;
    }

    private async void OnCreateGameClicked()
    {
        Debug.Log("[MainMenuUI] Create Game button clicked.");

        // Prevent double-clicks
        createGameButton.interactable = false;
        joinGameButton.interactable = false;
        statusText.text = "Creating Relay allocation...";

        if (RelayManager.Instance == null)
        {
            statusText.text = "RelayManager not found. Check console.";
            Debug.LogError("[MainMenuUI] RelayManager.Instance is null.");
            createGameButton.interactable = true;
            joinGameButton.interactable = true;
            return;
        }

        string joinCode = await RelayManager.Instance.CreateRelay();

        if (!string.IsNullOrEmpty(joinCode))
        {
            statusText.text = $"Host Started!\nJoin Code: {joinCode}";
            GUIUtility.systemCopyBuffer = joinCode;
            Debug.Log($"[MainMenuUI] Host created successfully. Join code copied to clipboard: {joinCode}");

            // Note: RelayManager.CreateRelay() loads GameScene already
        }
        else
        {
            statusText.text = "Failed to start host. Check console.";
            createGameButton.interactable = true;
            joinGameButton.interactable = true;
            Debug.LogError("[MainMenuUI] Failed to create relay.");
        }
    }

    private async void OnJoinGameClicked()
    {
        string joinCode = joinCodeInput.text.Trim();

        if (string.IsNullOrEmpty(joinCode))
        {
            statusText.text = "Please enter a join code.";
            Debug.LogWarning("[MainMenuUI] Join attempted with empty code.");
            return;
        }

        Debug.Log($"[MainMenuUI] Join Game button clicked. Code: {joinCode}");

        // Prevent double-clicks
        joinGameButton.interactable = false;
        createGameButton.interactable = false;
        statusText.text = "Joining relay...";

        if (RelayManager.Instance == null)
        {
            statusText.text = "RelayManager not found. Check console.";
            Debug.LogError("[MainMenuUI] RelayManager.Instance is null.");
            joinGameButton.interactable = true;
            createGameButton.interactable = true;
            return;
        }

        bool success = await RelayManager.Instance.JoinRelay(joinCode);

        if (success)
        {
            statusText.text = "Joined! Waiting for host...";
            Debug.Log("[MainMenuUI] Successfully joined relay. Awaiting scene sync from host.");

            // Client will be synced to GameScene by host automatically
        }
        else
        {
            statusText.text = "Failed to join. Check code.";
            joinGameButton.interactable = true;
            createGameButton.interactable = true;
            Debug.LogError("[MainMenuUI] Failed to join relay.");
        }
    }

    private void OnDestroy()
    {
        // Clean up listeners
        if (createGameButton != null)
            createGameButton.onClick.RemoveListener(OnCreateGameClicked);

        if (joinGameButton != null)
            joinGameButton.onClick.RemoveListener(OnJoinGameClicked);

        // Clean up event subscription to prevent errors
        if (RelayManager.Instance != null)
            RelayManager.Instance.OnRelayReady -= EnableMenuInteractions;
    }
}
