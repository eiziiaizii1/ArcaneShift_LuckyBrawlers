using System.Threading.Tasks;
using Unity.Services.Core;
using UnityEngine.SceneManagement;

public class ClientGameManager
{
    private const string MenuSceneName = "Menu";
    public async Task<bool> InitAsync()
    {
        // 1. Initialise Unity Services
        await UnityServices.InitializeAsync();

        // 2. Try to authenticate the player
        var authState = await AuthenticationWrapper.DoAuth();
        if (authState == AuthState.Authenticated) 
        {
            return true;
        }
        return false;
    }

    public void GoToMenu()
    {
        SceneManager.LoadScene(MenuSceneName);
    }


}
