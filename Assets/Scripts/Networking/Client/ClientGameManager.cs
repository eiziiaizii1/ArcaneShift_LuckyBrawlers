using System;
using System.Threading.Tasks;
using UnityEngine;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using Unity.Networking.Transport.Relay;
using UnityEngine.SceneManagement;
using Unity.Services.Core;

public class ClientGameManager
{
    private const string MenuSceneName = "Menu";
    private JoinAllocation allocation;
    
    public async Task<bool> InitAsync()
    {
        // 1. Initialise Unity Services
        await UnityServices.InitializeAsync();

        // 2. Try to authenticate the player
        var authState = await AuthenticationWrapper.DoAuth();
        return authState == AuthState.Authenticated;
    }

    public void GoToMenu()
    {
        SceneManager.LoadScene(MenuSceneName);
    }
   
   public async Task StartClientAsync(string joinCode)
   {
       try
       {
           allocation = await RelayService.Instance.JoinAllocationAsync(joinCode);
       }
        catch (Exception e)
        {
            Debug.LogError(e);
            return;
        }
    
        var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
        var relayServerData = new RelayServerData(allocation, "dtls");
        transport.SetRelayServerData(relayServerData);
        NetworkManager.Singleton.StartClient();
    }
}
