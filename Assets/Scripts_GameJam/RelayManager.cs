using System;
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

public class RelayManager : MonoBehaviour
{
    public static RelayManager Instance { get; private set; }

    public event Action OnRelayReady;
    public bool IsRelayReady { get; private set; } = false;

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
        await InitializeUnityServices();
    }

    private async Task InitializeUnityServices()
    {
        try
        {
            Debug.Log("[RelayManager] Initializing Unity Services...");
            await UnityServices.InitializeAsync();
            Debug.Log("[RelayManager] Unity Services initialized.");

            if (!AuthenticationService.Instance.IsSignedIn)
            {
                Debug.Log("[RelayManager] Signing in anonymously...");
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
                Debug.Log($"[RelayManager] Signed in anonymously. Player ID: {AuthenticationService.Instance.PlayerId}");
            }

            IsRelayReady = true;
            OnRelayReady?.Invoke();
            Debug.Log("[RelayManager] Ready for interactions.");
        }
        catch (Exception e)
        {
            Debug.LogError($"[RelayManager] Failed to initialize Unity Services: {e.Message}");
        }
    }

    private byte[] GetConnectionPayload()
    {
        string playerName = PlayerPrefs.GetString(BootstrapUI.PlayerNameKey, "Unknown Wizard");
        string authId = AuthenticationService.Instance.PlayerId;

        UserData data = new UserData
        {
            userName = playerName,
            userAuthId = authId
        };

        string jsonPayload = JsonUtility.ToJson(data);
        Debug.Log($"[RelayManager] Built connection payload for {playerName} ({authId}).");
        return Encoding.UTF8.GetBytes(jsonPayload);
    }

    /// <summary>
    /// HOST: Creates a relay allocation and starts the host.
    /// Returns the Join Code. Does NOT load scenes - let LobbyManager handle that.
    /// </summary>
    public async Task<string> CreateRelay(int maxConnections = 3)
    {
        try
        {
            Debug.Log($"[RelayManager] Creating relay allocation (maxConnections: {maxConnections})...");

            // 1. Create Allocation & Get Join Code
            Allocation allocation = await RelayService.Instance.CreateAllocationAsync(maxConnections);
            string joinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
            Debug.Log($"[RelayManager] Relay allocation created. Join code: {joinCode}");

            // 2. Configure Transport with DTLS
            RelayServerData relayServerData = new RelayServerData(allocation, "dtls");
            
            if (NetworkManager.Singleton == null)
            {
                Debug.LogError("[RelayManager] NetworkManager.Singleton is null!");
                return null;
            }

            UnityTransport transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
            if (transport == null)
            {
                Debug.LogError("[RelayManager] UnityTransport component not found on NetworkManager!");
                return null;
            }

            transport.SetRelayServerData(relayServerData);
            Debug.Log("[RelayManager] Transport configured with relay server data.");

            // 3. Inject Payload (Must be set BEFORE StartHost)
            NetworkManager.Singleton.NetworkConfig.ConnectionData = GetConnectionPayload();

            // 4. Start Host
            bool startedSuccessfully = NetworkManager.Singleton.StartHost();
            
            if (startedSuccessfully)
            {
                Debug.Log($"[RelayManager] Host started successfully. Join Code: {joinCode}");
                return joinCode;
            }
            else
            {
                Debug.LogError("[RelayManager] Failed to start host.");
                return null;
            }
        }
        catch (RelayServiceException e)
        {
            Debug.LogError($"[RelayManager] Relay creation failed: {e.Message}\nReason: {e.Reason}");
            return null;
        }
        catch (Exception e)
        {
            Debug.LogError($"[RelayManager] Unexpected error creating relay: {e.Message}");
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
            Debug.Log($"[RelayManager] Attempting to join relay with code: {joinCode}");

            // 1. Join the allocation using the code
            JoinAllocation joinAllocation = await RelayService.Instance.JoinAllocationAsync(joinCode);
            Debug.Log("[RelayManager] Join allocation successful.");

            // 2. Configure transport with DTLS
            RelayServerData relayServerData = new RelayServerData(joinAllocation, "dtls");
            
            if (NetworkManager.Singleton == null)
            {
                Debug.LogError("[RelayManager] NetworkManager.Singleton is null!");
                return false;
            }

            UnityTransport transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
            if (transport == null)
            {
                Debug.LogError("[RelayManager] UnityTransport component not found on NetworkManager!");
                return false;
            }

            transport.SetRelayServerData(relayServerData);
            Debug.Log("[RelayManager] Transport configured with relay server data.");

            // 3. Inject payload (Must be set BEFORE StartClient)
            NetworkManager.Singleton.NetworkConfig.ConnectionData = GetConnectionPayload();

            // 4. Start Client
            bool startedSuccessfully = NetworkManager.Singleton.StartClient();
            
            if (startedSuccessfully)
            {
                Debug.Log("[RelayManager] Client started successfully. Awaiting scene sync from host.");
                return true;
            }
            else
            {
                Debug.LogError("[RelayManager] Failed to start client.");
                return false;
            }
        }
        catch (RelayServiceException e)
        {
            Debug.LogError($"[RelayManager] Relay join failed: {e.Message}\nReason: {e.Reason}");
            return false;
        }
        catch (Exception e)
        {
            Debug.LogError($"[RelayManager] Unexpected error joining relay: {e.Message}");
            return false;
        }
    }
}