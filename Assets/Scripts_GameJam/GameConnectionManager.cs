using System.Collections;
using System.Collections.Generic;
using System.Text;
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
            // Subscribe to connection events
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
        // Deserialize the connection payload
        byte[] payloadBytes = request.Payload;
        string jsonPayload = Encoding.UTF8.GetString(payloadBytes);
        UserData data = JsonUtility.FromJson<UserData>(jsonPayload);

        Debug.Log($"[Gatekeeper] Connection approval request from: {data.userName} (ClientID: {request.ClientNetworkId}, AuthID: {data.userAuthId})");

        // Store the client data for tracking
        if (!clientDataMap.ContainsKey(request.ClientNetworkId))
        {
            clientDataMap.Add(request.ClientNetworkId, data);
            Debug.Log($"[Gatekeeper] Added {data.userName} to client tracking dictionary.");
        }
        else
        {
            Debug.LogWarning($"[Gatekeeper] ClientID {request.ClientNetworkId} already exists in dictionary. Updating data.");
            clientDataMap[request.ClientNetworkId] = data;
        }

        // Approve the connection and create player object
        response.Approved = true;
        response.CreatePlayerObject = true;
        
        Debug.Log($"[Gatekeeper] Connection approved for {data.userName}.");
    }

    // --- QUEST 7: Presence & Departure - Disconnect Handling ---
    private void OnClientDisconnect(ulong clientId)
    {
        if (NetworkManager.Singleton.IsServer)
        {
            // SERVER: A client has disconnected
            if (clientDataMap.ContainsKey(clientId))
            {
                string playerName = clientDataMap[clientId].userName;
                Debug.Log($"[Gatekeeper] Player disconnected: {playerName} (ClientID: {clientId})");
                clientDataMap.Remove(clientId);
            }
            else
            {
                Debug.LogWarning($"[Gatekeeper] Unknown client disconnected (ClientID: {clientId})");
            }
        }
        else
        {
            // CLIENT: Host has disconnected or we lost connection
            Debug.Log($"[Client] Detected host disconnect (ClientID: {clientId}). Starting shutdown sequence.");
            StartCoroutine(ShutdownSequence());
        }
    }

    // --- QUEST 8: Shutdown Discipline - Clean Shutdown ---
    
    /// <summary>
    /// Public method that can be called by UI buttons to quit the game cleanly.
    /// </summary>
    public void QuitGame()
    {
        Debug.Log("[GameConnectionManager] QuitGame() called. Initiating shutdown sequence.");
        StartCoroutine(ShutdownSequence());
    }

    private IEnumerator ShutdownSequence()
    {
        Debug.Log("[GameConnectionManager] === Shutdown Sequence Started ===");
        
        // Step 1: Delete Lobby (Quest 3 - To be implemented later)
        // TODO: When lobby system is implemented, add:
        // if (LobbyManager.Instance != null)
        // {
        //     await LobbyManager.Instance.DeleteLobby();
        // }

        // Step 2: Shut down NetworkManager
        if (NetworkManager.Singleton != null)
        {
            Debug.Log("[GameConnectionManager] Shutting down NetworkManager...");
            NetworkManager.Singleton.Shutdown();
        }
        else
        {
            Debug.LogWarning("[GameConnectionManager] NetworkManager.Singleton is null during shutdown.");
        }

        // Step 3: Wait a frame to ensure Netcode's internal cleanup completes
        yield return null;

        // Step 4: Destroy NetworkManager GameObject to prevent stale state bugs
        // This is critical for Unity Editor testing where the GameObject persists between play sessions
        if (NetworkManager.Singleton != null)
        {
            Debug.Log("[GameConnectionManager] Destroying NetworkManager GameObject to prevent stale state.");
            Destroy(NetworkManager.Singleton.gameObject);
        }

        // Step 5: Return to MainMenu scene (lobby/connection scene)
        Debug.Log("[GameConnectionManager] Loading MainMenu scene.");
        SceneManager.LoadScene("MainMenu");
        
        Debug.Log("[GameConnectionManager] === Shutdown Sequence Complete ===");
    }

    // --- QUEST 8: Cleanup on Destroy ---
    private void OnDestroy()
    {
        // Unsubscribe from all callbacks to prevent memory leaks
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.ConnectionApprovalCallback -= ApprovalCheck;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnect;
            Debug.Log("[GameConnectionManager] Unsubscribed from NetworkManager callbacks.");
        }
    }

    // --- QUEST 8: Handle Application Quit (Additional Safety) ---
    private void OnApplicationQuit()
    {
        // If we're the host and the application is closing, clean up the lobby
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
        {
            Debug.Log("[GameConnectionManager] Application quitting on host. Lobby cleanup pending.");
            // TODO: When lobby system is implemented, add:
            // LobbyManager.Instance?.DeleteLobby();
            // This prevents "ghost lobbies" from staying active on Unity Gaming Services
        }
    }

    // --- DEBUG HELPER: View Connected Clients ---
    public void LogConnectedClients()
    {
        Debug.Log($"[GameConnectionManager] === Connected Clients ({clientDataMap.Count}) ===");
        foreach (var kvp in clientDataMap)
        {
            Debug.Log($"  ClientID: {kvp.Key} | Name: {kvp.Value.userName} | AuthID: {kvp.Value.userAuthId}");
        }
    }
}