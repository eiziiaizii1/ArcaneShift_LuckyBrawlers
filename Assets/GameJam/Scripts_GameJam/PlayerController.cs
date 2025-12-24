using System.Collections;
using Unity.Netcode;
using UnityEngine;
using Unity.Cinemachine;

public class PlayerController : NetworkBehaviour
{
    // Quest 17 için gerekli skor değişkeni
    public NetworkVariable<int> score = new NetworkVariable<int>(0);
    
    [Header("Movement Settings")]
    [SerializeField] private float moveSpeed = 5f;

    [Header("Combat Settings")]
    [SerializeField] private GameObject fireballPrefab; // Editörden atanacak
    [SerializeField] private Transform firePoint;       // Editörden atanacak

    [Header("Camera Settings")]
    [SerializeField] private CinemachineCamera sceneCamera;
    [SerializeField] private Transform cameraFollowTarget;
    [SerializeField] private float cameraSearchTimeout = 5f;

    [Header("Aiming Settings")]
    [SerializeField] private Sprite arrowSprite; // Sadece sprite asset'i
    [SerializeField] private float aimIndicatorRadius = 1.5f; // Okun oyuncunun etrafında dönme yarıçapı
    [SerializeField] private float arrowScale = 0.5f; // Ok boyutu

    private Rigidbody2D rb;
    private Vector2 moveInput;
    private GameObject aimArrowInstance; // Spawn edilen ok nesnesi
    private SpriteRenderer aimArrowRenderer;

    private static CinemachineCamera cachedSceneCamera;
    
    // Görünürlük referansları (Respawn sırasında gizlenmek için)
    private SpriteRenderer playerSprite;
    private Canvas nameCanvas;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        
        // SpriteRenderer'ı bul - hem parent'ta hem child'da arayabilir
        playerSprite = GetComponent<SpriteRenderer>();
        if (playerSprite == null)
        {
            playerSprite = GetComponentInChildren<SpriteRenderer>();
        }
        
        nameCanvas = GetComponentInChildren<Canvas>();
        
        if (playerSprite != null)
        {
            Debug.Log("[PlayerController] Player sprite found successfully");
        }
        else
        {
            Debug.LogWarning("[PlayerController] Player sprite not found!");
        }
    }

    public override void OnNetworkSpawn()
    {
        if (IsOwner)
        {
            Debug.Log("[PlayerController] OnNetworkSpawn called for owner");
            
            // 1. Kamera Bağlama (coroutine ile bekle)
            StartCoroutine(SetupCameraFollow());

            // 2. HOST SPAWN FIX: Host (0,0)'da doğarsa spawn noktasına taşı
            if (IsServer)
            {
                SetVisibility(false);
                StartCoroutine(MoveHostToSpawnPoint());
            }
            else
            {
                // Client ise direkt arrow'u spawn et
                CreateAimingArrow();
            }
        }
        else
        {
            this.enabled = false; // Başkasını kontrol etme
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

        // Kamera bulunana kadar bekle (max 5 saniye)
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
            Debug.Log("[PlayerController] Camera follow set successfully");
        }
        else
        {
            Debug.LogWarning("[PlayerController] CinemachineCamera bulunamadı!");
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
        if (arrowSprite == null)
        {
            Debug.LogError("[PlayerController] Arrow Sprite is not assigned! Please assign an arrow sprite in the inspector.");
            return;
        }

        // Tamamen yeni bir GameObject oluştur
        aimArrowInstance = new GameObject("AimArrow_Local");
        aimArrowInstance.transform.position = transform.position;
        aimArrowInstance.transform.rotation = Quaternion.identity;
        aimArrowInstance.transform.localScale = Vector3.one * arrowScale;

        // SpriteRenderer ekle
        aimArrowRenderer = aimArrowInstance.AddComponent<SpriteRenderer>();
        aimArrowRenderer.sprite = arrowSprite;
        
        // Player sprite varsa onun sorting layer'ını kullan
        if (playerSprite != null)
        {
            aimArrowRenderer.sortingLayerName = playerSprite.sortingLayerName;
            aimArrowRenderer.sortingOrder = playerSprite.sortingOrder + 1;
        }
        else
        {
            aimArrowRenderer.sortingLayerName = "Default";
            aimArrowRenderer.sortingOrder = 10;
        }
        
        aimArrowRenderer.color = Color.yellow; // Sarı renk - görünür olsun

        Debug.Log($"[PlayerController] Arrow created successfully at {aimArrowInstance.transform.position}");
    }

    // Public method for Health.cs to control arrow visibility
    public void SetArrowVisibility(bool isVisible)
    {
        if (aimArrowRenderer != null)
        {
            aimArrowRenderer.enabled = isVisible;
        }
        
        // Eğer görünür hale geliyorsa ama arrow yoksa, oluştur
        if (isVisible && aimArrowInstance == null && IsOwner)
        {
            CreateAimingArrow();
        }
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
        // Hareket
        float moveX = Input.GetAxisRaw("Horizontal");
        float moveY = Input.GetAxisRaw("Vertical");
        moveInput = new Vector2(moveX, moveY).normalized;

        // Aiming Indicator'ı Güncelle
        UpdateAimingIndicator();

        // Ateş Etme (Sol Tık)
        if (Input.GetButtonDown("Fire1"))
        {
            if (fireballPrefab != null && aimArrowInstance != null)
            {
                // Mermi arrow'un pozisyonundan fırlatılsın
                Vector3 shootPosition = aimArrowInstance.transform.position;
                Quaternion shootRotation = aimArrowInstance.transform.rotation;
                
                ShootServerRpc(shootPosition, shootRotation);
            }
        }
    }

    private void UpdateAimingIndicator()
    {
        if (aimArrowInstance == null)
        {
            // Eğer arrow yoksa ve player görünürse, oluştur
            if (playerSprite != null && playerSprite.enabled)
            {
                CreateAimingArrow();
            }
            return;
        }

        // Mouse pozisyonunu al
        Vector3 mousePos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
        mousePos.z = 0f;

        // Player'dan mouse'a yön vektörü
        Vector2 direction = (mousePos - transform.position).normalized;

        // Ok'u oyuncunun etrafında aimIndicatorRadius kadar uzakta konumlandır
        Vector3 arrowPosition = transform.position + (Vector3)direction * aimIndicatorRadius;
        arrowPosition.z = 0f; // Z eksenini sıfırla
        aimArrowInstance.transform.position = arrowPosition;

        // Ok'u mouse yönüne çevir (Ok sprite'ının yukarı baktığını varsayıyoruz)
        float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg - 90f;
        aimArrowInstance.transform.rotation = Quaternion.Euler(0, 0, angle);
    }

    private void Move()
    {
        if (rb != null) rb.linearVelocity = moveInput * moveSpeed;
        else transform.position += (Vector3)moveInput * moveSpeed * Time.deltaTime;
    }

    // --- COMBAT NETWORKING ---

    [ServerRpc]
    private void ShootServerRpc(Vector3 position, Quaternion rotation, ServerRpcParams rpcParams = default)
    {
        // 1. Mermiyi sunucuda yarat
        GameObject fireball = Instantiate(fireballPrefab, position, rotation);
        
        // 2. SAHİPLİK AYARI (Kritik Kısım!)
        // Merminin sahibi, bu fonksiyonu çağıran kişi (SenderClientId) olsun.
        ulong shooterId = rpcParams.Receive.SenderClientId;
        fireball.GetComponent<NetworkObject>().SpawnWithOwnership(shooterId);
    }

    // --- YARDIMCI FONKSİYONLAR (Host Spawn Fix) ---

    private void SetVisibility(bool isVisible)
    {
        if (playerSprite != null) playerSprite.enabled = isVisible;
        if (nameCanvas != null) nameCanvas.enabled = isVisible;
        
        if (aimArrowRenderer != null)
        {
            aimArrowRenderer.enabled = isVisible;
        }
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
        
        // Host için arrow'u burada oluştur (visibility true olduktan sonra)
        CreateAimingArrow();
    }

    private void OnDestroy()
    {
        // Aim arrow'u temizle
        if (aimArrowInstance != null)
        {
            Destroy(aimArrowInstance);
        }
    }

    private void OnDisable()
    {
        // Script disable olduğunda arrow'u gizle
        if (aimArrowRenderer != null)
        {
            aimArrowRenderer.enabled = false;
        }
    }

    private void OnEnable()
    {
        // Script enable olduğunda arrow'u göster (eğer varsa)
        if (aimArrowRenderer != null && IsOwner)
        {
            aimArrowRenderer.enabled = true;
        }
    }
}