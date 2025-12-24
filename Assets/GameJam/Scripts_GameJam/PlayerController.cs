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

    private Rigidbody2D rb;
    private Vector2 moveInput;

    private static CinemachineCamera cachedSceneCamera;
    
    // Görünürlük referansları (Respawn sırasında gizlenmek için)
    private SpriteRenderer playerSprite;
    private Canvas nameCanvas;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        playerSprite = GetComponent<SpriteRenderer>();
        nameCanvas = GetComponentInChildren<Canvas>();
    }

    public override void OnNetworkSpawn()
    {
        if (IsOwner)
        {
            // 1. Kamera Bağlama (coroutine ile bekle)
            StartCoroutine(SetupCameraFollow());

            // 2. HOST SPAWN FIX: Host (0,0)'da doğarsa spawn noktasına taşı
            if (IsServer)
            {
                SetVisibility(false);
                StartCoroutine(MoveHostToSpawnPoint());
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

        // Kamera bulunana kadar bekle (max 3 saniye)
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
        else
        {
            Debug.LogWarning("CinemachineCamera bulunamadı!");
        }
    }

    private CinemachineCamera ResolveCamera()
    {
        if (sceneCamera != null) return sceneCamera;
        if (cachedSceneCamera != null) return cachedSceneCamera;
        cachedSceneCamera = FindFirstObjectByType<CinemachineCamera>(FindObjectsInactive.Include);
        return cachedSceneCamera;
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

        // Mouse'a Bakma
        RotateTowardsMouse();

        // Ateş Etme (Sol Tık)
        if (Input.GetButtonDown("Fire1"))
        {
            if (fireballPrefab != null && firePoint != null)
            {
                // RPC çağırırken parametre göndermemize gerek yok, 
                // Unity "ServerRpcParams" ile göndereni zaten biliyor.
                ShootServerRpc(firePoint.position, firePoint.rotation);
            }
        }
    }

    private void RotateTowardsMouse()
    {
        Vector3 mousePos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
        Vector2 direction = mousePos - transform.position;
        float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg - 90f;
        transform.rotation = Quaternion.Euler(0, 0, angle);
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
    }
}
