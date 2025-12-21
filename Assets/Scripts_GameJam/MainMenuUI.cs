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
        
        // Display welcome message with player name
        string playerName = PlayerPrefs.GetString(BootstrapUI.PlayerNameKey, "Wizard");
        statusText.text = $"Welcome, {playerName}!";
    }

    private async void OnCreateGameClicked()
    {
        Debug.Log("[MainMenuUI] Create Game button clicked.");
        
        // Disable button to prevent double-clicks
        createGameButton.interactable = false;
        statusText.text = "Creating Relay allocation...";

        // Create the relay and get the join code
        string joinCode = await RelayManager.Instance.CreateRelay();

        if (!string.IsNullOrEmpty(joinCode))
        {
            // Success! Display the code and copy to clipboard
            statusText.text = $"Host Started!\nJoin Code: {joinCode}";
            GUIUtility.systemCopyBuffer = joinCode;
            
            Debug.Log($"[MainMenuUI] Host created successfully. Join code copied to clipboard: {joinCode}");
            
            // Note: RelayManager.CreateRelay() already loads GameScene
            // So we don't need to do anything else here
        }
        else
        {
            // Failed to create relay
            statusText.text = "Failed to start host. Check console.";
            createGameButton.interactable = true; // Re-enable button for retry
            Debug.LogError("[MainMenuUI] Failed to create relay.");
        }
    }

    private async void OnJoinGameClicked()
    {
        string joinCode = joinCodeInput.text.Trim();
        
        // Validate join code
        if (string.IsNullOrEmpty(joinCode))
        {
            statusText.text = "Please enter a join code.";
            Debug.LogWarning("[MainMenuUI] Join attempted with empty code.");
            return;
        }

        Debug.Log($"[MainMenuUI] Join Game button clicked. Code: {joinCode}");
        
        // Disable button to prevent double-clicks
        joinGameButton.interactable = false;
        statusText.text = "Joining relay...";

        // Attempt to join the relay
        bool success = await RelayManager.Instance.JoinRelay(joinCode);

        if (success)
        {
            statusText.text = "Joined! Waiting for host...";
            Debug.Log("[MainMenuUI] Successfully joined relay. Awaiting scene sync from host.");
            
            // Note: Client will automatically be synced to GameScene by the host
            // No need to manually load scenes here
        }
        else
        {
            // Failed to join
            statusText.text = "Failed to join. Check code.";
            joinGameButton.interactable = true; // Re-enable button for retry
            Debug.LogError("[MainMenuUI] Failed to join relay.");
        }
    }

    private void OnDestroy()
    {
        // Clean up listeners
        createGameButton.onClick.RemoveListener(OnCreateGameClicked);
        joinGameButton.onClick.RemoveListener(OnJoinGameClicked);
    }
}