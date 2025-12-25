using Unity.Netcode;
using UnityEngine;
using System.Collections;

/// <summary>
/// SlimeController handles the Slime form's unique mechanics:
/// - Adaptive Scaling: As damage is taken, the slime becomes smaller but faster
/// - Gloop Projectile: Different projectile type when in slime form
/// - Ultimate Ability: Laser Beam that instantly kills enemies
/// 
/// Attach to Player prefab alongside PlayerController.
/// </summary>
public class SlimeController : NetworkBehaviour
{
    #region Network Variables
    
    /// <summary>
    /// Current scale of the slime (1.0 = full size, 0.3 = minimum)
    /// Decreases as damage is taken, increases speed
    /// </summary>
    public NetworkVariable<float> currentScale = new NetworkVariable<float>(
        1.0f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    /// <summary>
    /// Ultimate meter (0-100). When full, player can trigger Laser Beam.
    /// Builds up when dealing damage to other players.
    /// </summary>
    public NetworkVariable<float> ultimateMeter = new NetworkVariable<float>(
        0f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    /// <summary>
    /// Whether the player is currently in slime form
    /// </summary>
    public NetworkVariable<bool> isSlimeForm = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    /// <summary>
    /// Whether the ultimate is currently being fired
    /// </summary>
    public NetworkVariable<bool> isFiringUltimate = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    #endregion

    #region Inspector Fields

    [Header("Adaptive Scaling Settings")]
    [SerializeField] private float minScale = 0.3f;
    [SerializeField] private float maxScale = 1.0f;
    [SerializeField] private float scalePerDamage = 0.007f;
    [SerializeField] private float maxSpeedMultiplier = 2.0f;
    [SerializeField] private float minSpeedMultiplier = 1.0f;

    [Header("Ultimate Settings")]
    [SerializeField] private float ultimateChargePerDamage = 2f;
    [SerializeField] private float maxUltimateCharge = 100f;
    [SerializeField] private KeyCode ultimateKey = KeyCode.Q;
    
    [Header("Laser Beam Settings")]
    [SerializeField] private float laserRange = 20f;
    [SerializeField] private float laserDuration = 0.5f;
    [SerializeField] private float laserWidth = 0.3f;
    [SerializeField] private Color laserColor = Color.red;
    [SerializeField] private Color laserCoreColor = Color.white;

    [Header("Gloop Projectile")]
    [SerializeField] private GameObject gloopPrefab;
    [SerializeField] private float gloopSpeed = 7f;
    [SerializeField] private int gloopDamage = 20;

    [Header("Visual Effects")]
    [SerializeField] private GameObject transformVFX;
    [SerializeField] private AudioClip transformSound;
    [SerializeField] private AudioClip laserFireSound;

    [Header("References")]
    [SerializeField] private Transform visualTransform;
    [SerializeField] private Transform firePoint;
    [SerializeField] private PlayerController playerController;
    [SerializeField] private ProceduralCharacterAnimator animator;

    #endregion

    #region Private Fields

    private AudioSource audioSource;
    private bool ultimateReady = true;
    private LineRenderer laserLineRenderer;
    private LineRenderer laserCoreRenderer;
    private GameObject laserObject;

    #endregion

    #region Unity Lifecycle

    private void Awake()
    {
        if (playerController == null)
            playerController = GetComponent<PlayerController>();
        
        if (animator == null)
            animator = GetComponent<ProceduralCharacterAnimator>();
        
        audioSource = GetComponent<AudioSource>();
        if (audioSource == null)
            audioSource = gameObject.AddComponent<AudioSource>();
        
        CreateLaserBeam();
    }

    private void CreateLaserBeam()
    {
        laserObject = new GameObject("LaserBeam");
        laserObject.transform.SetParent(transform);
        laserObject.transform.localPosition = Vector3.zero;
        
        // Main laser (outer glow)
        laserLineRenderer = laserObject.AddComponent<LineRenderer>();
        laserLineRenderer.positionCount = 2;
        laserLineRenderer.startWidth = laserWidth;
        laserLineRenderer.endWidth = laserWidth * 0.5f;
        laserLineRenderer.material = new Material(Shader.Find("Sprites/Default"));
        laserLineRenderer.startColor = laserColor;
        laserLineRenderer.endColor = new Color(laserColor.r, laserColor.g, laserColor.b, 0.3f);
        laserLineRenderer.sortingOrder = 100;
        laserLineRenderer.enabled = false;
        
        // Core laser (bright center)
        GameObject coreObject = new GameObject("LaserCore");
        coreObject.transform.SetParent(laserObject.transform);
        coreObject.transform.localPosition = Vector3.zero;
        
        laserCoreRenderer = coreObject.AddComponent<LineRenderer>();
        laserCoreRenderer.positionCount = 2;
        laserCoreRenderer.startWidth = laserWidth * 0.3f;
        laserCoreRenderer.endWidth = laserWidth * 0.15f;
        laserCoreRenderer.material = new Material(Shader.Find("Sprites/Default"));
        laserCoreRenderer.startColor = laserCoreColor;
        laserCoreRenderer.endColor = laserCoreColor;
        laserCoreRenderer.sortingOrder = 101;
        laserCoreRenderer.enabled = false;
    }

    public override void OnNetworkSpawn()
    {
        currentScale.OnValueChanged += OnScaleChanged;
        isSlimeForm.OnValueChanged += OnFormChanged;
        ultimateMeter.OnValueChanged += OnUltimateChanged;
        isFiringUltimate.OnValueChanged += OnFiringStateChanged;

        UpdateVisualScale(currentScale.Value);
        
        if (animator != null && IsServer)
        {
            animator.isSlimeForm.Value = isSlimeForm.Value;
        }
    }

    public override void OnNetworkDespawn()
    {
        currentScale.OnValueChanged -= OnScaleChanged;
        isSlimeForm.OnValueChanged -= OnFormChanged;
        ultimateMeter.OnValueChanged -= OnUltimateChanged;
        isFiringUltimate.OnValueChanged -= OnFiringStateChanged;
    }

    private void Update()
    {
        if (!IsOwner) return;

        // Check for ultimate input (works in both Wizard and Slime form)
        if (Input.GetKeyDown(ultimateKey))
        {
            TryUseUltimate();
        }
    }

    #endregion

    #region Form Management

    public void TransformToSlime()
    {
        if (!IsServer) return;
        
        isSlimeForm.Value = true;
        currentScale.Value = maxScale;
        
        if (animator != null)
            animator.SetSlimeForm(true);
        
        PlayTransformEffectClientRpc();
    }

    public void TransformToWizard()
    {
        if (!IsServer) return;
        
        isSlimeForm.Value = false;
        currentScale.Value = maxScale;
        
        if (animator != null)
            animator.SetSlimeForm(false);
        
        PlayTransformEffectClientRpc();
    }

    public void ToggleForm()
    {
        if (!IsServer) return;
        
        if (isSlimeForm.Value)
            TransformToWizard();
        else
            TransformToSlime();
    }

    private void OnFormChanged(bool oldValue, bool newValue)
    {
        UpdateLeaderboardState();
    }

    [ClientRpc]
    private void PlayTransformEffectClientRpc()
    {
        if (transformVFX != null)
        {
            Instantiate(transformVFX, transform.position, Quaternion.identity);
        }
        
        if (transformSound != null && audioSource != null)
        {
            audioSource.PlayOneShot(transformSound);
        }
    }

    #endregion

    #region Adaptive Scaling

    public void OnDamageTaken(int damageAmount)
    {
        if (!IsServer || !isSlimeForm.Value) return;

        float scaleReduction = damageAmount * scalePerDamage;
        float newScale = Mathf.Max(minScale, currentScale.Value - scaleReduction);
        currentScale.Value = newScale;
    }

    public void OnDamageDealt(int damageAmount)
    {
        if (!IsServer) return;

        float chargeGain = damageAmount * ultimateChargePerDamage;
        ultimateMeter.Value = Mathf.Min(maxUltimateCharge, ultimateMeter.Value + chargeGain);
    }

    private void OnScaleChanged(float oldScale, float newScale)
    {
        UpdateVisualScale(newScale);
        UpdateLeaderboardState();
    }

    private void UpdateVisualScale(float scale)
    {
        if (visualTransform != null)
        {
            visualTransform.localScale = Vector3.one * scale;
        }
        else
        {
            foreach (Transform child in transform)
            {
                if (child.GetComponent<Canvas>() == null)
                {
                    child.localScale = Vector3.one * scale;
                }
            }
        }
    }

    public float GetSpeedMultiplier()
    {
        if (!isSlimeForm.Value) return 1f;
        
        float scaleNormalized = (currentScale.Value - minScale) / (maxScale - minScale);
        float speedMultiplier = Mathf.Lerp(maxSpeedMultiplier, minSpeedMultiplier, scaleNormalized);
        
        return speedMultiplier;
    }

    #endregion

    #region Ultimate Ability - Laser Beam

    private void TryUseUltimate()
    {
        if (!IsOwner) return;
        
        if (ultimateMeter.Value >= maxUltimateCharge && ultimateReady)
        {
            // Get aim direction from mouse
            Vector3 mousePos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
            mousePos.z = 0f;
            Vector2 aimDirection = ((Vector2)(mousePos - transform.position)).normalized;
            
            UseUltimateServerRpc(aimDirection);
        }
    }

    [ServerRpc]
    private void UseUltimateServerRpc(Vector2 aimDirection)
    {
        if (ultimateMeter.Value < maxUltimateCharge || !ultimateReady) return;
        
        // Reset meter
        ultimateMeter.Value = 0f;
        isFiringUltimate.Value = true;
        
        // Perform laser raycast
        Vector2 startPos = firePoint != null ? (Vector2)firePoint.position : (Vector2)transform.position;
        Vector2 endPos = startPos + aimDirection * laserRange;
        
        // Get all hits along the laser path
        RaycastHit2D[] hits = Physics2D.RaycastAll(startPos, aimDirection, laserRange);
        
        foreach (var hit in hits)
        {
            if (hit.collider == null) continue;
            
            // Skip self
            NetworkObject netObj = hit.collider.GetComponent<NetworkObject>();
            if (netObj != null && netObj.OwnerClientId == OwnerClientId) continue;
            
            // Instant kill any player hit
            Health health = hit.collider.GetComponent<Health>();
            if (health != null)
            {
                health.TakeDamage(9999, OwnerClientId);
            }
            
            // If we hit a wall, stop the laser there
            if (hit.collider.CompareTag("Wall") || hit.collider.CompareTag("Obstacle"))
            {
                endPos = hit.point;
                break;
            }
        }
        
        // Fire laser visual on all clients
        FireLaserClientRpc(startPos, endPos);
        
        // Start cooldown
        StartCoroutine(LaserCooldownCoroutine());
    }

    private IEnumerator LaserCooldownCoroutine()
    {
        ultimateReady = false;
        
        yield return new WaitForSeconds(laserDuration);
        
        isFiringUltimate.Value = false;
        
        yield return new WaitForSeconds(0.5f);
        
        ultimateReady = true;
    }

    [ClientRpc]
    private void FireLaserClientRpc(Vector2 startPos, Vector2 endPos)
    {
        StartCoroutine(LaserVisualCoroutine(startPos, endPos));
    }

    private IEnumerator LaserVisualCoroutine(Vector2 startPos, Vector2 endPos)
    {
        if (laserLineRenderer == null) yield break;
        
        // Play sound
        if (laserFireSound != null && audioSource != null)
        {
            audioSource.PlayOneShot(laserFireSound);
        }
        
        // Enable and set positions
        laserLineRenderer.enabled = true;
        laserLineRenderer.SetPosition(0, startPos);
        laserLineRenderer.SetPosition(1, endPos);
        
        if (laserCoreRenderer != null)
        {
            laserCoreRenderer.enabled = true;
            laserCoreRenderer.SetPosition(0, startPos);
            laserCoreRenderer.SetPosition(1, endPos);
        }
        
        // Animate the laser (flash effect)
        float elapsed = 0f;
        float flashSpeed = 20f;
        
        while (elapsed < laserDuration)
        {
            elapsed += Time.deltaTime;
            
            // Pulsing width
            float pulse = 1f + Mathf.Sin(elapsed * flashSpeed) * 0.3f;
            laserLineRenderer.startWidth = laserWidth * pulse;
            laserLineRenderer.endWidth = laserWidth * 0.5f * pulse;
            
            if (laserCoreRenderer != null)
            {
                laserCoreRenderer.startWidth = laserWidth * 0.3f * pulse;
                laserCoreRenderer.endWidth = laserWidth * 0.15f * pulse;
            }
            
            // Fade out near the end
            float alpha = 1f - (elapsed / laserDuration);
            Color startCol = new Color(laserColor.r, laserColor.g, laserColor.b, alpha);
            Color endCol = new Color(laserColor.r, laserColor.g, laserColor.b, alpha * 0.3f);
            laserLineRenderer.startColor = startCol;
            laserLineRenderer.endColor = endCol;
            
            yield return null;
        }
        
        laserLineRenderer.enabled = false;
        if (laserCoreRenderer != null)
            laserCoreRenderer.enabled = false;
    }

    private void OnFiringStateChanged(bool oldValue, bool newValue)
    {
        if (!newValue)
        {
            if (laserLineRenderer != null)
                laserLineRenderer.enabled = false;
            if (laserCoreRenderer != null)
                laserCoreRenderer.enabled = false;
        }
    }

    private void OnUltimateChanged(float oldValue, float newValue)
    {
        // UI updates handled by UltimateMeterUI
    }

    #endregion

    #region Gloop Projectile

    public (GameObject prefab, float speed, int damage) GetProjectileSettings(GameObject defaultFireball, float defaultSpeed)
    {
        if (isSlimeForm.Value && gloopPrefab != null)
        {
            return (gloopPrefab, gloopSpeed, gloopDamage);
        }
        
        return (defaultFireball, defaultSpeed, 25);
    }

    public bool ShouldUseGloop()
    {
        return isSlimeForm.Value && gloopPrefab != null;
    }

    #endregion

    #region Leaderboard Integration

    private void UpdateLeaderboardState()
    {
        if (!IsServer) return;
        
        LeaderboardManager lb = Object.FindFirstObjectByType<LeaderboardManager>();
        if (lb != null)
        {
            for (int i = 0; i < lb.playerRoster.Count; i++)
            {
                if (lb.playerRoster[i].ClientId == OwnerClientId)
                {
                    var data = lb.playerRoster[i];
                    data.IsSlimeForm = isSlimeForm.Value;
                    data.CurrentScale = currentScale.Value;
                    lb.playerRoster[i] = data;
                    break;
                }
            }
        }
    }

    #endregion

    #region Public API

    public void ResetState()
    {
        if (!IsServer) return;
        
        currentScale.Value = maxScale;
    }

    public bool IsSlime => isSlimeForm.Value;
    public float CurrentScale => currentScale.Value;
    public float UltimateCharge => ultimateMeter.Value;
    public float MaxUltimateCharge => maxUltimateCharge;
    public bool IsUltimateReady => ultimateMeter.Value >= maxUltimateCharge && ultimateReady;
    public bool IsFiringLaser => isFiringUltimate.Value;

    #endregion

    #region Cleanup

    public override void OnDestroy()
    {
        base.OnDestroy();
        
        if (laserObject != null)
            Destroy(laserObject);
    }

    #endregion

    #region Debug

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Vector3 start = firePoint != null ? firePoint.position : transform.position;
        Gizmos.DrawLine(start, start + transform.up * laserRange);
    }
#endif

    #endregion
}