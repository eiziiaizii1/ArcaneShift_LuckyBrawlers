using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

public class GameConnectionManager : MonoBehaviour
{
    private Dictionary<ulong, UserData> clientDataMap = new Dictionary<ulong, UserData>();

    private void Start()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.ConnectionApprovalCallback += ApprovalCheck;
            NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnect;
            Debug.Log("[GameConnectionManager] Subscribed to connection approval and disconnect callbacks.");
        }
        else
        {
            Debug.LogWarning("[GameConnectionManager] NetworkManager.Singleton is null on Start.");
        }
    }

    // --- QUEST 6: Gatekeeper Approval ---
    private void ApprovalCheck(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
    {
        try
        {
            byte[] payloadBytes = request.Payload;
            
            if (payloadBytes == null || payloadBytes.Length == 0)
            {
                Debug.LogWarning($"[Gatekeeper] Empty payload received from ClientID: {request.ClientNetworkId}");
                response.Approved = false;
                response.Reason = "Empty connection payload";
                return;
            }

            string jsonPayload = Encoding.UTF8.GetString(payloadBytes);
            UserData data = JsonUtility.FromJson<UserData>(jsonPayload);

            if (data == null || string.IsNullOrEmpty(data.userName))
            {
                Debug.LogWarning($"[Gatekeeper] Invalid user data from ClientID: {request.ClientNetworkId}");
                response.Approved = false;
                response.Reason = "Invalid user data";
                return;
            }

            Debug.Log($"[Gatekeeper] Connection request from: {data.userName} (ClientID: {request.ClientNetworkId}, AuthID: {data.userAuthId})");

            if (!clientDataMap.ContainsKey(request.ClientNetworkId))
            {
                clientDataMap.Add(request.ClientNetworkId, data);
                Debug.Log($"[Gatekeeper] Added {data.userName} to client tracking dictionary.");
            }
            else
            {
                Debug.LogWarning($"[Gatekeeper] ClientID {request.ClientNetworkId} already exists. Updating data.");
                clientDataMap[request.ClientNetworkId] = data;
            }

            response.Approved = true;
            response.CreatePlayerObject = true;
            
            Debug.Log($"[Gatekeeper] Connection APPROVED for {data.userName}.");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[Gatekeeper] Error during approval: {e.Message}");
            response.Approved = false;
            response.Reason = "Server error during approval";
        }
    }
    
    // LeaderboardManager'ın oyuncu ismini çekebilmesi için bu fonksiyonu ekle
    public string GetPlayerName(ulong clientId)
{
        if (clientDataMap.TryGetValue(clientId, out UserData data))
        {
              return data.userName;
        }
               return null;
}
    // --- QUEST 7: Presence & Departure ---
    private void OnClientDisconnect(ulong clientId)
    {
        if (NetworkManager.Singleton.IsServer)
        {
            // SERVER: A client has disconnected
            if (clientDataMap.ContainsKey(clientId))
            {
                string playerName = clientDataMap[clientId].userName;
                string authId = clientDataMap[clientId].userAuthId;
                Debug.Log($"[Gatekeeper] Player disconnected: {playerName} (ClientID: {clientId}, AuthID: {authId})");
                
                // Remove from tracking
                clientDataMap.Remove(clientId);
                
                // Update lobby to remove this player
                if (LobbyManager.Instance != null)
                {
                    LobbyManager.Instance.RemovePlayerFromLobby(authId);
                }
            }
            else
            {
                Debug.LogWarning($"[Gatekeeper] Unknown client disconnected (ClientID: {clientId})");
            }
        }
        else
        {
            // CLIENT: Host has disconnected or we lost connection
            Debug.Log($"[Client] Detected disconnect (ClientID: {clientId}). Starting shutdown sequence.");
            StartCoroutine(ShutdownSequence());
        }
    }

    // --- QUEST 8: Shutdown Discipline ---
    public void QuitGame()
    {
        Debug.Log("[GameConnectionManager] QuitGame() called. Initiating shutdown sequence.");
        StartCoroutine(ShutdownSequence());
    }

    private IEnumerator ShutdownSequence()
    {
        Debug.Log("[GameConnectionManager] === Shutdown Sequence Started ===");
        
        // Step 1: Leave/Delete Lobby based on role
        if (NetworkManager.Singleton != null && LobbyManager.Instance != null)
        {
            if (NetworkManager.Singleton.IsServer)
            {
                // Host: Delete the entire lobby
                Debug.Log("[GameConnectionManager] Host shutting down - deleting lobby...");
                Task deleteTask = LobbyManager.Instance.DeleteLobby();
                
                float timeoutCounter = 0f;
                while (!deleteTask.IsCompleted && timeoutCounter < 5f)
                {
                    timeoutCounter += Time.deltaTime;
                    yield return null;
                }
                
                if (deleteTask.IsCompleted)
                {
                    Debug.Log("[GameConnectionManager] Lobby deleted successfully.");
                }
                else
                {
                    Debug.LogWarning("[GameConnectionManager] Lobby deletion timed out.");
                }
            }
            else
            {
                // Client: Just leave the lobby
                Debug.Log("[GameConnectionManager] Client shutting down - leaving lobby...");
                Task leaveTask = LobbyManager.Instance.LeaveLobby();
                
                float timeoutCounter = 0f;
                while (!leaveTask.IsCompleted && timeoutCounter < 5f)
                {
                    timeoutCounter += Time.deltaTime;
                    yield return null;
                }
                
                if (leaveTask.IsCompleted)
                {
                    Debug.Log("[GameConnectionManager] Left lobby successfully.");
                }
                else
                {
                    Debug.LogWarning("[GameConnectionManager] Leave lobby timed out.");
                }
            }
        }

        // Step 2: Shut down NetworkManager
        if (NetworkManager.Singleton != null)
        {
            Debug.Log("[GameConnectionManager] Shutting down NetworkManager...");
            NetworkManager.Singleton.Shutdown();
        }

        // Step 3: Wait for cleanup
        yield return null;

        // Step 4: Destroy NetworkManager GameObject
        if (NetworkManager.Singleton != null)
        {
            Debug.Log("[GameConnectionManager] Destroying NetworkManager GameObject.");
            Destroy(NetworkManager.Singleton.gameObject);
        }

        // Step 5: Return to MainMenu
        Debug.Log("[GameConnectionManager] Loading MainMenu scene.");
        SceneManager.LoadScene("MainMenu");
        
        Debug.Log("[GameConnectionManager] === Shutdown Sequence Complete ===");
    }

    private void OnDestroy()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.ConnectionApprovalCallback -= ApprovalCheck;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnect;
            Debug.Log("[GameConnectionManager] Unsubscribed from NetworkManager callbacks.");
        }
    }

    private void OnApplicationQuit()
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
        {
            Debug.Log("[GameConnectionManager] Application quitting on host. Initiating lobby cleanup.");
            if (LobbyManager.Instance != null)
            {
                _ = LobbyManager.Instance.DeleteLobby();
            }
        }
    }

    public void LogConnectedClients()
    {
        Debug.Log($"[GameConnectionManager] === Connected Clients ({clientDataMap.Count}) ===");
        foreach (var kvp in clientDataMap)
        {
            Debug.Log($"  ClientID: {kvp.Key} | Name: {kvp.Value.userName} | AuthID: {kvp.Value.userAuthId}");
        }
    }
}