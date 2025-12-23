using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.SceneManagement;

public class BootstrapUI : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private TMP_InputField nameInputField;
    [SerializeField] private Button connectButton;

    // Const keys for persistence
    public const string PlayerNameKey = "PlayerName";
    private const int MaxNameLength = 12;
    private const int MinNameLength = 1;

    private void Start()
    {
        // Load saved name if it exists
        if (PlayerPrefs.HasKey(PlayerNameKey))
        {
            nameInputField.text = PlayerPrefs.GetString(PlayerNameKey);
            Debug.Log($"[BootstrapUI] Loaded saved player name: {nameInputField.text}");
        }
        else
        {
            Debug.Log("[BootstrapUI] No saved player name found.");
        }

        // Add listener to validate input every time it changes
        nameInputField.onValueChanged.AddListener(ValidateInput);
        
        // Add listener to button
        connectButton.onClick.AddListener(OnConnectClicked);

        // Initial validation check
        ValidateInput(nameInputField.text);
    }

    private void ValidateInput(string input)
    {
        // Quest 5: Enforce constraints (min 1, max 12)
        bool isValid = !string.IsNullOrWhiteSpace(input) && 
                       input.Length >= MinNameLength && 
                       input.Length <= MaxNameLength;

        connectButton.interactable = isValid;
        Debug.Log($"[BootstrapUI] Name validation. Length: {input?.Length ?? 0}, Valid: {isValid}");
    }

    private void OnConnectClicked()
    {
        // Quest 5: Save name to PlayerPrefs
        string cleanName = nameInputField.text.Trim();
        PlayerPrefs.SetString(PlayerNameKey, cleanName);
        PlayerPrefs.Save();
        Debug.Log($"[BootstrapUI] Saved player name: {cleanName}");

        // Transition to the main Networking/Menu scene
        Debug.Log("[BootstrapUI] Loading MainMenu scene.");
        SceneManager.LoadScene("MainMenu"); 
    }
}