using UnityEngine;
using TMPro;
using UnityEngine.UI;
using Unity.Services.Lobbies.Models;

public class LobbyItem : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI lobbyNameText;
    [SerializeField] private TextMeshProUGUI playerCountText;
    [SerializeField] private Button joinButton;

    private Lobby lobby;
    private MainMenuUI mainMenu;

    public void Initialize(Lobby lobby, MainMenuUI mainMenu)
    {
        this.lobby = lobby;
        this.mainMenu = mainMenu;

        lobbyNameText.text = lobby.Name;
        playerCountText.text = $"{lobby.Players.Count}/{lobby.MaxPlayers}";
        
        joinButton.onClick.AddListener(OnJoinClicked);
    }

    private void OnJoinClicked()
    {
        if (mainMenu != null)
        {
            mainMenu.JoinSpecificLobby(lobby);
        }
    }
}