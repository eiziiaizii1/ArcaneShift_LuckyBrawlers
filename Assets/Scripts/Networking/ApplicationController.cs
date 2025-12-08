using System.Threading.Tasks;
using UnityEngine;

public class ApplicationController : MonoBehaviour
{
    [SerializeField] private ClientSingleton clientPrefab;
    [SerializeField] private HostSingleton hostPrefab;
    private async void Start()
    {
        DontDestroyOnLoad(gameObject);

        bool isDedicatedServer = 
            SystemInfo.graphicsDeviceType == 
            UnityEngine.Rendering.GraphicsDeviceType.Null;

        await LaunchInMode(isDedicatedServer);

        // will do this later
    }

    private async Task LaunchInMode(bool isDedicatedServer)
    {
        if (isDedicatedServer)
        {
            // TODO: dedicated server setup
        }
        else
        {
            // Spawn and initialize client
            ClientSingleton clientSingleton = Instantiate(clientPrefab);
            bool authenticated = await clientSingleton.CreateClient();

            // Spawn host
            HostSingleton hostSingleton = Instantiate(hostPrefab);
            hostSingleton.CreateHost();

            if (authenticated)
            {
                clientSingleton.GameManager.GoToMenu();
            }
            else
            {
                // TODO: handle authentication failure (retry UI, message, etc.)
            }
        }
    }
}
