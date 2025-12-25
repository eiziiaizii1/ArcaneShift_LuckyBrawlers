using Unity.Netcode;
using UnityEngine;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// LaserBeamUltimate handles the Ultimate ability for both Wizard and Slime forms.
/// Fires an instant-kill laser beam in the aim direction.
/// 
/// Features:
/// - Instant kill on any enemy hit
/// - Visual laser beam effect
/// - Network synced across all clients
/// - Works for both Wizard and Slime forms
/// - Proper cooldown prevents spam firing
/// 
/// Attach to Player prefab.
/// </summary>
public class LaserBeamUltimate : NetworkBehaviour
{
    #region Network Variables

    /// <summary>
    /// Ultimate meter (0-100). When full, player can trigger laser beam.
    /// </summary>
    public NetworkVariable<float> ultimateMeter = new NetworkVariable<float>(
        0f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    /// <summary>
    /// Is the ultimate currently being fired?
    /// </summary>
    public NetworkVariable<bool> isFiringLaser = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    /// <summary>
    /// Is the ultimate on cooldown? (Synced so UI can show it)
    /// </summary>
    public NetworkVariable<bool> isOnCooldown = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    #endregion

    #region Inspector Fields

    [Header("Ultimate Settings")]
    [Tooltip("Ultimate charge per damage dealt")]
    [SerializeField] private float ultimateChargePerDamage = 2f;
    
    [Tooltip("Maximum ultimate charge")]
    [SerializeField] private float maxUltimateCharge = 100f;
    
    [Tooltip("Cooldown after using ultimate (seconds)")]
    [SerializeField] private float ultimateCooldown = 2f;
    
    [Tooltip("Key to activate ultimate")]
    [SerializeField] private KeyCode ultimateKey = KeyCode.Q;

    [Header("Laser Beam Settings")]
    [Tooltip("Maximum range of the laser beam")]
    [SerializeField] private float laserRange = 50f;
    
    [Tooltip("Width of the laser beam")]
    [SerializeField] private float laserWidth = 0.5f;
    
    [Tooltip("Duration the laser stays visible")]
    [SerializeField] private float laserDuration = 0.3f;
    
    [Tooltip("Layers the laser can hit")]
    [SerializeField] private LayerMask hitLayers;

    [Header("Visual Settings")]
    [Tooltip("Color of the laser beam for Wizard")]
    [SerializeField] private Color wizardLaserColor = new Color(0.5f, 0.8f, 1f, 1f);
    
    [Tooltip("Color of the laser beam for Slime")]
    [SerializeField] private Color slimeLaserColor = new Color(0.3f, 1f, 0.3f, 1f);
    
    [Tooltip("Laser beam line renderer prefab (optional)")]
    [SerializeField] private GameObject laserPrefab;

    [Header("Audio")]
    [SerializeField] private AudioClip laserFireSound;
    [SerializeField] private AudioClip ultimateReadySound;

    [Header("References")]
    [Tooltip("Where the laser fires FROM - assign your FirePoint here")]
    [SerializeField] private Transform firePoint;

    #endregion

    #region Private Fields

    private bool wasReady = false;
    private AudioSource audioSource;
    private SlimeController slimeController;
    private LineRenderer laserLineRenderer;
    private GameObject currentLaserInstance;
    private GameObject laserRendererObject;
    
    // Local cooldown tracking (client-side for responsive input)
    private bool localCooldownActive = false;

    #endregion

    #region Unity Lifecycle

    private void Awake()
    {
        audioSource = GetComponent<AudioSource>();
        if (audioSource == null)
            audioSource = gameObject.AddComponent<AudioSource>();
        
        slimeController = GetComponent<SlimeController>();
        
        // Create a line renderer for the laser if no prefab assigned
        if (laserPrefab == null)
        {
            CreateDefaultLaserRenderer();
        }
    }

    private void CreateDefaultLaserRenderer()
    {
        laserRendererObject = new GameObject("LaserBeam");
        laserRendererObject.transform.SetParent(transform);
        laserRendererObject.transform.localPosition = Vector3.zero;
        
        laserLineRenderer = laserRendererObject.AddComponent<LineRenderer>();
        laserLineRenderer.startWidth = laserWidth;
        laserLineRenderer.endWidth = laserWidth * 0.5f;
        laserLineRenderer.positionCount = 2;
        laserLineRenderer.useWorldSpace = true;
        laserLineRenderer.enabled = false;
        
        // Create a simple material
        laserLineRenderer.material = new Material(Shader.Find("Sprites/Default"));
        laserLineRenderer.sortingOrder = 100;
    }

    public override void OnNetworkSpawn()
    {
        ultimateMeter.OnValueChanged += OnUltimateChanged;
        isFiringLaser.OnValueChanged += OnFiringStateChanged;
        isOnCooldown.OnValueChanged += OnCooldownChanged;
        
        // Auto-find firePoint if not assigned
        if (firePoint == null)
        {
            Transform found = transform.Find("FirePoint");
            if (found != null)
            {
                firePoint = found;
                Debug.Log("[LaserBeamUltimate] Auto-found FirePoint child");
            }
        }
        
        Debug.Log($"[LaserBeamUltimate] Spawned for player {OwnerClientId}. FirePoint assigned: {firePoint != null}");
    }

    public override void OnNetworkDespawn()
    {
        ultimateMeter.OnValueChanged -= OnUltimateChanged;
        isFiringLaser.OnValueChanged -= OnFiringStateChanged;
        isOnCooldown.OnValueChanged -= OnCooldownChanged;
    }

    private void Update()
    {
        if (!IsOwner) return;

        // Check for ultimate input
        if (Input.GetKeyDown(ultimateKey))
        {
            TryUseUltimate();
        }
    }

    #endregion

    #region Ultimate Charge

    /// <summary>
    /// Called when dealing damage to charge the ultimate.
    /// Server-only.
    /// </summary>
    public void AddUltimateCharge(int damageDealt)
    {
        if (!IsServer) return;

        // Don't charge while on cooldown
        if (isOnCooldown.Value) return;

        float chargeGain = damageDealt * ultimateChargePerDamage;
        float newCharge = Mathf.Min(maxUltimateCharge, ultimateMeter.Value + chargeGain);
        ultimateMeter.Value = newCharge;
        
        Debug.Log($"[LaserBeamUltimate] Player {OwnerClientId} gained {chargeGain} charge. Total: {newCharge}%");
    }

    /// <summary>
    /// Directly set ultimate charge (for testing or special pickups)
    /// </summary>
    public void SetUltimateCharge(float charge)
    {
        if (!IsServer) return;
        ultimateMeter.Value = Mathf.Clamp(charge, 0f, maxUltimateCharge);
    }

    private void OnUltimateChanged(float oldValue, float newValue)
    {
        // Check if ultimate just became ready
        bool isNowReady = newValue >= maxUltimateCharge && !isOnCooldown.Value;
        
        if (isNowReady && !wasReady && IsOwner)
        {
            // Play ready sound
            if (ultimateReadySound != null && audioSource != null)
            {
                audioSource.PlayOneShot(ultimateReadySound);
            }
            Debug.Log("[LaserBeamUltimate] Ultimate is READY! Press Q to fire!");
        }
        
        wasReady = isNowReady;
    }

    private void OnCooldownChanged(bool oldValue, bool newValue)
    {
        localCooldownActive = newValue;
        
        if (!newValue && IsOwner)
        {
            // Cooldown ended
            Debug.Log("[LaserBeamUltimate] Cooldown ended. Ultimate ready to charge again.");
        }
    }

    #endregion

    #region Ultimate Activation

    private void TryUseUltimate()
    {
        if (!IsOwner) return;
        
        // Check all conditions
        if (localCooldownActive)
        {
            Debug.Log("[LaserBeamUltimate] Ultimate on cooldown!");
            return;
        }
        
        if (isFiringLaser.Value)
        {
            Debug.Log("[LaserBeamUltimate] Already firing!");
            return;
        }
        
        if (ultimateMeter.Value < maxUltimateCharge)
        {
            Debug.Log($"[LaserBeamUltimate] Ultimate not charged. Current: {ultimateMeter.Value:F0}%");
            return;
        }
        
        // Set local cooldown immediately to prevent double-tap
        localCooldownActive = true;
        
        // Get aim direction from mouse
        Vector3 mousePos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
        mousePos.z = 0f;
        
        // Calculate start position - USE FIREPOINT IF ASSIGNED
        Vector3 startPos = (firePoint != null) ? firePoint.position : transform.position;
        
        // Calculate direction from start position to mouse
        Vector2 aimDirection = ((Vector2)(mousePos - startPos)).normalized;
        
        // Send the ACTUAL start position to the server
        FireLaserServerRpc(startPos, aimDirection);
        
        Debug.Log($"[LaserBeamUltimate] Requesting laser fire from {startPos} in direction {aimDirection}");
    }

    [ServerRpc]
    private void FireLaserServerRpc(Vector3 clientStartPos, Vector2 direction, ServerRpcParams rpcParams = default)
    {
        // Server-side validation
        if (ultimateMeter.Value < maxUltimateCharge)
        {
            Debug.Log("[LaserBeamUltimate] Server rejected: not enough charge");
            return;
        }
        
        if (isFiringLaser.Value)
        {
            Debug.Log("[LaserBeamUltimate] Server rejected: already firing");
            return;
        }
        
        if (isOnCooldown.Value)
        {
            Debug.Log("[LaserBeamUltimate] Server rejected: on cooldown");
            return;
        }
        
        ulong shooterId = rpcParams.Receive.SenderClientId;
        
        // === IMMEDIATELY SET ALL BLOCKING FLAGS ===
        ultimateMeter.Value = 0f;      // Reset meter FIRST
        isFiringLaser.Value = true;    // Mark as firing
        isOnCooldown.Value = true;     // Start cooldown
        
        // Use the client-provided start position
        Vector3 startPos = clientStartPos;
        Vector3 endPos = startPos + (Vector3)(direction * laserRange);
        
        // Perform raycast to find hits
        RaycastHit2D[] hits = Physics2D.RaycastAll(startPos, direction, laserRange, hitLayers);
        
        // Sort by distance
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        
        // Track killed players to avoid double-kills
        HashSet<ulong> killedPlayers = new HashSet<ulong>();
        
        foreach (var hit in hits)
        {
            // Skip self
            NetworkObject netObj = hit.collider.GetComponent<NetworkObject>();
            if (netObj != null)
            {
                if (netObj.OwnerClientId == shooterId) continue;
                if (killedPlayers.Contains(netObj.OwnerClientId)) continue;
            }
            
            // Check for Health component
            Health health = hit.collider.GetComponent<Health>();
            if (health != null && netObj != null)
            {
                // Instant kill - deal massive damage
                int instantKillDamage = 9999;
                health.TakeDamage(instantKillDamage, shooterId);
                killedPlayers.Add(netObj.OwnerClientId);
                
                Debug.Log($"[LaserBeamUltimate] Player {shooterId} INSTANT KILLED player {netObj.OwnerClientId}!");
                
                // Update end position to hit point for visual
                endPos = hit.point;
                break; // Stop at first player hit
            }
            
            // Check for walls
            if (hit.collider.CompareTag("Wall") || hit.collider.CompareTag("Obstacle"))
            {
                endPos = hit.point;
                break;
            }
        }
        
        // Determine laser color based on form
        bool isSlime = slimeController != null && slimeController.IsSlime;
        Color laserColor = isSlime ? slimeLaserColor : wizardLaserColor;
        
        // Trigger visual effect on all clients
        FireLaserVisualClientRpc(startPos, endPos, laserColor);
        
        // Start cooldown coroutine
        StartCoroutine(LaserCooldownCoroutine());
        
        Debug.Log($"[LaserBeamUltimate] Player {shooterId} fired LASER BEAM!");
    }

    private IEnumerator LaserCooldownCoroutine()
    {
        // Wait for laser visual to finish
        yield return new WaitForSeconds(laserDuration);
        
        // Laser visual done
        isFiringLaser.Value = false;
        
        // Wait for remaining cooldown
        float remainingCooldown = ultimateCooldown - laserDuration;
        if (remainingCooldown > 0)
        {
            yield return new WaitForSeconds(remainingCooldown);
        }
        
        // Cooldown complete - can charge and fire again
        isOnCooldown.Value = false;
        
        Debug.Log("[LaserBeamUltimate] Cooldown complete!");
    }

    #endregion

    #region Visual Effects

    private void OnFiringStateChanged(bool oldValue, bool newValue)
    {
        if (!newValue && laserLineRenderer != null)
        {
            laserLineRenderer.enabled = false;
        }
    }

    [ClientRpc]
    private void FireLaserVisualClientRpc(Vector3 startPos, Vector3 endPos, Color laserColor)
    {
        StartCoroutine(ShowLaserBeamCoroutine(startPos, endPos, laserColor));
    }

    private IEnumerator ShowLaserBeamCoroutine(Vector3 startPos, Vector3 endPos, Color laserColor)
    {
        LineRenderer activeRenderer = null;
        
        // Use prefab if available, otherwise use line renderer
        if (laserPrefab != null)
        {
            currentLaserInstance = Instantiate(laserPrefab);
            activeRenderer = currentLaserInstance.GetComponent<LineRenderer>();
        }
        else if (laserLineRenderer != null)
        {
            activeRenderer = laserLineRenderer;
            laserLineRenderer.enabled = true;
        }
        
        if (activeRenderer != null)
        {
            activeRenderer.SetPosition(0, startPos);
            activeRenderer.SetPosition(1, endPos);
            activeRenderer.startColor = laserColor;
            activeRenderer.endColor = laserColor;
            activeRenderer.startWidth = laserWidth;
            activeRenderer.endWidth = laserWidth * 0.5f;
        }
        
        // Play sound
        if (laserFireSound != null && audioSource != null)
        {
            audioSource.PlayOneShot(laserFireSound);
        }
        
        // Animate the laser (flash effect)
        float elapsed = 0f;
        while (elapsed < laserDuration)
        {
            elapsed += Time.deltaTime;
            float alpha = 1f - (elapsed / laserDuration);
            
            if (activeRenderer != null)
            {
                Color startCol = laserColor;
                startCol.a = alpha;
                Color endCol = laserColor;
                endCol.a = alpha * 0.3f;
                
                activeRenderer.startColor = startCol;
                activeRenderer.endColor = endCol;
                activeRenderer.startWidth = laserWidth * alpha;
                activeRenderer.endWidth = laserWidth * 0.5f * alpha;
            }
            
            yield return null;
        }
        
        // Cleanup
        if (laserPrefab != null && currentLaserInstance != null)
        {
            Destroy(currentLaserInstance);
        }
        else if (laserLineRenderer != null)
        {
            laserLineRenderer.enabled = false;
            laserLineRenderer.startWidth = laserWidth;
            laserLineRenderer.endWidth = laserWidth * 0.5f;
        }
    }

    #endregion

    #region Public API

    /// <summary>
    /// Check if ultimate is ready to use
    /// </summary>
    public bool IsUltimateReady => ultimateMeter.Value >= maxUltimateCharge && !isOnCooldown.Value && !isFiringLaser.Value;

    /// <summary>
    /// Get current charge percentage (0-100)
    /// </summary>
    public float UltimateCharge => ultimateMeter.Value;

    /// <summary>
    /// Get max charge value
    /// </summary>
    public float MaxCharge => maxUltimateCharge;

    /// <summary>
    /// Check if currently firing
    /// </summary>
    public bool IsFiring => isFiringLaser.Value;

    /// <summary>
    /// Check if on cooldown
    /// </summary>
    public bool IsOnCooldown => isOnCooldown.Value;

    /// <summary>
    /// Reset ultimate state (called on respawn if desired)
    /// </summary>
    public void ResetUltimate()
    {
        if (!IsServer) return;
        // Ultimate persists through death per GDD
    }

    #endregion

    #region Cleanup

    public override void OnDestroy()
    {
        base.OnDestroy();
        
        if (laserRendererObject != null)
        {
            Destroy(laserRendererObject);
        }
    }

    #endregion

    #region Debug

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Vector3 start = firePoint != null ? firePoint.position : transform.position;
        
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(start, 0.2f);
        
        Gizmos.color = new Color(1, 0, 0, 0.3f);
        Gizmos.DrawLine(start, start + Vector3.right * laserRange);
        Gizmos.DrawLine(start, start + Vector3.up * laserRange);
        Gizmos.DrawLine(start, start + Vector3.left * laserRange);
        Gizmos.DrawLine(start, start + Vector3.down * laserRange);
        
        UnityEditor.Handles.Label(start + Vector3.up * 0.5f, "Laser Origin");
    }
#endif

    #endregion
}