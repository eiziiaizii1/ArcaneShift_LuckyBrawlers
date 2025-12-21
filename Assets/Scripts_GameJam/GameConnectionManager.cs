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
            // Subscribe to events
            NetworkManager.Singleton.ConnectionApprovalCallback += ApprovalCheck;
            NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnect;
            Debug.Log("[GameConnectionManager] Subscribed to connection approval and disconnect callbacks.");
        }
        else
        {
            Debug.LogWarning("[GameConnectionManager] NetworkManager.Singleton missing on Start.");
        }
    }

    // --- QUEST 6: Gatekeeper (Kept same as before) ---
    private void ApprovalCheck(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
    {
        byte[] payloadBytes = request.Payload;
        string jsonPayload = Encoding.UTF8.GetString(payloadBytes);
        UserData data = JsonUtility.FromJson<UserData>(jsonPayload);

        Debug.Log($"[Gatekeeper] Approval Request: {data.userName} ({request.ClientNetworkId})");

        if (!clientDataMap.ContainsKey(request.ClientNetworkId))
        {
            clientDataMap.Add(request.ClientNetworkId, data);
        }

        response.Approved = true;
        response.CreatePlayerObject = true;
    }

    // --- QUEST 7: Disconnect Logic (Updated) ---
    private void OnClientDisconnect(ulong clientId)
    {
        if (NetworkManager.Singleton.IsServer)
        {
            if (clientDataMap.ContainsKey(clientId))
            {
                Debug.Log($"[Gatekeeper] Player Disconnected: {clientDataMap[clientId].userName}");
                clientDataMap.Remove(clientId);
            }
            else
            {
                Debug.LogWarning($"[Gatekeeper] Unknown client disconnected: {clientId}");
            }
        }
        else
        {
            // Client detected that Host shutdown or connection was lost
            Debug.Log($"[Client] Host disconnected (clientId: {clientId}). Cleaning up...");
            StartCoroutine(ShutdownSequence());
        }
    }

    // --- QUEST 8: Shutdown Discipline ---
    
    // Public method for Quit Buttons to call
    public void QuitGame()
    {
        Debug.Log("[GameConnectionManager] QuitGame requested. Starting shutdown sequence.");
        StartCoroutine(ShutdownSequence());
    }

    private System.Collections.IEnumerator ShutdownSequence()
    {
        Debug.Log("[GameConnectionManager] Shutdown sequence started.");
        // 1. TODO: Delete Lobby (Quest 3) - We will add this later when Lobby is built
        // LobbyManager.Instance.DeleteLobby(); 

        // 2. Shut down NetworkManager 
        if (NetworkManager.Singleton != null)
        {
            Debug.Log("[GameConnectionManager] Shutting down NetworkManager.");
            NetworkManager.Singleton.Shutdown();
        }
        else
        {
            Debug.LogWarning("[GameConnectionManager] NetworkManager.Singleton missing during shutdown.");
        }

        // 3. Wait a frame to ensure NGO cleans up internal state
        yield return null; 

        // 4. Destroy this NetworkManager GameObject so a fresh one is created in MainMenu
        // This prevents "Stale State" bugs in the Editor.
        if (NetworkManager.Singleton != null)
        {
            Debug.Log("[GameConnectionManager] Destroying NetworkManager GameObject.");
            Destroy(NetworkManager.Singleton.gameObject);
        }

        // 5. Return to Main Menu [cite: 343]
        Debug.Log("[GameConnectionManager] Loading MainMenu scene.");
        SceneManager.LoadScene("MainMenu");
    }

    // Safety Net: Ensure callbacks are removed if this object is destroyed 
    private void OnDestroy()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.ConnectionApprovalCallback -= ApprovalCheck;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnect;
            Debug.Log("[GameConnectionManager] Unsubscribed from callbacks on destroy.");
        }
    }
    
    // Safety Net: Handle application close [cite: 355]
    private void OnApplicationQuit()
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
        {
             Debug.Log("[GameConnectionManager] Application quit on host. Lobby cleanup pending.");
             // TODO: Delete Lobby (Quest 3)
             // This ensures we don't leave a ghost lobby on UGS if we just close the window
        }
    }
}
