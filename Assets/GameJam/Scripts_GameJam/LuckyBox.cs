using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI; // UI kütüphanesi şart
using System.Collections;
using TMPro;


// Etkinlik tiplerini burada tanımlıyoruz ki PlayerController da görebilsin
public enum ModifierType { None, SpeedBoost, ReverseControls }

public class LuckyBox : NetworkBehaviour
{
    // Global etkinlik durumu (Sadece sunucu yazar, herkes okur)
    public static NetworkVariable<ModifierType> ActiveGlobalEvent = new NetworkVariable<ModifierType>(
        ModifierType.None, 
        NetworkVariableReadPermission.Everyone, 
        NetworkVariableWritePermission.Server
    );

    // Geri sayım saniyesi
    public static NetworkVariable<int> EventTimer = new NetworkVariable<int>(0);

    [Header("UI Settings")]
    // Eğer TextMeshPro kullanıyorsan aşağıdaki satırı kullan:
    [SerializeField] private TextMeshProUGUI eventDisplayText;

    public override void OnNetworkSpawn()
    {
        if (IsServer) StartCoroutine(GlobalEventCycle());
        
        // Değerler değiştikçe UI yazısını yenile
        EventTimer.OnValueChanged += (oldVal, newVal) => RefreshUI();
        ActiveGlobalEvent.OnValueChanged += (oldVal, newVal) => RefreshUI();
    }

    private void RefreshUI()
    {
        if (eventDisplayText == null) return;

        // Etkinlik yoksa bekleme süresini göster
        if (ActiveGlobalEvent.Value == ModifierType.None)
        {
            eventDisplayText.text = "Next Event In: " + EventTimer.Value + "s";
            eventDisplayText.color = Color.white;
        }
        else
        {
            // Etkinlik varsa ismini ve kalan süresini göster
            string eventName = ActiveGlobalEvent.Value == ModifierType.SpeedBoost ? "SPEED BOOST!" : "REVERSE CONTROLS!";
            eventDisplayText.text = "CURRENT EVENT: " + eventName + " (" + EventTimer.Value + "s)";
            eventDisplayText.color = Color.yellow; // Dikkat çekmesi için sarı yapıyoruz
        }
    }

    private IEnumerator GlobalEventCycle()
    {
        while (true)
        {
            // 5 saniye boyunca "Sıradaki Etkinlik" geri sayımı
            for (int i = 5; i > 0; i--)
            {
                EventTimer.Value = i;
                yield return new WaitForSeconds(1f);
            }

            // Rastgele kaos seç (1 veya 2)
            ActiveGlobalEvent.Value = (ModifierType)Random.Range(1, 3);

            // 20 saniye boyunca "Mevcut Etkinlik" geri sayımı
            for (int i = 20; i > 0; i--)
            {
                EventTimer.Value = i;
                yield return new WaitForSeconds(1f);
            }

            // Her şeyi sıfırla ve başa dön
            ActiveGlobalEvent.Value = ModifierType.None;
        }
    }
}