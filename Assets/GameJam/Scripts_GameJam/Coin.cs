using Unity.Netcode;
using UnityEngine;

public class Coin : NetworkBehaviour
{
    [SerializeField] private int scoreValue = 10;

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!IsServer) return;

        if (other.CompareTag("Player"))
        {
            var networkObject = other.GetComponent<NetworkObject>();
            if (networkObject != null)
            {
                // 1. Skoru güncelle
                LeaderboardManager lb = Object.FindFirstObjectByType<LeaderboardManager>();
                if (lb != null) lb.UpdateScore(networkObject.OwnerClientId, scoreValue);

  
                CoinSpawner spawner = Object.FindFirstObjectByType<CoinSpawner>();
                if (spawner != null) spawner.SpawnCoin();
                GetComponent<NetworkObject>().Despawn();
            }
        }
    }
}
