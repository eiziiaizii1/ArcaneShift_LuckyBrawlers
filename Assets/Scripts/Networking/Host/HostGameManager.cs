using System;
using System.Threading.Tasks;
using UnityEngine;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using Unity.Networking.Transport.Relay;
using UnityEngine.SceneManagement;

public class HostGameManager
{
    // Constants for connection limits and scene names
    private const int MAX_CONNECTIONS = 20;
    private const string GAME_SCENE_NAME = "Game";

    // Fields to store allocation data
    private Allocation allocation;
    public string JoinCode { get; private set; }

    // The main async method linked to the UI
    public async Task StartHostAsync()
    {
        try
        {
            // 1. Create Allocation & Get Join Code
            allocation = await RelayService.Instance.CreateAllocationAsync(MAX_CONNECTIONS);
            JoinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
            
            Debug.Log(JoinCode);
            

            // 2. Configure Transport with Relay Data (using DTLS for security)
            var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
            var relayServerData = new RelayServerData(allocation, "dtls");
            transport.SetRelayServerData(relayServerData);

            // 3. Start the Host
            NetworkManager.Singleton.StartHost();

            // 4. Load the Gameplay Scene (Server drives the scene transition)
            NetworkManager.Singleton.SceneManager.LoadScene(
                GAME_SCENE_NAME, 
                LoadSceneMode.Single
            );
        }
        catch (Exception e)
        {
            Debug.LogError(e);
            return;
        }
    }
}