using UnityEngine;
using TMPro;

public class JoinCodeDisplay : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI joinCodeText;

    private void Start()
    {
        // 1. Check if HostSingleton exists.
        // If we are a Client, HostSingleton.Instance might be null, 
        // or it might exist but not have a valid game manager/join code yet.
        if (HostSingleton.Instance == null || HostSingleton.Instance.GameManager == null) 
        {
            joinCodeText.gameObject.SetActive(false); // Hide the text for Clients
            return;
        }

        // 2. Get the code
        string code = HostSingleton.Instance.GameManager.JoinCode;

        // 3. Verify the code is not empty
        if (string.IsNullOrEmpty(code))
        {
            joinCodeText.gameObject.SetActive(false); // Hide if code is invalid
        }
        else
        {
            joinCodeText.gameObject.SetActive(true); // Ensure it's visible for Host
            joinCodeText.text = $"Join Code: {code}";
        }
    }
}