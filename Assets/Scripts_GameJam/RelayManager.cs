using System.Text;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Networking.Transport.Relay;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;
using UnityEngine.SceneManagement;

public class RelayManager : MonoBehaviour
{
    // Singleton for easy access
    public static RelayManager Instance { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[RelayManager] Duplicate instance detected. Destroying new instance.");
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
        Debug.Log("[RelayManager] Singleton instance set and marked DontDestroyOnLoad.");
    }

    private async void Start()
    {
        // Initialize Unity Services (Required for Relay/Lobby)
        Debug.Log("[RelayManager] Initializing Unity Services...");
        await UnityServices.InitializeAsync();
        Debug.Log("[RelayManager] Unity Services initialized.");

        // Sign in anonymously (Required before using Relay)
        if (!AuthenticationService.Instance.IsSignedIn)
        {
            Debug.Log("[RelayManager] Signing in anonymously...");
            await AuthenticationService.Instance.SignInAnonymouslyAsync();
            Debug.Log("[RelayManager] Signed in anonymously.");
        }
    }

    // HELPER: Construct the payload (Name + AuthID)
    private byte[] GetConnectionPayload()
    {
        // 1) Name from PlayerPrefs (Quest 5)
        string playerName = PlayerPrefs.GetString("PlayerName", "Unknown Wizard");

        // 2) Unique Auth ID from Unity Services
        string authId = AuthenticationService.Instance.PlayerId;

        // 3) Create user data
        UserData data = new UserData
        {
            userName = playerName,
            userAuthId = authId
        };

        // 4) Serialize to JSON -> bytes
        string jsonPayload = JsonUtility.ToJson(data);
        Debug.Log($"[RelayManager] Built connection payload for {playerName} ({authId}).");
        return Encoding.UTF8.GetBytes(jsonPayload);
    }

    /// <summary>
    /// HOST: Creates a relay allocation and starts the host.
    /// Returns the Join Code to share with clients.
    /// </summary>
    public async Task<string> CreateRelay(int maxConnections = 3)
    {
        try
        {
            Debug.Log($"[RelayManager] Creating relay allocation (maxConnections: {maxConnections})...");
            // 1. Create Allocation & Get Join Code (Same as before)
            Allocation allocation = await RelayService.Instance.CreateAllocationAsync(maxConnections);
            string joinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
            Debug.Log($"[RelayManager] Relay allocation created. Join code: {joinCode}");

            // 2. Configure Transport (Same as before)
            RelayServerData relayServerData = new RelayServerData(allocation, "dtls");
            NetworkManager.Singleton.GetComponent<UnityTransport>().SetRelayServerData(relayServerData);

            // 3. Inject Payload (Quest 6 - Same as before)
            NetworkManager.Singleton.NetworkConfig.ConnectionData = GetConnectionPayload();

            // 4. Start Host
            if (NetworkManager.Singleton.StartHost())
            {
                // --- THE FIX IS HERE ---
                // The Host tells the server to load the GameScene.
                // Netcode automatically syncs all connecting clients to this scene.
                NetworkManager.Singleton.SceneManager.LoadScene("GameScene", LoadSceneMode.Single);
                
                Debug.Log($"[RelayManager] Host started. Loading GameScene... Code: {joinCode}");
                return joinCode;
            }
            else
            {
                Debug.LogError("[RelayManager] Host failed to start.");
                return null;
            }
        }
        catch (RelayServiceException e)
        {
            Debug.LogError($"[RelayManager] Relay Create Error: {e.Message}");
            return null;
        }
    }

    /// <summary>
    /// CLIENT: Joins a relay allocation via code and starts the client.
    /// </summary>
    public async Task<bool> JoinRelay(string joinCode)
    {
        try
        {
            Debug.Log($"[RelayManager] Joining relay with code: {joinCode}");
            // Join the allocation using the code
            JoinAllocation joinAllocation = await RelayService.Instance.JoinAllocationAsync(joinCode);

            // NEW: Inject payload before starting client
            NetworkManager.Singleton.NetworkConfig.ConnectionData = GetConnectionPayload();

            // Configure transport (dtls must match host)
            RelayServerData relayServerData = new RelayServerData(joinAllocation, "dtls");
            NetworkManager.Singleton.GetComponent<UnityTransport>().SetRelayServerData(relayServerData);

            // Start Client
            NetworkManager.Singleton.StartClient();
            Debug.Log("[RelayManager] Client start requested.");

            return true;
        }
        catch (RelayServiceException e)
        {
            Debug.LogError($"[RelayManager] Relay Join Error: {e.Message}");
            return false;
        }
    }
}
