using Unity.Netcode;
using UnityEngine;

public class FireballLogic : NetworkBehaviour
{
    [SerializeField] private float speed = 10f;
    [SerializeField] private int damage = 25; 
    
    private Rigidbody2D rb;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
    }

    // PlayerController'dan hızı almak için yeni metod
    public void SetSpeed(float newSpeed)
    {
        speed = newSpeed;
        // Eğer mermi çoktan spawn olduysa hızı güncelle
        if (rb != null) rb.linearVelocity = transform.up * speed;
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            rb.linearVelocity = transform.up * speed;
            Destroy(gameObject, 3f);
        }
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!IsServer) return;

        if (other.TryGetComponent(out NetworkObject netObj))
        {
            if (netObj.OwnerClientId == OwnerClientId) return;
        }

        if (other.TryGetComponent(out Health healthScript))
        {
            healthScript.TakeDamage(damage, OwnerClientId);
            Destroy(gameObject);
        }
        else if (other.CompareTag("Wall")) 
        {
            Destroy(gameObject);
        }
    }
}
