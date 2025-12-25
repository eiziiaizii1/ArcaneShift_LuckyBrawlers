using Unity.Netcode;
using UnityEngine;
using System.Collections;

public class CoinSpawner : NetworkBehaviour
{
    [SerializeField] private GameObject coinPrefab;
    [SerializeField] private int maxCoinCount = 15; // Sahnede olması gereken toplam coin
    
    [Header("Yeşil Kare Sınırları (Scene ekranındaki değerler)")]
    public float minX = -20f; 
    public float maxX = 20f;  
    public float minY = -15f; 
    public float maxY = 15f;  

    [Header("Çakışma Kontrolü")]
    public float checkRadius = 0.5f; 
    public LayerMask obstacleLayer; // 'Coin' ve 'Wall' layerlarını seçmelisin

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            // İlk başlangıçta coinleri dağıt
            for (int i = 0; i < maxCoinCount; i++)
            {
                SpawnCoin();
            }
            // Sürekli kontrol eden bir döngü başlat (toplandıkça yenisi çıksın)
            StartCoroutine(RespawnRoutine());
        }
    }

    private IEnumerator RespawnRoutine()
    {
        while (true)
        {
            yield return new WaitForSeconds(3f); // Her 3 saniyede bir eksik var mı bak
            
            // Sahnedeki güncel coin sayısını bul
            int currentCoins = GameObject.FindGameObjectsWithTag("Coin").Length;
            
            if (currentCoins < maxCoinCount)
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
        int maxAttempts = 15; 

        while (!positionFound && maxAttempts > 0)
        {
            float randX = Random.Range(minX, maxX);
            float randY = Random.Range(minY, maxY);
            spawnPosition = new Vector2(randX, randY);

            // Çakışma kontrolü
            Collider2D hit = Physics2D.OverlapCircle(spawnPosition, checkRadius, obstacleLayer);
            
            if (hit == null)
            {
                positionFound = true;
            }
            maxAttempts--;
        }

        GameObject coin = Instantiate(coinPrefab, spawnPosition, Quaternion.identity);
        coin.GetComponent<NetworkObject>().Spawn();
    }

    // Görseldeki yeşil kareyi Scene ekranında görmeni sağlar
    private void OnDrawGizmos()
    {
        Gizmos.color = Color.green;
        Vector3 center = new Vector3((minX + maxX) / 2, (minY + maxY) / 2, 0);
        Vector3 size = new Vector3(maxX - minX, maxY - minY, 1);
        Gizmos.DrawWireCube(center, size);
    }
}
