using System.Collections.Generic;
using TMPro;
using Unity.Services.Lobbies.Models;
using UnityEngine;
using UnityEngine.UI;

public class MainMenuUI : MonoBehaviour
{
    [Header("Main Buttons")]
    [SerializeField] private Button createGameButton;
    [SerializeField] private Button refreshListButton;
    [SerializeField] private Button joinGameButton;

    [Header("Input Fields")]
    [SerializeField] private TMP_InputField joinCodeInput;
    [SerializeField] private TMP_InputField lobbyNameInput;

    [Header("Lobby Browser")]
    [SerializeField] private Transform lobbyListContainer;
    [SerializeField] private GameObject lobbyItemPrefab;

    [Header("Status")]
    [SerializeField] private TextMeshProUGUI statusText;

    private bool isProcessing = false;

    private void Start()
    {
        // Register button listeners
        if (createGameButton != null) createGameButton.onClick.AddListener(OnCreateGameClicked);
        if (refreshListButton != null) refreshListButton.onClick.AddListener(RefreshLobbyList);
        if (joinGameButton != null) joinGameButton.onClick.AddListener(OnJoinGameClicked);

        Debug.Log("[MainMenuUI] Button listeners registered.");

        // Initial state
        SetInteractable(false);
        statusText.text = "Connecting to Unity Services...";

        // Wait for services to be ready
        if (RelayManager.Instance != null)
        {
            if (RelayManager.Instance.IsRelayReady)
            {
                EnableMenuInteractions();
            }
            else
            {
                RelayManager.Instance.OnRelayReady += EnableMenuInteractions;
            }
        }
        else
        {
            statusText.text = "ERROR: RelayManager not found!";
            Debug.LogError("[MainMenuUI] RelayManager.Instance is null. Check scene setup.");
        }
    }

    private void SetInteractable(bool value)
    {
        if (createGameButton != null) createGameButton.interactable = value;
        if (refreshListButton != null) refreshListButton.interactable = value;
        if (joinGameButton != null) joinGameButton.interactable = value;
    }

    private void EnableMenuInteractions()
    {
        if (!this) return;

        SetInteractable(true);

        string playerName = PlayerPrefs.GetString(BootstrapUI.PlayerNameKey, "Wizard");
        statusText.text = $"Welcome, {playerName}!";
        Debug.Log("[MainMenuUI] Unity Services Ready. Menu enabled.");

        // Auto-refresh lobby list
        if (refreshListButton != null && lobbyListContainer != null && LobbyManager.Instance != null)
        {
            RefreshLobbyList();
        }

        // Unsubscribe
        if (RelayManager.Instance != null)
            RelayManager.Instance.OnRelayReady -= EnableMenuInteractions;
    }

    // --- CREATE LOBBY ---
    private async void OnCreateGameClicked()
    {
        if (isProcessing)
        {
            Debug.LogWarning("[MainMenuUI] Already processing a request. Please wait.");
            return;
        }

        isProcessing = true;
        SetInteractable(false);

        string lobbyName = lobbyNameInput != null && !string.IsNullOrWhiteSpace(lobbyNameInput.text) 
            ? lobbyNameInput.text.Trim() 
            : "Lucky Arena";

        statusText.text = "Creating Lobby...";
        Debug.Log($"[MainMenuUI] Creating lobby: {lobbyName}");

        if (LobbyManager.Instance == null)
        {
            statusText.text = "ERROR: LobbyManager not found!";
            Debug.LogError("[MainMenuUI] LobbyManager.Instance is null.");
            SetInteractable(true);
            isProcessing = false;
            return;
        }

        bool success = await LobbyManager.Instance.CreateLobby(lobbyName, 4);

        if (success)
        {
            statusText.text = "Lobby Created! Loading game...";
            Debug.Log("[MainMenuUI] Lobby created successfully!");
            // Note: Scene loading is handled by LobbyManager, so we don't re-enable buttons
        }
        else
        {
            statusText.text = "Failed to create lobby. Try again.";
            Debug.LogError("[MainMenuUI] Lobby creation failed.");
            SetInteractable(true);
            isProcessing = false;
        }
    }

    // --- REFRESH LOBBY LIST ---
    private async void RefreshLobbyList()
    {
        if (LobbyManager.Instance == null || lobbyListContainer == null || lobbyItemPrefab == null)
        {
            Debug.LogWarning("[MainMenuUI] Lobby browser UI not fully configured.");
            return;
        }

        statusText.text = "Refreshing lobbies...";
        Debug.Log("[MainMenuUI] Refreshing lobby list...");

        // Clear old entries
        foreach (Transform child in lobbyListContainer)
        {
            Destroy(child.gameObject);
        }

        // Query lobbies
        List<Lobby> lobbies = await LobbyManager.Instance.GetActiveLobbies();

        // Populate list
        foreach (Lobby lobby in lobbies)
        {
            GameObject itemObj = Instantiate(lobbyItemPrefab, lobbyListContainer);
            LobbyItem itemScript = itemObj.GetComponent<LobbyItem>();
            
            if (itemScript != null)
            {
                itemScript.Initialize(lobby, this);
            }
            else
            {
                Debug.LogError("[MainMenuUI] LobbyItem prefab is missing LobbyItem component!");
            }
        }

        statusText.text = $"Found {lobbies.Count} lobby(s).";
        Debug.Log($"[MainMenuUI] Lobby refresh complete. Found {lobbies.Count} lobbies.");
    }

    // --- JOIN SPECIFIC LOBBY (Called by LobbyItem) ---
    public async void JoinSpecificLobby(Lobby lobby)
    {
        if (isProcessing)
        {
            Debug.LogWarning("[MainMenuUI] Already processing a request. Please wait.");
            return;
        }

        isProcessing = true;
        SetInteractable(false);

        statusText.text = $"Joining '{lobby.Name}'...";
        Debug.Log($"[MainMenuUI] Attempting to join lobby: {lobby.Name}");

        if (LobbyManager.Instance == null)
        {
            statusText.text = "ERROR: LobbyManager not found!";
            Debug.LogError("[MainMenuUI] LobbyManager.Instance is null.");
            SetInteractable(true);
            isProcessing = false;
            return;
        }

        bool success = await LobbyManager.Instance.JoinLobby(lobby);

        if (success)
        {
            statusText.text = "Joined! Waiting for host...";
            Debug.Log("[MainMenuUI] Successfully joined lobby.");
            // Don't re-enable buttons - we're waiting for scene sync
        }
        else
        {
            statusText.text = "Failed to join. Try again.";
            Debug.LogError("[MainMenuUI] Failed to join lobby.");
            SetInteractable(true);
            isProcessing = false;
        }
    }

    // --- DIRECT JOIN BY CODE (Optional fallback) ---
    private async void OnJoinGameClicked()
    {
        if (joinCodeInput == null)
        {
            Debug.LogWarning("[MainMenuUI] Join code input not assigned.");
            return;
        }

        if (isProcessing)
        {
            Debug.LogWarning("[MainMenuUI] Already processing a request. Please wait.");
            return;
        }

        string joinCode = joinCodeInput.text.Trim();

        if (string.IsNullOrEmpty(joinCode))
        {
            statusText.text = "Please enter a join code.";
            return;
        }

        isProcessing = true;
        SetInteractable(false);

        statusText.text = "Joining relay...";
        Debug.Log($"[MainMenuUI] Joining via code: {joinCode}");

        if (RelayManager.Instance == null)
        {
            statusText.text = "ERROR: RelayManager not found!";
            Debug.LogError("[MainMenuUI] RelayManager.Instance is null.");
            SetInteractable(true);
            isProcessing = false;
            return;
        }

        bool success = await RelayManager.Instance.JoinRelay(joinCode);

        if (success)
        {
            statusText.text = "Joined! Waiting for host...";
            Debug.Log("[MainMenuUI] Successfully joined via relay code.");
        }
        else
        {
            statusText.text = "Failed to join. Check code.";
            Debug.LogError("[MainMenuUI] Failed to join via relay code.");
            SetInteractable(true);
            isProcessing = false;
        }
    }

    private void OnDestroy()
    {
        // Clean up listeners
        if (createGameButton != null) createGameButton.onClick.RemoveListener(OnCreateGameClicked);
        if (refreshListButton != null) refreshListButton.onClick.RemoveListener(RefreshLobbyList);
        if (joinGameButton != null) joinGameButton.onClick.RemoveListener(OnJoinGameClicked);

        if (RelayManager.Instance != null)
            RelayManager.Instance.OnRelayReady -= EnableMenuInteractions;
    }
}