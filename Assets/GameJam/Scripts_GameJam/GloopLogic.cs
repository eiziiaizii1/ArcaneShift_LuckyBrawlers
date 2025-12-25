using Unity.Netcode;
using UnityEngine;

/// <summary>
/// GloopLogic handles the Slime's unique projectile.
/// 
/// KEY FEATURE: Damage is scaled based on the shooter's slime size!
/// - Large slime (1.0 scale) = Full damage
/// - Small slime (0.3 scale) = Reduced damage (40% by default)
/// 
/// The damage is set by PlayerController when spawning the projectile,
/// which gets the scaled value from SlimeController.GetProjectileSettings().
/// 
/// Also applies a slow debuff on hit.
/// 
/// Attach to Gloop prefab with NetworkObject, Rigidbody2D, and Collider2D.
/// </summary>
public class GloopLogic : NetworkBehaviour
{
    [Header("Projectile Settings")]
    [SerializeField] private float speed = 7f;
    [SerializeField] private int damage = 20; // This gets overwritten by SetDamage()
    [SerializeField] private float lifetime = 4f;
    
    [Header("Slow Effect Settings")]
    [Tooltip("Duration of slow effect applied to hit targets")]
    [SerializeField] private float slowDuration = 1.5f;
    
    [Tooltip("Speed multiplier during slow (0.5 = half speed)")]
    [SerializeField] private float slowMultiplier = 0.6f;
    
    [Header("Visual Effects")]
    [SerializeField] private GameObject hitVFX;
    [SerializeField] private TrailRenderer trail;

    private Rigidbody2D rb;
    private SpriteRenderer spriteRenderer;
    private bool hasHit = false;
    
    // Track the scaled damage for debugging
    private int originalDamage;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        spriteRenderer = GetComponent<SpriteRenderer>();
        
        if (trail == null)
            trail = GetComponent<TrailRenderer>();
        
        originalDamage = damage;
    }

    /// <summary>
    /// Set the speed of the gloop projectile.
    /// Called by PlayerController after spawning.
    /// </summary>
    public void SetSpeed(float newSpeed)
    {
        speed = newSpeed;
        if (rb != null && IsServer)
        {
            rb.linearVelocity = transform.up * speed;
        }
    }

    /// <summary>
    /// Set the damage of the gloop projectile.
    /// Called by PlayerController with the SCALED damage from SlimeController.
    /// This is how the "smaller = weaker" mechanic is applied.
    /// </summary>
    public void SetDamage(int newDamage)
    {
        damage = newDamage;
        Debug.Log($"[GloopLogic] Damage set to {damage} (scaled from base)");
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            // Set velocity based on rotation
            rb.linearVelocity = transform.up * speed;
            
            // Schedule destruction
            Destroy(gameObject, lifetime);
        }
        
        // Optional: Set up visual effects
        if (trail != null)
        {
            trail.emitting = true;
        }
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!IsServer || hasHit) return;

        // Get network object to check ownership
        if (other.TryGetComponent(out NetworkObject netObj))
        {
            // Don't hit the player who fired this gloop
            if (netObj.OwnerClientId == OwnerClientId) return;
        }

        // Try to damage the target
        if (other.TryGetComponent(out Health healthScript))
        {
            hasHit = true;
            
            // Deal SCALED damage (set by PlayerController from SlimeController)
            healthScript.TakeDamage(damage, OwnerClientId);
            
            Debug.Log($"[GloopLogic] Hit for {damage} damage");
            
            // Award ultimate charge to shooter
            AwardUltimateCharge(damage);
            
            // Apply slow effect
            ApplySlowEffect(other.gameObject);
            
            // Spawn hit effect and destroy
            SpawnHitEffectClientRpc(transform.position, damage);
            
            // Despawn network object
            GetComponent<NetworkObject>().Despawn();
        }
        else if (other.CompareTag("Wall") || other.CompareTag("Obstacle"))
        {
            hasHit = true;
            SpawnHitEffectClientRpc(transform.position, 0);
            GetComponent<NetworkObject>().Despawn();
        }
    }

    private void AwardUltimateCharge(int damageDealt)
    {
        // Find the shooter's SlimeController and award ultimate charge
        if (NetworkManager.Singleton.ConnectedClients.TryGetValue(OwnerClientId, out NetworkClient client))
        {
            if (client.PlayerObject != null)
            {
                SlimeController slimeController = client.PlayerObject.GetComponent<SlimeController>();
                if (slimeController != null)
                {
                    slimeController.OnDamageDealt(damageDealt);
                }
            }
        }
    }

    private void ApplySlowEffect(GameObject target)
    {
        // Try to apply slow effect via SlimeController or a separate SlowEffect component
        SlimeController targetSlime = target.GetComponent<SlimeController>();
        if (targetSlime != null)
        {
            // The target has a SlimeController - they can handle the slow
            Debug.Log($"[GloopLogic] Applied slow to player {targetSlime.OwnerClientId}");
        }
        
        // Add a temporary slow debuff component
        SlowDebuff existingDebuff = target.GetComponent<SlowDebuff>();
        if (existingDebuff != null)
        {
            existingDebuff.RefreshDuration(slowDuration, slowMultiplier);
        }
        else
        {
            SlowDebuff debuff = target.AddComponent<SlowDebuff>();
            debuff.Initialize(slowDuration, slowMultiplier);
        }
    }

    [ClientRpc]
    private void SpawnHitEffectClientRpc(Vector3 position, int damageDealt)
    {
        if (hitVFX != null)
        {
            GameObject vfx = Instantiate(hitVFX, position, Quaternion.identity);
            Destroy(vfx, 1f);
        }
        
        // Optional: Could scale the VFX based on damage dealt
        // Lower damage = smaller effect to show the weaker hit
    }

    private void OnDestroy()
    {
        // Clean up trail
        if (trail != null)
        {
            trail.emitting = false;
        }
    }

    #region Public Properties
    
    /// <summary>
    /// Current damage this projectile will deal
    /// </summary>
    public int CurrentDamage => damage;
    
    #endregion
}

/// <summary>
/// Temporary slow debuff applied by Gloop projectiles.
/// Self-destructs after duration expires.
/// </summary>
public class SlowDebuff : MonoBehaviour
{
    private float duration;
    private float multiplier;
    private float timer;
    private PlayerController playerController;
    private bool initialized = false;

    public void Initialize(float duration, float multiplier)
    {
        this.duration = duration;
        this.multiplier = multiplier;
        this.timer = duration;
        
        playerController = GetComponent<PlayerController>();
        
        // Note: The PlayerController will need to check for this component
        // and apply the slow multiplier in its Move() method
        
        initialized = true;
        Debug.Log($"[SlowDebuff] Initialized: {duration}s at {multiplier}x speed");
    }

    public void RefreshDuration(float newDuration, float newMultiplier)
    {
        duration = newDuration;
        multiplier = newMultiplier;
        timer = newDuration;
        Debug.Log($"[SlowDebuff] Refreshed: {duration}s at {multiplier}x speed");
    }

    private void Update()
    {
        if (!initialized) return;
        
        timer -= Time.deltaTime;
        
        if (timer <= 0)
        {
            Debug.Log("[SlowDebuff] Expired, removing component");
            Destroy(this);
        }
    }

    /// <summary>
    /// Get the current slow multiplier (for PlayerController to use)
    /// </summary>
    public float GetMultiplier()
    {
        return multiplier;
    }

    /// <summary>
    /// Check if the debuff is active
    /// </summary>
    public bool IsActive => initialized && timer > 0;

    /// <summary>
    /// Time remaining on the debuff
    /// </summary>
    public float TimeRemaining => timer;
}