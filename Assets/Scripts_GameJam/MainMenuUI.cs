using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class MainMenuUI : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private Button createGameButton;
    [SerializeField] private Button joinGameButton;
    [SerializeField] private TMP_InputField joinCodeInput;
    [SerializeField] private TextMeshProUGUI statusText;

    private void Start()
    {
        createGameButton.onClick.AddListener(OnCreateGameClicked);
        joinGameButton.onClick.AddListener(OnJoinGameClicked);
        Debug.Log("[MainMenuUI] Button listeners registered.");
    }

    private async void OnCreateGameClicked()
    {
        Debug.Log("[MainMenuUI] Create Game clicked.");
        statusText.text = "Creating Relay...";
        string code = await RelayManager.Instance.CreateRelay();

        if (!string.IsNullOrEmpty(code))
        {
            statusText.text = $"Host Started! Code: {code}";
            // Copy code to clipboard for easy testing
            GUIUtility.systemCopyBuffer = code;
            Debug.Log($"Join Code Copied: {code}");
        }
        else
        {
            statusText.text = "Host failed to start.";
        }
    }

    private async void OnJoinGameClicked()
    {
        string code = joinCodeInput.text;
        if (string.IsNullOrEmpty(code))
        {
            Debug.LogWarning("[MainMenuUI] Join code missing.");
            return;
        }

        Debug.Log($"[MainMenuUI] Join Game clicked. Code: {code}");
        statusText.text = "Joining Relay...";
        bool success = await RelayManager.Instance.JoinRelay(code);

        if (success)
        {
            statusText.text = "Joined!";
        }
        else
        {
            statusText.text = "Join Failed.";
        }
    }
}
