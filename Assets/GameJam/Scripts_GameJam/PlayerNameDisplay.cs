using Unity.Collections;
using Unity.Netcode;
using TMPro;
using UnityEngine;

public class PlayerNameDisplay : NetworkBehaviour
{
    public NetworkVariable<FixedString32Bytes> playerName = new NetworkVariable<FixedString32Bytes>();
    [SerializeField] private TextMeshProUGUI nameText;

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            var manager = NetworkManager.Singleton.GetComponent<GameConnectionManager>();
            if (manager != null)
            {
                string nameFromData = manager.GetPlayerName(OwnerClientId);
                playerName.Value = nameFromData;

                // [YENİ]: İsim sunucuda belirlendiği an Leaderboard'u güncelle
                UpdateLeaderboard(nameFromData);
            }
        }

        playerName.OnValueChanged += OnNameChanged;
        UpdateNameUI(playerName.Value.ToString());
    }

    private void UpdateLeaderboard(string finalName)
    {
        // Sahnedeki LeaderboardManager'ı bul ve ismi "Joining..."den gerçek isme çevir
        LeaderboardManager lb = Object.FindFirstObjectByType<LeaderboardManager>();
        if (lb != null)
        {
            lb.UpdatePlayerName(OwnerClientId, finalName);
        }
    }

    private void OnNameChanged(FixedString32Bytes oldName, FixedString32Bytes newName)
    {
        UpdateNameUI(newName.ToString());
        
        // Eğer client isen ve isim sonradan değişirse (nadir bir durum), listeyi yine de tetikle
        if (IsServer) UpdateLeaderboard(newName.ToString());
    }

    private void UpdateNameUI(string name)
    {
        if (nameText != null) nameText.text = name;
    }

    public override void OnNetworkDespawn()
    {
        playerName.OnValueChanged -= OnNameChanged;
    }
}
