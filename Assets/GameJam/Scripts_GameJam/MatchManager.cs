using Unity.Netcode;
using UnityEngine;
using TMPro;

public class MatchManager : NetworkBehaviour
{
    public NetworkVariable<float> RemainingTime = new NetworkVariable<float>(300f);
    public NetworkVariable<bool> IsMatchEnded = new NetworkVariable<bool>(false);

    [Header("UI Referansları")]
    [SerializeField] private TextMeshProUGUI timerText;
    [SerializeField] private GameObject endGamePanel; // Skor tablosunun olduğu panel
    [SerializeField] private GameObject mainMenuButton; // Panelin DIŞINDAKİ buton

    public override void OnNetworkSpawn()
    {
        if (IsServer) StartCoroutine(MatchTimerRoutine());
        
        IsMatchEnded.OnValueChanged += (oldVal, newVal) => {
            if (newVal) StopGameAndShowUI();
        };
    }

    private System.Collections.IEnumerator MatchTimerRoutine()
    {
        while (RemainingTime.Value > 0)
        {
            yield return new WaitForSeconds(1f);
            RemainingTime.Value -= 1f;
        }
        if (IsServer) IsMatchEnded.Value = true;
    }

    private void StopGameAndShowUI()
    {
        // Zamanı durdurur, böylece mermiler ve coinler donar
        Time.timeScale = 0f;

        // Paneli ve butonu ayrı ayrı aktif ediyoruz
        if (endGamePanel != null) endGamePanel.SetActive(true); //
        if (mainMenuButton != null) mainMenuButton.SetActive(true); //

        // Yerel oyuncunun kontrollerini kapatır
        if (NetworkManager.Singleton.LocalClient != null)
        {
            var localPlayer = NetworkManager.Singleton.LocalClient.PlayerObject;
            if (localPlayer != null) localPlayer.GetComponent<PlayerController>().enabled = false;
        }
    }

    private void Update()
    {
        if (timerText != null && RemainingTime.Value >= 0)
        {
            int minutes = Mathf.FloorToInt(RemainingTime.Value / 60);
            int seconds = Mathf.FloorToInt(RemainingTime.Value % 60);
            timerText.text = string.Format("{0:00}:{1:00}", minutes, seconds);
        }
    }

    // Butonun OnClick olayına bağlanacak fonksiyon
    public void ExitToMenu()
    {
        Time.timeScale = 1f; // ÖNEMLİ: Zamanı tekrar başlatmazsan menü donuk kalır!
        NetworkManager.Singleton.Shutdown();
        UnityEngine.SceneManagement.SceneManager.LoadScene("MainMenu");
    }
}