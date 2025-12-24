using Unity.Netcode;
using UnityEngine;

public class FireballLogic : NetworkBehaviour
{
    [SerializeField] private float speed = 10f;
    [SerializeField] private int damage = 25; // Bir mermi 25 can götürsün (4 vuruşta ölüm)
    
    private Rigidbody2D rb;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            rb.linearVelocity = transform.up * speed;
            Destroy(gameObject, 3f);
        }
    }

    // BİR ŞEYE ÇARPINCA ÇALIŞIR
    private void OnTriggerEnter2D(Collider2D other)
    {
        // Sadece sunucu hasar hesaplar
        if (!IsServer) return;

        // 1. Kendi mermine çarpma!
        // Merminin sahibi (OwnerClientId) ile çarptığımız kişinin sahibi aynıysa işlem yapma
        if (other.TryGetComponent(out NetworkObject netObj))
        {
            if (netObj.OwnerClientId == OwnerClientId) return;
        }

        // 2. Çarptığımız şeyin canı var mı?
        if (other.TryGetComponent(out Health healthScript))
        {
            // Vur!
            healthScript.TakeDamage(damage, OwnerClientId);
            
            // Mermiyi yok et
            Destroy(gameObject);
        }
        else if (other.CompareTag("Wall")) // Duvara çarparsa da yok olsun (Opsiyonel)
        {
            Destroy(gameObject);
        }
    }
}
