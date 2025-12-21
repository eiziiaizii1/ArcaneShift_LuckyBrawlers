using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

public class InGameQuitUI : MonoBehaviour
{
    [SerializeField] private Button leaveButton;

    private void Start()
    {
        if (leaveButton != null)
        {
            leaveButton.onClick.AddListener(OnLeaveClicked);
            Debug.Log("[InGameQuitUI] Leave button listener registered.");
        }
        else
        {
            Debug.LogError("[InGameQuitUI] Leave button is not assigned in inspector!");
        }
    }

    private void OnLeaveClicked()
    {
        Debug.Log("[InGameQuitUI] Leave button clicked. Initiating clean shutdown.");
        
        // Find the GameConnectionManager component on the NetworkManager
        if (NetworkManager.Singleton != null)
        {
            GameConnectionManager connectionManager = NetworkManager.Singleton.GetComponent<GameConnectionManager>();
            
            if (connectionManager != null)
            {
                Debug.Log("[InGameQuitUI] GameConnectionManager found. Calling QuitGame().");
                connectionManager.QuitGame();
            }
            else
            {
                // Fallback if GameConnectionManager is missing
                Debug.LogWarning("[InGameQuitUI] GameConnectionManager component not found on NetworkManager. Using fallback shutdown.");
                FallbackShutdown();
            }
        }
        else
        {
            Debug.LogError("[InGameQuitUI] NetworkManager.Singleton is null. Cannot quit properly.");
            // Last resort fallback
            FallbackShutdown();
        }
    }

    /// <summary>
    /// Fallback shutdown method if GameConnectionManager is not available.
    /// This is less clean but ensures the player can still exit.
    /// </summary>
    private void FallbackShutdown()
    {
        Debug.Log("[InGameQuitUI] Executing fallback shutdown sequence.");
        
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.Shutdown();
        }
        
        UnityEngine.SceneManagement.SceneManager.LoadScene("MainMenu");
    }

    private void OnDestroy()
    {
        // Clean up listener
        if (leaveButton != null)
        {
            leaveButton.onClick.RemoveListener(OnLeaveClicked);
        }
    }
}