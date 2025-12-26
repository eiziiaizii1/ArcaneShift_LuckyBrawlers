using System.Collections;
using Unity.Netcode;
using UnityEngine;
using Unity.Cinemachine;

/// <summary>
/// PlayerController handles movement, aiming, and shooting.
/// Updated to integrate with SlimeController for form-aware combat.
/// 
/// Features:
/// - Form-aware projectiles (Fireball vs Gloop)
/// - Adaptive speed from SlimeController
/// - SCALED DAMAGE for Gloop based on slime size (smaller = weaker)
/// - Lucky Box event integration
/// - Slow debuff handling
/// </summary>
public class PlayerController : NetworkBehaviour
{
    public NetworkVariable<int> score = new NetworkVariable<int>(0);
    
    #region Inspector Fields

    [Header("Movement Settings")]
    [SerializeField] private float moveSpeed = 5f;
    [SerializeField] private float speedBoostMultiplier = 2f; 

    [Header("Combat Settings")]
    [SerializeField] private GameObject fireballPrefab; 
    [SerializeField] private Transform firePoint;
    [SerializeField] private float fireRate = 0.5f;
    [SerializeField] private float fireballBaseSpeed = 10f;
    [SerializeField] private int fireballBaseDamage = 25;
    private float nextFireTime = 0f;

    [Header("Camera Settings")]
    [SerializeField] private CinemachineCamera sceneCamera;
    [SerializeField] private Transform cameraFollowTarget;
    [SerializeField] private float cameraSearchTimeout = 5f;

    [Header("Aiming Settings")]
    [SerializeField] private Sprite arrowSprite; 
    [SerializeField] private float aimIndicatorRadius = 1.5f; 
    [SerializeField] private float arrowScale = 0.5f; 

    [Header("Form-Specific Colors")]
    [SerializeField] private Color wizardArrowColor = Color.yellow;
    [SerializeField] private Color slimeArrowColor = Color.green;

    [Header("Audio")]
    [SerializeField] private AudioClip fireballWhooshSound;
    [SerializeField] private AudioClip gloopWhooshSound;
    [Range(0f, 1f)]
    [SerializeField] private float fireballWhooshVolume = 1f;

    #endregion

    #region Private Fields

    private Rigidbody2D rb;
    private Vector2 moveInput;
    private GameObject aimArrowInstance; 
    private SpriteRenderer aimArrowRenderer;
    private static CinemachineCamera cachedSceneCamera;
    private SpriteRenderer playerSprite;
    private Canvas nameCanvas;
    
    private SlimeController slimeController;
    private ProceduralCharacterAnimator animator;
    private AudioSource audioSource;

    #endregion

    #region Unity Lifecycle

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        playerSprite = GetComponent<SpriteRenderer>() ?? GetComponentInChildren<SpriteRenderer>();
        nameCanvas = GetComponentInChildren<Canvas>();
        slimeController = GetComponent<SlimeController>();
        animator = GetComponent<ProceduralCharacterAnimator>();
        audioSource = GetComponent<AudioSource>();
        if (audioSource == null) audioSource = gameObject.AddComponent<AudioSource>();
    }

    public override void OnNetworkSpawn()
    {
        if (IsOwner)
        {
            StartCoroutine(SetupCameraFollow());

            if (IsServer)
            {
                SetVisibility(false);
                StartCoroutine(MoveHostToSpawnPoint());
            }
            else
            {
                CreateAimingArrow();
            }
        }
        else
        {
            this.enabled = false; 
        }
        
        if (slimeController != null)
        {
            slimeController.isSlimeForm.OnValueChanged += OnFormChanged;
        }
    }

    public override void OnNetworkDespawn()
    {
        if (!IsOwner) return;
        
        var cam = ResolveCamera();
        var target = cameraFollowTarget != null ? cameraFollowTarget : transform;
        if (cam != null && cam.Follow == target) cam.Follow = null;
        
        if (slimeController != null)
        {
            slimeController.isSlimeForm.OnValueChanged -= OnFormChanged;
        }
    }

    private void OnFormChanged(bool oldValue, bool newValue)
    {
        if (aimArrowRenderer != null)
        {
            aimArrowRenderer.color = newValue ? slimeArrowColor : wizardArrowColor;
        }
    }

    #endregion

    #region Camera Setup

    private IEnumerator SetupCameraFollow()
    {
        CinemachineCamera cam = ResolveCamera();
        float timer = 0f;
        while (cam == null && timer < cameraSearchTimeout)
        {
            yield return null;
            timer += Time.unscaledDeltaTime;
            cam = ResolveCamera();
        }
        if (cam != null)
        {
            var target = cameraFollowTarget != null ? cameraFollowTarget : transform;
            cam.Follow = target;
        }
    }

    private CinemachineCamera ResolveCamera()
    {
        if (sceneCamera != null) return sceneCamera;
        if (cachedSceneCamera != null) return cachedSceneCamera;
        cachedSceneCamera = FindFirstObjectByType<CinemachineCamera>(FindObjectsInactive.Include);
        return cachedSceneCamera;
    }

    #endregion

    #region Aiming Arrow

    private void CreateAimingArrow()
    {
        if (arrowSprite == null) return;
        
        aimArrowInstance = new GameObject("AimArrow_Local");
        aimArrowInstance.transform.localScale = Vector3.one * arrowScale;
        aimArrowRenderer = aimArrowInstance.AddComponent<SpriteRenderer>();
        aimArrowRenderer.sprite = arrowSprite;
        
        if (playerSprite != null) 
        {
            aimArrowRenderer.sortingLayerName = playerSprite.sortingLayerName;
            aimArrowRenderer.sortingOrder = playerSprite.sortingOrder + 1;
        }
        
        bool isSlime = slimeController != null && slimeController.isSlimeForm.Value;
        aimArrowRenderer.color = isSlime ? slimeArrowColor : wizardArrowColor;
    }

    public void SetArrowVisibility(bool isVisible)
    {
        if (aimArrowRenderer != null) 
            aimArrowRenderer.enabled = isVisible;
        
        if (isVisible && aimArrowInstance == null && IsOwner) 
            CreateAimingArrow();
    }

    private void UpdateAimingIndicator()
    {
        if (aimArrowInstance == null) 
        {
            if (playerSprite != null && playerSprite.enabled) 
                CreateAimingArrow();
            return;
        }
        
        Vector3 mousePos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
        mousePos.z = 0f;
        Vector2 direction = (mousePos - transform.position).normalized;
        Vector3 arrowPosition = transform.position + (Vector3)direction * aimIndicatorRadius;
        aimArrowInstance.transform.position = arrowPosition;
        float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg - 90f;
        aimArrowInstance.transform.rotation = Quaternion.Euler(0, 0, angle);
    }

    #endregion

    #region Update Loop

    private void Update()
    {
        if (!IsOwner) return;
        InputHandling();
    }

    private void FixedUpdate()
    {
        if (!IsOwner) return;
        Move();
    }

    private void InputHandling()
    {
        float moveX = Input.GetAxisRaw("Horizontal");
        float moveY = Input.GetAxisRaw("Vertical");

        // LUCKY BOX: REVERSE CONTROLS
        if (LuckyBox.AreControlsReversed())
        {
            moveX *= -1;
            moveY *= -1;
        }

        moveInput = new Vector2(moveX, moveY).normalized;
        UpdateAimingIndicator();

        // SHOOTING
        if (Input.GetButton("Fire1") && Time.time >= nextFireTime)
        {
            TryShoot();
        }
    }

    #endregion

    #region Movement

    private void Move()
    {
        float currentSpeed = CalculateCurrentSpeed();

        if (rb != null) 
            rb.linearVelocity = moveInput * currentSpeed;
        else 
            transform.position += (Vector3)moveInput * currentSpeed * Time.deltaTime;
    }

    private float CalculateCurrentSpeed()
    {
        float speed = moveSpeed;

        // 1. Lucky Box Speed Boost
        if (LuckyBox.Instance != null && LuckyBox.Instance.ActiveGlobalEvent.Value == ModifierType.SpeedBoost)
        {
            speed *= speedBoostMultiplier;
        }

        // 2. Slime Form Adaptive Scaling (smaller = faster)
        if (slimeController != null && slimeController.IsSlime)
        {
            speed *= slimeController.GetSpeedMultiplier();
        }

        // 3. Slow Debuff (from Gloop hits)
        SlowDebuff slowDebuff = GetComponent<SlowDebuff>();
        if (slowDebuff != null && slowDebuff.IsActive)
        {
            speed *= slowDebuff.GetMultiplier();
        }

        return speed;
    }

    #endregion

    #region Combat

    private void TryShoot()
    {
        if (aimArrowInstance == null) return;

        GameObject projectilePrefab = fireballPrefab;
        float projectileSpeed = fireballBaseSpeed;
        int projectileDamage = fireballBaseDamage;
        
        if (slimeController != null && slimeController.ShouldUseGloop())
        {
            var (gloopPrefab, gloopSpeed, gloopDamage) = slimeController.GetProjectileSettings(fireballPrefab, fireballBaseSpeed);
            projectilePrefab = gloopPrefab;
            projectileSpeed = gloopSpeed;
            projectileDamage = gloopDamage;
        }

        // Apply Lucky Box speed boost to projectiles too
        if (LuckyBox.Instance != null && LuckyBox.Instance.ActiveGlobalEvent.Value == ModifierType.SpeedBoost)
        {
            projectileSpeed *= speedBoostMultiplier;
        }

        Vector3 spawnPos = aimArrowInstance.transform.position;
        Quaternion spawnRot = aimArrowInstance.transform.rotation;
        
        ShootServerRpc(spawnPos, spawnRot, projectileSpeed, projectileDamage);
        nextFireTime = Time.time + fireRate;
    }

    [ServerRpc]
    private void ShootServerRpc(Vector3 position, Quaternion rotation, float speed, int damage, ServerRpcParams rpcParams = default)
    {
        ulong shooterId = rpcParams.Receive.SenderClientId;
        
        GameObject prefabToUse = fireballPrefab;
        SlimeController shooterSlime = null;
        int finalDamage = damage;
        
        if (NetworkManager.Singleton.ConnectedClients.TryGetValue(shooterId, out NetworkClient client))
        {
            shooterSlime = client.PlayerObject?.GetComponent<SlimeController>();
            if (shooterSlime != null && shooterSlime.ShouldUseGloop())
            {
                var (gloopPrefab, _, scaledDamage) = shooterSlime.GetProjectileSettings(fireballPrefab, fireballBaseSpeed);
                prefabToUse = gloopPrefab;
                finalDamage = scaledDamage;
            }
        }

        GameObject projectile = Instantiate(prefabToUse, position, rotation);
        
        var fireballLogic = projectile.GetComponent<FireballLogic>();
        if (fireballLogic != null)
        {
            fireballLogic.SetSpeed(speed);
        }
        
        var gloopLogic = projectile.GetComponent<GloopLogic>();
        if (gloopLogic != null)
        {
            gloopLogic.SetSpeed(speed);
            gloopLogic.SetDamage(finalDamage);
            
            Debug.Log($"[PlayerController] Spawned Gloop with {finalDamage} damage (Scale: {shooterSlime?.CurrentScale:F2})");
        }

        projectile.GetComponent<NetworkObject>().SpawnWithOwnership(shooterId);

        bool isGloop = prefabToUse != fireballPrefab;
        PlayAttackWhooshClientRpc(isGloop);
    }

    #endregion

    [ClientRpc]
    private void PlayAttackWhooshClientRpc(bool isGloop)
    {
        if (audioSource == null) return;
        AudioClip clip = isGloop ? gloopWhooshSound : fireballWhooshSound;
        if (clip == null) return;
        audioSource.PlayOneShot(clip, fireballWhooshVolume);
    }

    #region Visibility & Spawn

    private void SetVisibility(bool isVisible)
    {
        if (playerSprite != null) playerSprite.enabled = isVisible;
        if (nameCanvas != null) nameCanvas.enabled = isVisible;
        if (aimArrowRenderer != null) aimArrowRenderer.enabled = isVisible;
    }

    private IEnumerator MoveHostToSpawnPoint()
    {
        float timer = 0f;
        GameObject[] points = null;
        
        while (timer < 3f) 
        {
            points = GameObject.FindGameObjectsWithTag("Respawn");
            if (points.Length > 0) break;
            yield return new WaitForSeconds(0.1f);
            timer += 0.1f;
        }
        
        if (points != null && points.Length > 0) 
        {
            var target = points[Random.Range(0, points.Length)].transform.position;
            if (rb != null) rb.position = target;
            else transform.position = target;
        }
        
        SetVisibility(true);
        CreateAimingArrow();
    }

    #endregion

    #region Cleanup

    public override void OnDestroy() 
    { 
        base.OnDestroy();
        if (aimArrowInstance != null) 
            Destroy(aimArrowInstance); 
    }

    private void OnDisable() 
    { 
        if (aimArrowRenderer != null) 
            aimArrowRenderer.enabled = false; 
    }
    
    private void OnEnable() 
    { 
        if (aimArrowRenderer != null && IsOwner) 
            aimArrowRenderer.enabled = true; 
    }

    #endregion
}
