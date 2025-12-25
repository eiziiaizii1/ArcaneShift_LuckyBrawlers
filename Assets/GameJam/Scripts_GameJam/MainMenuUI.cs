using System.Collections;
using System.Collections.Generic;
using TMPro;
using Unity.Services.Lobbies.Models;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Main Menu UI controller with automatic lobby list refresh.
/// 
/// Features:
/// - Auto-refreshes lobby list every few seconds
/// - Manual refresh button still works
/// - Shows loading indicator during refresh
/// - Stops auto-refresh when joining/creating
/// </summary>
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

    [Header("Auto-Refresh Settings")]
    [Tooltip("How often to auto-refresh the lobby list (seconds)")]
    [SerializeField] private float autoRefreshInterval = 3f;
    
    [Tooltip("Enable/disable auto-refresh")]
    [SerializeField] private bool enableAutoRefresh = true;

    [Header("Loading Indicator (Optional)")]
    [SerializeField] private GameObject loadingIndicator;

    private bool isProcessing = false;
    private bool isRefreshing = false;
    private Coroutine autoRefreshCoroutine;
    private List<Lobby> cachedLobbies = new List<Lobby>();

    private void Start()
    {
        // Register button listeners
        if (createGameButton != null) createGameButton.onClick.AddListener(OnCreateGameClicked);
        if (refreshListButton != null) refreshListButton.onClick.AddListener(OnManualRefreshClicked);
        if (joinGameButton != null) joinGameButton.onClick.AddListener(OnJoinGameClicked);

        Debug.Log("[MainMenuUI] Button listeners registered.");

        // Hide loading indicator initially
        if (loadingIndicator != null)
            loadingIndicator.SetActive(false);

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

        // Start auto-refresh
        if (enableAutoRefresh)
        {
            StartAutoRefresh();
        }

        // Also do an immediate refresh
        RefreshLobbyList();

        // Unsubscribe
        if (RelayManager.Instance != null)
            RelayManager.Instance.OnRelayReady -= EnableMenuInteractions;
    }

    #region Auto-Refresh System

    /// <summary>
    /// Start the auto-refresh coroutine
    /// </summary>
    private void StartAutoRefresh()
    {
        StopAutoRefresh();
        autoRefreshCoroutine = StartCoroutine(AutoRefreshCoroutine());
        Debug.Log($"[MainMenuUI] Auto-refresh started (every {autoRefreshInterval}s)");
    }

    /// <summary>
    /// Stop the auto-refresh coroutine
    /// </summary>
    private void StopAutoRefresh()
    {
        if (autoRefreshCoroutine != null)
        {
            StopCoroutine(autoRefreshCoroutine);
            autoRefreshCoroutine = null;
            Debug.Log("[MainMenuUI] Auto-refresh stopped");
        }
    }

    /// <summary>
    /// Coroutine that periodically refreshes the lobby list
    /// </summary>
    private IEnumerator AutoRefreshCoroutine()
    {
        while (true)
        {
            yield return new WaitForSeconds(autoRefreshInterval);
            
            // Don't refresh if we're in the middle of joining/creating
            if (!isProcessing && !isRefreshing)
            {
                yield return RefreshLobbyListAsync();
            }
        }
    }

    #endregion

    #region Lobby List Refresh

    /// <summary>
    /// Manual refresh button clicked
    /// </summary>
    private void OnManualRefreshClicked()
    {
        if (!isRefreshing && !isProcessing)
        {
            RefreshLobbyList();
        }
    }

    /// <summary>
    /// Refresh the lobby list (non-async wrapper)
    /// </summary>
    private void RefreshLobbyList()
    {
        if (isRefreshing) return;
        StartCoroutine(RefreshLobbyListAsync());
    }

    /// <summary>
    /// Async refresh lobby list
    /// </summary>
    private IEnumerator RefreshLobbyListAsync()
    {
        if (LobbyManager.Instance == null || lobbyListContainer == null || lobbyItemPrefab == null)
        {
            Debug.LogWarning("[MainMenuUI] Lobby browser UI not fully configured.");
            yield break;
        }

        isRefreshing = true;
        
        // Show loading indicator
        if (loadingIndicator != null)
            loadingIndicator.SetActive(true);

        // Disable refresh button during refresh
        if (refreshListButton != null)
            refreshListButton.interactable = false;

        // Query lobbies
        var queryTask = LobbyManager.Instance.GetActiveLobbies();
        
        // Wait for task to complete
        while (!queryTask.IsCompleted)
        {
            yield return null;
        }

        List<Lobby> lobbies = queryTask.Result;
        
        // Check if the list actually changed
        bool listChanged = HasLobbyListChanged(lobbies);
        
        if (listChanged)
        {
            // Clear old entries
            foreach (Transform child in lobbyListContainer)
            {
                Destroy(child.gameObject);
            }

            // Populate new list
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

            // Cache the new list
            cachedLobbies = lobbies;
            
            Debug.Log($"[MainMenuUI] Lobby list updated. Found {lobbies.Count} lobbies.");
        }

        // Update status only if not processing something else
        if (!isProcessing)
        {
            if (lobbies.Count > 0)
            {
                statusText.text = $"Found {lobbies.Count} lobby(s)";
            }
            else
            {
                statusText.text = "No lobbies found. Create one!";
            }
        }

        // Hide loading indicator
        if (loadingIndicator != null)
            loadingIndicator.SetActive(false);

        // Re-enable refresh button
        if (refreshListButton != null && !isProcessing)
            refreshListButton.interactable = true;

        isRefreshing = false;
    }

    /// <summary>
    /// Check if the lobby list has changed compared to cached version
    /// </summary>
    private bool HasLobbyListChanged(List<Lobby> newLobbies)
    {
        if (newLobbies.Count != cachedLobbies.Count)
            return true;

        for (int i = 0; i < newLobbies.Count; i++)
        {
            // Check if lobby IDs match
            bool found = false;
            foreach (var cached in cachedLobbies)
            {
                if (cached.Id == newLobbies[i].Id)
                {
                    // Also check if player count changed
                    if (cached.Players.Count != newLobbies[i].Players.Count)
                        return true;
                    
                    found = true;
                    break;
                }
            }
            
            if (!found)
                return true;
        }

        return false;
    }

    #endregion

    #region Create Lobby

    private async void OnCreateGameClicked()
    {
        if (isProcessing)
        {
            Debug.LogWarning("[MainMenuUI] Already processing a request. Please wait.");
            return;
        }

        isProcessing = true;
        StopAutoRefresh(); // Stop auto-refresh while creating
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
            StartAutoRefresh();
            isProcessing = false;
            return;
        }

        bool success = await LobbyManager.Instance.CreateLobby(lobbyName, 4);

        if (success)
        {
            statusText.text = "Lobby Created! Loading game...";
            Debug.Log("[MainMenuUI] Lobby created successfully!");
            // Scene loading is handled by LobbyManager
        }
        else
        {
            statusText.text = "Failed to create lobby. Try again.";
            Debug.LogError("[MainMenuUI] Lobby creation failed.");
            SetInteractable(true);
            StartAutoRefresh();
            isProcessing = false;
        }
    }

    #endregion

    #region Join Lobby

    /// <summary>
    /// Called by LobbyItem when a specific lobby is clicked
    /// </summary>
    public async void JoinSpecificLobby(Lobby lobby)
    {
        if (isProcessing)
        {
            Debug.LogWarning("[MainMenuUI] Already processing a request. Please wait.");
            return;
        }

        isProcessing = true;
        StopAutoRefresh(); // Stop auto-refresh while joining
        SetInteractable(false);

        statusText.text = $"Joining '{lobby.Name}'...";
        Debug.Log($"[MainMenuUI] Attempting to join lobby: {lobby.Name}");

        if (LobbyManager.Instance == null)
        {
            statusText.text = "ERROR: LobbyManager not found!";
            Debug.LogError("[MainMenuUI] LobbyManager.Instance is null.");
            SetInteractable(true);
            StartAutoRefresh();
            isProcessing = false;
            return;
        }

        bool success = await LobbyManager.Instance.JoinLobby(lobby);

        if (success)
        {
            statusText.text = "Joined! Waiting for host...";
            Debug.Log("[MainMenuUI] Successfully joined lobby.");
            // Don't re-enable - waiting for scene sync
        }
        else
        {
            statusText.text = "Failed to join. Try again.";
            Debug.LogError("[MainMenuUI] Failed to join lobby.");
            SetInteractable(true);
            StartAutoRefresh();
            isProcessing = false;
            
            // Refresh to get updated list (lobby might be full/gone)
            RefreshLobbyList();
        }
    }

    /// <summary>
    /// Direct join by relay code
    /// </summary>
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
        StopAutoRefresh();
        SetInteractable(false);

        statusText.text = "Joining relay...";
        Debug.Log($"[MainMenuUI] Joining via code: {joinCode}");

        if (RelayManager.Instance == null)
        {
            statusText.text = "ERROR: RelayManager not found!";
            Debug.LogError("[MainMenuUI] RelayManager.Instance is null.");
            SetInteractable(true);
            StartAutoRefresh();
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
            StartAutoRefresh();
            isProcessing = false;
        }
    }

    #endregion

    #region Cleanup

    private void OnDestroy()
    {
        StopAutoRefresh();
        
        if (createGameButton != null) createGameButton.onClick.RemoveListener(OnCreateGameClicked);
        if (refreshListButton != null) refreshListButton.onClick.RemoveListener(OnManualRefreshClicked);
        if (joinGameButton != null) joinGameButton.onClick.RemoveListener(OnJoinGameClicked);

        if (RelayManager.Instance != null)
            RelayManager.Instance.OnRelayReady -= EnableMenuInteractions;
    }

    private void OnDisable()
    {
        StopAutoRefresh();
    }

    private void OnEnable()
    {
        // Resume auto-refresh if we're re-enabled and services are ready
        if (enableAutoRefresh && RelayManager.Instance != null && RelayManager.Instance.IsRelayReady && !isProcessing)
        {
            StartAutoRefresh();
        }
    }

    #endregion

    #region Public API

    /// <summary>
    /// Enable or disable auto-refresh at runtime
    /// </summary>
    public void SetAutoRefresh(bool enabled)
    {
        enableAutoRefresh = enabled;
        
        if (enabled && !isProcessing)
            StartAutoRefresh();
        else
            StopAutoRefresh();
    }

    /// <summary>
    /// Change auto-refresh interval at runtime
    /// </summary>
    public void SetAutoRefreshInterval(float seconds)
    {
        autoRefreshInterval = Mathf.Max(1f, seconds); // Minimum 1 second
        
        // Restart with new interval
        if (enableAutoRefresh && autoRefreshCoroutine != null)
        {
            StartAutoRefresh();
        }
    }

    #endregion
}