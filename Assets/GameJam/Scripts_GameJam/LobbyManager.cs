using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;
using Unity.Services.Authentication;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

public class LobbyManager : MonoBehaviour
{
    public static LobbyManager Instance { get; private set; }

    private const string KEY_JOIN_CODE = "RelayJoinCode";
    private Lobby currentLobby; // Changed from hostLobby to track both host and client lobbies
    private float heartbeatTimer;
    private const float HEARTBEAT_INTERVAL = 15f;
    private bool isHost = false;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void Update()
    {
        HandleLobbyHeartbeat();
    }

    // --- QUEST 3: Host Beacon Protocol ---
    public async Task<bool> CreateLobby(string lobbyName, int maxPlayers)
    {
        try
        {
            Debug.Log($"[LobbyManager] Creating lobby '{lobbyName}' with {maxPlayers} max players...");

            // 1. Create the Relay allocation
            string relayJoinCode = await RelayManager.Instance.CreateRelay(maxPlayers - 1);
            if (string.IsNullOrEmpty(relayJoinCode))
            {
                Debug.LogError("[LobbyManager] Failed to create relay allocation.");
                return false;
            }

            Debug.Log($"[LobbyManager] Relay created with join code: {relayJoinCode}");

            // 2. Get player info for lobby
            string playerId = AuthenticationService.Instance.PlayerId;
            string playerName = PlayerPrefs.GetString(BootstrapUI.PlayerNameKey, "Host");

            // 3. Configure Lobby options
            CreateLobbyOptions options = new CreateLobbyOptions
            {
                IsPrivate = false,
                Player = new Player
                {
                    Data = new Dictionary<string, PlayerDataObject>
                    {
                        { "PlayerName", new PlayerDataObject(PlayerDataObject.VisibilityOptions.Member, playerName) }
                    }
                },
                Data = new Dictionary<string, DataObject>
                {
                    { KEY_JOIN_CODE, new DataObject(DataObject.VisibilityOptions.Member, relayJoinCode) }
                }
            };

            // 4. Create the Lobby
            Lobby lobby = await LobbyService.Instance.CreateLobbyAsync(lobbyName, maxPlayers, options);
            currentLobby = lobby;
            isHost = true;
            
            Debug.Log($"[LobbyManager] Lobby '{lobbyName}' created! Lobby ID: {lobby.Id}, Players: {lobby.Players.Count}");
            
            // 5. Load the game scene
            Debug.Log("[LobbyManager] Loading GameScene...");
            NetworkManager.Singleton.SceneManager.LoadScene("GameScene", LoadSceneMode.Single);
            
            return true;
        }
        catch (LobbyServiceException e)
        {
            Debug.LogError($"[LobbyManager] Failed to create lobby: {e.Message}\nReason: {e.Reason}");
            return false;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[LobbyManager] Unexpected error creating lobby: {e.Message}");
            return false;
        }
    }

    // Keep the lobby alive (only for host)
    private async void HandleLobbyHeartbeat()
    {
        if (currentLobby != null && isHost)
        {
            heartbeatTimer -= Time.deltaTime;
            if (heartbeatTimer <= 0f)
            {
                heartbeatTimer = HEARTBEAT_INTERVAL;
                try
                {
                    await LobbyService.Instance.SendHeartbeatPingAsync(currentLobby.Id);
                }
                catch (LobbyServiceException e)
                {
                    Debug.LogWarning($"[LobbyManager] Heartbeat failed: {e.Message}");
                }
            }
        }
    }

    // --- QUEST 4: Drop-In Boarding (Query) ---
    public async Task<List<Lobby>> GetActiveLobbies()
    {
        try
        {
            Debug.Log("[LobbyManager] Querying active lobbies...");

            QueryLobbiesOptions options = new QueryLobbiesOptions
            {
                Count = 20,
                Filters = new List<QueryFilter>
                {
                    new QueryFilter(QueryFilter.FieldOptions.AvailableSlots, "0", QueryFilter.OpOptions.GT)
                }
            };

            QueryResponse response = await Lobbies.Instance.QueryLobbiesAsync(options);
            Debug.Log($"[LobbyManager] Found {response.Results.Count} active lobbies.");
            return response.Results;
        }
        catch (LobbyServiceException e)
        {
            Debug.LogError($"[LobbyManager] Failed to query lobbies: {e.Message}");
            return new List<Lobby>();
        }
    }

    // --- QUEST 4: Drop-In Boarding (Join) ---
    public async Task<bool> JoinLobby(Lobby lobby)
    {
        try
        {
            Debug.Log($"[LobbyManager] Attempting to join lobby: {lobby.Name} (ID: {lobby.Id})");

            // 1. Get player info
            string playerName = PlayerPrefs.GetString(BootstrapUI.PlayerNameKey, "Player");

            // 2. Join lobby with player data
            JoinLobbyByIdOptions options = new JoinLobbyByIdOptions
            {
                Player = new Player
                {
                    Data = new Dictionary<string, PlayerDataObject>
                    {
                        { "PlayerName", new PlayerDataObject(PlayerDataObject.VisibilityOptions.Member, playerName) }
                    }
                }
            };

            Lobby joinedLobby = await Lobbies.Instance.JoinLobbyByIdAsync(lobby.Id, options);
            currentLobby = joinedLobby;
            isHost = false;
            
            Debug.Log($"[LobbyManager] Successfully joined lobby. Players in lobby: {joinedLobby.Players.Count}");

            // 3. Extract the Relay Code
            if (!joinedLobby.Data.ContainsKey(KEY_JOIN_CODE))
            {
                Debug.LogError("[LobbyManager] Lobby data doesn't contain relay join code!");
                return false;
            }

            string relayJoinCode = joinedLobby.Data[KEY_JOIN_CODE].Value;
            Debug.Log($"[LobbyManager] Retrieved relay join code: {relayJoinCode}");

            // 4. Join the relay
            bool relayJoinSuccess = await RelayManager.Instance.JoinRelay(relayJoinCode);
            
            if (relayJoinSuccess)
            {
                Debug.Log("[LobbyManager] Successfully joined relay. Waiting for scene sync...");
                return true;
            }
            else
            {
                Debug.LogError("[LobbyManager] Failed to join relay.");
                // Leave the lobby if relay join failed
                await LeaveLobby();
                return false;
            }
        }
        catch (LobbyServiceException e)
        {
            Debug.LogError($"[LobbyManager] Failed to join lobby: {e.Message}\nReason: {e.Reason}");
            return false;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[LobbyManager] Unexpected error joining lobby: {e.Message}");
            return false;
        }
    }

    // Remove a specific player from the lobby (called when client disconnects)
    public async void RemovePlayerFromLobby(string playerAuthId)
    {
        if (currentLobby == null || !isHost)
        {
            return;
        }

        try
        {
            Debug.Log($"[LobbyManager] Attempting to remove player {playerAuthId} from lobby...");
            await LobbyService.Instance.RemovePlayerAsync(currentLobby.Id, playerAuthId);
            Debug.Log($"[LobbyManager] Player {playerAuthId} removed from lobby.");
            
            // Refresh lobby data to get updated player count
            currentLobby = await LobbyService.Instance.GetLobbyAsync(currentLobby.Id);
            Debug.Log($"[LobbyManager] Lobby updated. Current players: {currentLobby.Players.Count}");
        }
        catch (LobbyServiceException e)
        {
            Debug.LogWarning($"[LobbyManager] Failed to remove player from lobby: {e.Message}");
        }
    }

    // Leave lobby (for clients)
    public async Task LeaveLobby()
    {
        if (currentLobby != null && !isHost)
        {
            try
            {
                string playerId = AuthenticationService.Instance.PlayerId;
                Debug.Log($"[LobbyManager] Leaving lobby {currentLobby.Id}...");
                await LobbyService.Instance.RemovePlayerAsync(currentLobby.Id, playerId);
                currentLobby = null;
                Debug.Log("[LobbyManager] Successfully left lobby.");
            }
            catch (LobbyServiceException e)
            {
                Debug.LogError($"[LobbyManager] Failed to leave lobby: {e.Message}");
            }
        }
    }

    // Delete lobby (for host)
    public async Task DeleteLobby()
    {
        if (currentLobby != null && isHost)
        {
            try
            {
                Debug.Log($"[LobbyManager] Deleting lobby {currentLobby.Id}...");
                await LobbyService.Instance.DeleteLobbyAsync(currentLobby.Id);
                currentLobby = null;
                isHost = false;
                Debug.Log("[LobbyManager] Lobby deleted successfully.");
            }
            catch (LobbyServiceException e)
            {
                Debug.LogError($"[LobbyManager] Failed to delete lobby: {e.Message}");
            }
        }
    }

    private void OnDestroy()
    {
        // Clean up lobby when manager is destroyed
        if (currentLobby != null)
        {
            if (isHost)
            {
                _ = DeleteLobby();
            }
            else
            {
                _ = LeaveLobby();
            }
        }
    }
}