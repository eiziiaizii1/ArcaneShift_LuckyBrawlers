using Unity.Netcode;
using UnityEngine;
using TMPro;
using System.Collections.Generic;
using System.Linq;

public class LeaderboardManager : NetworkBehaviour
{
    [Header("UI References")]
    [SerializeField] private GameObject leaderboardPanel;
    [SerializeField] private Transform entryContainer; // VerticalLayoutGroup olan obje
    [SerializeField] private GameObject entryPrefab;    // Oyuncu satırı prefab'ı

    // Quest 15: NetworkList ile otomatik senkronizasyon
    public NetworkList<PlayerStateData> playerRoster;

    private List<GameObject> spawnedEntries = new List<GameObject>();

    private void Awake()
    {
        // NetworkList başlatma
        playerRoster = new NetworkList<PlayerStateData>();
    }


    public override void OnNetworkSpawn()
    {
        // Liste değiştiğinde UI'ı güncelle
        playerRoster.OnListChanged += (e) => UpdateUI();

        if (IsServer)
        {
            // Yeni biri bağlandığında listeye ekle
            NetworkManager.Singleton.OnClientConnectedCallback += AddPlayer;
            // Çıktığında listeden sil
            NetworkManager.Singleton.OnClientDisconnectCallback += RemovePlayer;

            // Halihazırda bağlı olanları ekle (Host için)
            foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
            {
                AddPlayer(client.ClientId);
            }
        }

        // Başlangıçta UI'ı bir kez tazele
        UpdateUI();
    }

private void AddPlayer(ulong id)
{
    if (!IsServer) return;

    // 1. Varsayılan isim belirle
    string finalName = "Joining...";

    // 2. Eğer GameConnectionManager'da bu oyuncunun ismi varsa direkt onu al
    var connManager = NetworkManager.Singleton.GetComponent<GameConnectionManager>();
    if (connManager != null)
    {
        string realName = connManager.GetPlayerName(id);
        if (!string.IsNullOrEmpty(realName)) 
        {
            finalName = realName;
        }
    }

    // 3. Listeye ekle
    playerRoster.Add(new PlayerStateData 
    { 
        ClientId = id, 
        PlayerName = finalName, 
        Score = 0,
        IsSlimeForm = false,
        CurrentScale = 1.0f
    });

    Debug.Log($"[Leaderboard] Oyuncu eklendi: {id} - İsim: {finalName}");
}

public void UpdatePlayerName(ulong id, string realName)
{
    if (!IsServer) return;

    for (int i = 0; i < playerRoster.Count; i++)
    {
        if (playerRoster[i].ClientId == id)
        {
            var data = playerRoster[i];
            data.PlayerName = realName; 
            playerRoster[i] = data; // Bu satır UI'daki o "Joining..." yazısını siler ve ismi yazar
            break;
        }
    }
}
    private void RemovePlayer(ulong id)
    {
        if (!IsServer) return;

        for (int i = 0; i < playerRoster.Count; i++)
        {
            if (playerRoster[i].ClientId == id)
            {
                playerRoster.RemoveAt(i);
                break;
            }
        }
    }

    public void UpdateScore(ulong id, int pointsToAdd)
{
    if (!IsServer) return;

    for (int i = 0; i < playerRoster.Count; i++)
    {
        if (playerRoster[i].ClientId == id)
        {
            var data = playerRoster[i];
            
            // Mevcut skorun üzerine gelen puanı ekliyoruz
            data.Score += pointsToAdd; 
            
            playerRoster[i] = data; // Değişikliği ağa yay
            Debug.Log($"[Score Update] Client: {id}, Yeni Skor: {data.Score}");
            break;
        }
    }
}

  private void UpdateUI()
    {
        // 1. Önceki satırları temizle
        foreach (var entry in spawnedEntries) if (entry != null) Destroy(entry);
        spawnedEntries.Clear();

        // 2. NetworkList'i düz bir listeye aktar (Sıralama için güvenli yol)
        List<PlayerStateData> sortedList = new List<PlayerStateData>();
        foreach (var p in playerRoster) sortedList.Add(p);

        // 3. Basit bir sıralama (Büyükten küçüğe)
        sortedList.Sort((a, b) => b.Score.CompareTo(a.Score));

        // 4. Her oyuncu için prefab oluştur
        for (int i = 0; i < sortedList.Count; i++)
        {
            var p = sortedList[i];
            GameObject newEntry = Instantiate(entryPrefab, entryContainer);
            spawnedEntries.Add(newEntry);

            // Eski çalışan mantığın: GetComponentsInChildren ile metinleri bulma
            var texts = newEntry.GetComponentsInChildren<TextMeshProUGUI>();
            
            if (texts.Length >= 2)
            {
                // Eğer prefab'ında 3 metin varsa: Rank, Name, Score
                if (texts.Length >= 3)
                {
                    texts[0].text = (i + 1).ToString() + "."; // Sıra
                    texts[1].text = p.PlayerName.ToString(); // İsim
                    texts[2].text = p.Score.ToString();      // Skor
                    
                    // Kendini Sarı Yap (Vurgula)
                    if (p.ClientId == NetworkManager.Singleton.LocalClientId)
                    {
                        texts[1].color = Color.yellow;
                        texts[1].fontStyle = FontStyles.Bold;
                    }
                }
                else // Sadece Name ve Score varsa (Eski halin)
                {
                    texts[0].text = p.PlayerName.ToString();
                    texts[1].text = p.Score.ToString();

                    // Kendini Sarı Yap
                    if (p.ClientId == NetworkManager.Singleton.LocalClientId)
                        texts[0].color = Color.yellow;
                }
            }
        }
    }
    private void Update()
    {
        // Tab tuşu kontrolü
        if (Input.GetKeyDown(KeyCode.Tab)) leaderboardPanel.SetActive(true);
        if (Input.GetKeyUp(KeyCode.Tab)) leaderboardPanel.SetActive(false);
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= AddPlayer;
            NetworkManager.Singleton.OnClientDisconnectCallback -= RemovePlayer;
        }
    }
}