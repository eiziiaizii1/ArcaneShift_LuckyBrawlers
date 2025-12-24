using Unity.Netcode;
using UnityEngine;
using System.Collections;

// Quest 16: Lucky Box Modifikasyon Tipleri
public enum ModifierType
{
    None,
    SpeedBoost,
    SlimeMode,
    DoubleScore,
    GhostMode
}

public class LuckyBox : NetworkBehaviour
{
    // Sunucu kontrollü senkronize değişken (Sadece sunucu yazar, herkes okur)
    public NetworkVariable<ModifierType> currentModifier = new NetworkVariable<ModifierType>(
        ModifierType.None, 
        NetworkVariableReadPermission.Everyone, 
        NetworkVariableWritePermission.Server
    );

    [SerializeField] private GameObject boxVisual; // Kutunun görsel objesi
    private bool isAvailable = false; // Kutu şu an toplanabilir mi?

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            // Sunucu kutunun döngüsünü başlatır
            StartCoroutine(SpawnCycle());
        }

        // Kutu içeriği değiştiğinde (dolduğunda veya boşaldığında) tüm clientlarda tetiklenir
        currentModifier.OnValueChanged += OnModifierChanged;
    }

    private void OnModifierChanged(ModifierType oldVal, ModifierType newVal)
    {
        // Kutu dolduysa görseli aç, boşaldıysa kapat
        bool hasContent = (newVal != ModifierType.None);
        boxVisual.SetActive(hasContent);
        
        if (hasContent)
            Debug.Log($"<color=cyan>[Lucky Box]</color> Kutuda yeni bir güç belirdi: {newVal}");
    }

    private IEnumerator SpawnCycle()
    {
        while (true)
        {
            // Kutu boşsa 15-20 saniye bekle ve yenisini oluştur
            if (!isAvailable)
            {
                float waitTime = Random.Range(15f, 20f);
                yield return new WaitForSeconds(waitTime);

                isAvailable = true;
                // Rastgele bir güç seç (None hariç)
                currentModifier.Value = (ModifierType)Random.Range(1, 5);
            }
            yield return null;
        }
    }

   private void OnTriggerEnter2D(Collider2D other)
{
    // 1. Çarpışma tetikleniyor mu? (Konsolda bunu görmelisin)
    Debug.Log($"<color=orange>[LuckyBox]</color> Temas algılandı: {other.name}");

    if (!IsServer || !isAvailable) return;

    if (other.CompareTag("Player"))
    {
        Debug.Log("<color=green>[LuckyBox]</color> Çarpan bir oyuncu! Güç uygulanıyor.");
        var networkObject = other.GetComponent<NetworkObject>();
        if (networkObject != null)
        {
            ApplyModifier(networkObject.OwnerClientId, currentModifier.Value);
            isAvailable = false;
            currentModifier.Value = ModifierType.None;
        }
    }
    else
    {
        // Eğer çarptığın halde Tag tutmuyorsa burası yazar
        Debug.Log($"<color=red>[LuckyBox]</color> Çarpanın Tag'i yanlış: {other.tag}");
    }
}
    private void ApplyModifier(ulong clientId, ModifierType type)
    {
        Debug.Log($"<color=green>BAŞARI:</color> Oyuncu {clientId} şu gücü kazandı: {type}");
        
        // BURAYA GELECEK: Quest 17 efektlerini tetikleyen fonksiyonlar
        // Örn: PlayerController.instance.GetModifierRpc(type, clientId);
    }
}
