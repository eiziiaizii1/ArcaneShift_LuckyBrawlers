using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

public class Health : NetworkBehaviour
{
    [Header("Health Settings")]
    [SerializeField] private int maxHealth = 100;
    public NetworkVariable<int> currentHealth = new NetworkVariable<int>(100);

    [Header("UI References")]
    [SerializeField] private Image healthBarFill;

    private SpriteRenderer spriteRenderer;
    private Collider2D col;
    private PlayerController controller;
    private Canvas nameCanvas;
    private Rigidbody2D rb;

    private void Awake()
    {
        // SpriteRenderer'ı bul - hem parent'ta hem child'da arayabilir
        spriteRenderer = GetComponent<SpriteRenderer>();
        if (spriteRenderer == null)
        {
            spriteRenderer = GetComponentInChildren<SpriteRenderer>();
        }
        
        col = GetComponent<Collider2D>();
        controller = GetComponent<PlayerController>();
        nameCanvas = GetComponentInChildren<Canvas>();
        rb = GetComponent<Rigidbody2D>();
    }

    public override void OnNetworkSpawn()
    {
        currentHealth.OnValueChanged += OnHealthChanged;
        UpdateHealthBar(currentHealth.Value);
    }

    private void OnHealthChanged(int oldHealth, int newHealth)
    {
        UpdateHealthBar(newHealth);
    }

    private void UpdateHealthBar(int current)
    {
        if (healthBarFill != null)
        {
            healthBarFill.fillAmount = (float)current / maxHealth;
        }
    }

   [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void TakeDamageServerRpc(int amount, ulong attackerId)
    {
        if (!IsServer) return;

        currentHealth.Value -= amount;

        if (currentHealth.Value <= 0 && spriteRenderer.enabled)
        {
            currentHealth.Value = 0;
            HandleDeath(attackerId);
        }
    }

    // Geriye dönük uyumluluk için eski metodu ServerRpc'ye yönlendiriyoruz
    public void TakeDamage(int amount, ulong attackerId)
    {
        TakeDamageServerRpc(amount, attackerId);
    }

    private void HandleDeath(ulong attackerId)
    {
        // Skor Güncelleme
        if (attackerId != OwnerClientId)
        {
            if (NetworkManager.Singleton.ConnectedClients.TryGetValue(attackerId, out NetworkClient attackerClient))
            {
                var attackerScript = attackerClient.PlayerObject.GetComponent<PlayerController>();
                if (attackerScript != null)
                {
                    attackerScript.score.Value += 1;
                    LeaderboardManager lb = Object.FindFirstObjectByType<LeaderboardManager>();
                    if (lb != null) lb.UpdateScore(attackerId, attackerScript.score.Value);
                }
            }
        }

        StartCoroutine(RespawnRoutine());
    }

    private IEnumerator RespawnRoutine()
    {
        // 1. Oyuncuyu Gizle
        SetPlayerState(false);

        // 2. Bekleme Süresi
        yield return new WaitForSeconds(3f);

        // 3. Işınlanma (Teleport) İşlemi
        GameObject[] spawnPoints = GameObject.FindGameObjectsWithTag("Respawn");
        if (spawnPoints.Length > 0)
        {
            Vector3 newPos = spawnPoints[Random.Range(0, spawnPoints.Length)].transform.position;

            // Network Transform'un çakışmaması için önce simülasyonu kapatıyoruz
            if (rb != null)
            {
                rb.simulated = false;
                rb.linearVelocity = Vector2.zero;
            }

            // Pozisyonu zorla değiştir
            transform.position = newPos;

            if (rb != null)
            {
                rb.position = newPos;
                rb.simulated = true;
            }
        }

        // 4. Oyuncuyu Canlandır
        currentHealth.Value = maxHealth;
        SetPlayerState(true);
    }

    private void SetPlayerState(bool isActive)
    {
        ToggleComponents(isActive);
        SetPlayerStateClientRpc(isActive);
    }

    [ClientRpc]
    private void SetPlayerStateClientRpc(bool isActive)
    {
        if (!IsServer) ToggleComponents(isActive);
    }

    private void ToggleComponents(bool isActive)
    {
        if (spriteRenderer != null) spriteRenderer.enabled = isActive;
        if (col != null) col.enabled = isActive;
        if (controller != null) 
        {
            controller.enabled = isActive;
            // IMPORTANT: Also control the arrow visibility
            controller.SetArrowVisibility(isActive);
        }
        if (nameCanvas != null) nameCanvas.enabled = isActive;
    }

    public override void OnDestroy()
    {
        currentHealth.OnValueChanged -= OnHealthChanged;
        base.OnDestroy();
    }
}