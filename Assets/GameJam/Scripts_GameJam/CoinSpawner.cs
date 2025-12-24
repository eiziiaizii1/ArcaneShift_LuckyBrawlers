using Unity.Netcode;
using UnityEngine;

public class CoinSpawner : NetworkBehaviour
{
    [SerializeField] private GameObject coinPrefab;
    [SerializeField] private int initialCoinCount = 15;
    
    [Header("Harita Sınırları (Kamera Dışını da Kapsasın)")]
    public float minX = -20f; // Haritanın en solu
    public float maxX = 20f;  // Haritanın en sağı
    public float minY = -15f; // Haritanın en altı
    public float maxY = 15f;  // Haritanın en üstü

    [Header("Çakışma Kontrolü")]
    public float checkRadius = 0.5f; // Diğer coinlere olan mesafe
    public LayerMask obstacleLayer; // Altınların üst üste binmemesi için

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            for (int i = 0; i < initialCoinCount; i++)
            {
                SpawnCoin();
            }
        }
    }

    public void SpawnCoin()
    {
        if (!IsServer) return;

        Vector2 spawnPosition = Vector2.zero;
        bool positionFound = false;
        int maxAttempts = 10; // Sonsuz döngüye girmemesi için deneme sınırı

        while (!positionFound && maxAttempts > 0)
        {
            // 1. Rastgele geniş bir pozisyon seç (Kamera dışını da kapsar)
            float randX = Random.Range(minX, maxX);
            float randY = Random.Range(minY, maxY);
            spawnPosition = new Vector2(randX, randY);

            // 2. Çakışma kontrolü: Bu noktada başka bir Collider var mı?
            // "Coin" layer'ını seçtiğinden emin ol
            Collider2D hit = Physics2D.OverlapCircle(spawnPosition, checkRadius, obstacleLayer);
            
            if (hit == null)
            {
                positionFound = true;
            }
            maxAttempts--;
        }

        // 3. Uygun yer bulunduysa spawn et
        GameObject coin = Instantiate(coinPrefab, spawnPosition, Quaternion.identity);
        coin.GetComponent<NetworkObject>().Spawn();
    }
}
