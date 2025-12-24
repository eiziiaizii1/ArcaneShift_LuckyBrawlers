using System.Collections;
using Unity.Netcode;
using UnityEngine;
using Unity.Cinemachine;

public class PlayerController : NetworkBehaviour
{
    public NetworkVariable<int> score = new NetworkVariable<int>(0);
    
    [Header("Movement Settings")]
    [SerializeField] private float moveSpeed = 5f;
    [SerializeField] private float speedBoostMultiplier = 2f; 

    [Header("Combat Settings")]
    [SerializeField] private GameObject fireballPrefab; 
    [SerializeField] private Transform firePoint;
    [SerializeField] private float fireRate = 0.5f; // Saniyede 2 mermi
    [SerializeField] private float fireballBaseSpeed = 10f; 
    private float nextFireTime = 0f;

    [Header("Camera Settings")]
    [SerializeField] private CinemachineCamera sceneCamera;
    [SerializeField] private Transform cameraFollowTarget;
    [SerializeField] private float cameraSearchTimeout = 5f;

    [Header("Aiming Settings")]
    [SerializeField] private Sprite arrowSprite; 
    [SerializeField] private float aimIndicatorRadius = 1.5f; 
    [SerializeField] private float arrowScale = 0.5f; 

    private Rigidbody2D rb;
    private Vector2 moveInput;
    private GameObject aimArrowInstance; 
    private SpriteRenderer aimArrowRenderer;
    private static CinemachineCamera cachedSceneCamera;
    private SpriteRenderer playerSprite;
    private Canvas nameCanvas;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        playerSprite = GetComponent<SpriteRenderer>() ?? GetComponentInChildren<SpriteRenderer>();
        nameCanvas = GetComponentInChildren<Canvas>();
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
    }

    public override void OnNetworkDespawn()
    {
        if (!IsOwner) return;
        var cam = ResolveCamera();
        var target = cameraFollowTarget != null ? cameraFollowTarget : transform;
        if (cam != null && cam.Follow == target) cam.Follow = null;
    }

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

    private void CreateAimingArrow()
    {
        if (arrowSprite == null) return;
        aimArrowInstance = new GameObject("AimArrow_Local");
        aimArrowInstance.transform.localScale = Vector3.one * arrowScale;
        aimArrowRenderer = aimArrowInstance.AddComponent<SpriteRenderer>();
        aimArrowRenderer.sprite = arrowSprite;
        if (playerSprite != null) {
            aimArrowRenderer.sortingLayerName = playerSprite.sortingLayerName;
            aimArrowRenderer.sortingOrder = playerSprite.sortingOrder + 1;
        }
        aimArrowRenderer.color = Color.yellow;
    }

    public void SetArrowVisibility(bool isVisible)
    {
        if (aimArrowRenderer != null) aimArrowRenderer.enabled = isVisible;
        if (isVisible && aimArrowInstance == null && IsOwner) CreateAimingArrow();
    }

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

        // KAOS: TERS KONTROL
        if (LuckyBox.ActiveGlobalEvent.Value == ModifierType.ReverseControls)
        {
            moveX *= -1;
            moveY *= -1;
        }

        moveInput = new Vector2(moveX, moveY).normalized;
        UpdateAimingIndicator();

        // ATEŞ ETME (Kısıtlama ve Hız Ayarı)
        if (Input.GetButton("Fire1") && Time.time >= nextFireTime)
        {
            if (fireballPrefab != null && aimArrowInstance != null)
            {
                float projSpeed = fireballBaseSpeed;
                if (LuckyBox.ActiveGlobalEvent.Value == ModifierType.SpeedBoost)
                {
                    projSpeed *= speedBoostMultiplier; 
                }

                ShootServerRpc(aimArrowInstance.transform.position, aimArrowInstance.transform.rotation, projSpeed);
                nextFireTime = Time.time + fireRate;
            }
        }
    }

    private void UpdateAimingIndicator()
    {
        if (aimArrowInstance == null) {
            if (playerSprite != null && playerSprite.enabled) CreateAimingArrow();
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

    private void Move()
    {
        float currentSpeed = moveSpeed;
        if (LuckyBox.ActiveGlobalEvent.Value == ModifierType.SpeedBoost)
        {
            currentSpeed *= speedBoostMultiplier;
        }

        if (rb != null) rb.linearVelocity = moveInput * currentSpeed;
        else transform.position += (Vector3)moveInput * currentSpeed * Time.deltaTime;
    }

    [ServerRpc]
    private void ShootServerRpc(Vector3 position, Quaternion rotation, float speed, ServerRpcParams rpcParams = default)
    {
        GameObject fireball = Instantiate(fireballPrefab, position, rotation);
        
        // HATA ÇÖZÜMÜ: Senin scriptinin adı FireballLogic olduğu için onu arıyoruz
        var fireballScript = fireball.GetComponent<FireballLogic>(); 
        if (fireballScript != null)
        {
            // Burası önemli: FireballLogic içindeki speed değerini set ediyoruz
            fireballScript.SetSpeed(speed);
        }

        ulong shooterId = rpcParams.Receive.SenderClientId;
        fireball.GetComponent<NetworkObject>().SpawnWithOwnership(shooterId);
    }

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
        while (timer < 3f) {
            points = GameObject.FindGameObjectsWithTag("Respawn");
            if (points.Length > 0) break;
            yield return new WaitForSeconds(0.1f);
            timer += 0.1f;
        }
        if (points != null && points.Length > 0) {
            var target = points[Random.Range(0, points.Length)].transform.position;
            if (rb != null) rb.position = target;
            else transform.position = target;
        }
        SetVisibility(true);
        CreateAimingArrow();
    }

    // NetworkBehaviour OnDestroy override hatasını çözdük
    public override void OnDestroy() 
    { 
        base.OnDestroy();
        if (aimArrowInstance != null) Destroy(aimArrowInstance); 
    }

    private void OnDisable() { if (aimArrowRenderer != null) aimArrowRenderer.enabled = false; }
    private void OnEnable() { if (aimArrowRenderer != null && IsOwner) aimArrowRenderer.enabled = true; }
}