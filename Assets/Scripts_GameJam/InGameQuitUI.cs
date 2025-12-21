using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

public class InGameQuitUI : MonoBehaviour
{
    [SerializeField] private Button leaveButton;

    private void Start()
    {
        leaveButton.onClick.AddListener(OnLeaveClicked);
        Debug.Log("[InGameQuitUI] Leave button listener registered.");
    }

    private void OnLeaveClicked()
    {
        Debug.Log("[InGameQuitUI] Leave clicked. Attempting clean shutdown.");
        // Find the manager and trigger the clean shutdown
        // Note: GameConnectionManager is on the NetworkManager object
        var connectionManager = NetworkManager.Singleton.GetComponent<GameConnectionManager>();
        
        if (connectionManager != null)
        {
            Debug.Log("[InGameQuitUI] GameConnectionManager found. Calling QuitGame().");
            connectionManager.QuitGame();
        }
        else
        {
            // Fallback if something is wrong
            Debug.LogWarning("[InGameQuitUI] GameConnectionManager missing. Falling back to NetworkManager shutdown.");
            NetworkManager.Singleton.Shutdown();
            UnityEngine.SceneManagement.SceneManager.LoadScene("MainMenu");
        }
    }
}
