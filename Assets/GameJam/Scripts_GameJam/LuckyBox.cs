using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;
using TMPro;

/// <summary>
/// LuckyBox global event system with full SizeChange support.
/// 
/// Modifiers:
/// - SpeedBoost: All players move 2x faster
/// - ReverseControls: All players have inverted controls  
/// - SlimeShift: ALL players transform into Slime form
/// - SizeChange: All players scale up/down (with separate visual/collider scaling)
/// 
/// Place in GameScene. Server-authoritative.
/// </summary>
public enum ModifierType 
{ 
    None = 0,
    SpeedBoost = 1, 
    ReverseControls = 2,
    SlimeShift = 3,
    SizeChange = 4
}

public class LuckyBox : NetworkBehaviour
{
    #region Singleton
    
    public static LuckyBox Instance { get; private set; }
    
    #endregion

    #region Network Variables

    /// <summary>
    /// Current active global event
    /// </summary>
    public NetworkVariable<ModifierType> ActiveGlobalEvent = new NetworkVariable<ModifierType>(
        ModifierType.None, 
        NetworkVariableReadPermission.Everyone, 
        NetworkVariableWritePermission.Server
    );

    /// <summary>
    /// Countdown timer (seconds remaining)
    /// </summary>
    public NetworkVariable<int> EventTimer = new NetworkVariable<int>(0);

    /// <summary>
    /// Size multiplier for SizeChange event (0.5 to 1.5 typically)
    /// </summary>
    public NetworkVariable<float> SizeMultiplier = new NetworkVariable<float>(
        1.0f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    #endregion

    #region Inspector Fields

    [Header("Event Timing")]
    [SerializeField] private int timeBetweenEvents = 4;
    [SerializeField] private int eventDuration = 10;

    [Header("Modifier Weights")]
    [SerializeField] private float speedBoostWeight = 1f;
    [SerializeField] private float reverseControlsWeight = 1f;
    [SerializeField] private float slimeShiftWeight = 1f;
    [SerializeField] private float sizeChangeWeight = 1f;

    [Header("Size Change Settings")]
    [Tooltip("Minimum size multiplier (smaller = harder to hit but weaker)")]
    [SerializeField] private float minSizeMultiplier = 0.5f;
    
    [Tooltip("Maximum size multiplier (larger = easier to hit but stronger presence)")]
    [SerializeField] private float maxSizeMultiplier = 1.5f;

    [Header("UI References")]
    [SerializeField] private TextMeshProUGUI eventDisplayText;
    [SerializeField] private Image eventBackgroundImage;
    [SerializeField] private Image timerFillImage;

    [Header("Audio/VFX")]
    [SerializeField] private GameObject eventActivationVFX;
    [SerializeField] private AudioClip eventStartSound;
    [SerializeField] private AudioClip eventTriggerSound;
    [SerializeField] private AudioClip eventEndSound;
    [SerializeField] private AudioClip countdownTickSound;

    #endregion

    #region Private Fields

    private AudioSource audioSource;
    private Dictionary<ModifierType, float> modifierWeights;
    private float totalWeight;
    private Dictionary<ulong, bool> preSlimeShiftForms = new Dictionary<ulong, bool>();

    #endregion

    #region Unity Lifecycle

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        audioSource = GetComponent<AudioSource>();
        if (audioSource == null)
            audioSource = gameObject.AddComponent<AudioSource>();

        modifierWeights = new Dictionary<ModifierType, float>
        {
            { ModifierType.SpeedBoost, speedBoostWeight },
            { ModifierType.ReverseControls, reverseControlsWeight },
            { ModifierType.SlimeShift, slimeShiftWeight },
            { ModifierType.SizeChange, sizeChangeWeight }
        };

        totalWeight = speedBoostWeight + reverseControlsWeight + slimeShiftWeight + sizeChangeWeight;
    }

    public override void OnNetworkSpawn()
    {
        ActiveGlobalEvent.OnValueChanged += OnEventChanged;
        EventTimer.OnValueChanged += OnTimerChanged;
        SizeMultiplier.OnValueChanged += OnSizeMultiplierChanged;

        RefreshUI();

        if (IsServer)
        {
            StartCoroutine(GlobalEventCycle());
        }

        Debug.Log("[LuckyBox] Network spawned.");
    }

    public override void OnNetworkDespawn()
    {
        ActiveGlobalEvent.OnValueChanged -= OnEventChanged;
        EventTimer.OnValueChanged -= OnTimerChanged;
        SizeMultiplier.OnValueChanged -= OnSizeMultiplierChanged;
    }

    #endregion

    #region Event Cycle

    private IEnumerator GlobalEventCycle()
    {
        while (true)
        {
            // === COUNTDOWN PHASE ===
            ActiveGlobalEvent.Value = ModifierType.None;
            SizeMultiplier.Value = 1.0f;
            
            for (int i = timeBetweenEvents; i > 0; i--)
            {
                EventTimer.Value = i;
                
                if (i <= 3)
                {
                    PlayTickSoundClientRpc();
                }
                
                yield return new WaitForSeconds(1f);
            }

            // === SELECT RANDOM EVENT ===
            ModifierType selectedEvent = SelectRandomModifier();
            
            // Pre-event setup
            if (selectedEvent == ModifierType.SlimeShift)
            {
                StorePlayerForms();
            }
            
            if (selectedEvent == ModifierType.SizeChange)
            {
                // Set size multiplier BEFORE activating event
                float newSize = Random.Range(minSizeMultiplier, maxSizeMultiplier);
                SizeMultiplier.Value = newSize;
                Debug.Log($"[LuckyBox] SizeChange multiplier set to: {newSize:F2}x");
            }

            // Activate event
            ActiveGlobalEvent.Value = selectedEvent;
            ApplyEventToAllPlayers(selectedEvent, true);
            PlayEventStartClientRpc(selectedEvent);

            Debug.Log($"[LuckyBox] Event started: {selectedEvent}");

            // === EVENT ACTIVE PHASE ===
            for (int i = eventDuration; i > 0; i--)
            {
                EventTimer.Value = i;
                yield return new WaitForSeconds(1f);
            }

            // === END EVENT ===
            ApplyEventToAllPlayers(selectedEvent, false);
            PlayEventEndClientRpc();
            
            // Reset size after SizeChange ends
            if (selectedEvent == ModifierType.SizeChange)
            {
                SizeMultiplier.Value = 1.0f;
            }
            
            Debug.Log($"[LuckyBox] Event ended: {selectedEvent}");
        }
    }

    private ModifierType SelectRandomModifier()
    {
        float randomValue = Random.Range(0f, totalWeight);
        float cumulative = 0f;

        foreach (var kvp in modifierWeights)
        {
            cumulative += kvp.Value;
            if (randomValue <= cumulative)
            {
                return kvp.Key;
            }
        }

        return ModifierType.SpeedBoost;
    }

    #endregion

    #region Event Application

    private void ApplyEventToAllPlayers(ModifierType eventType, bool activate)
    {
        if (!IsServer) return;

        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            if (client.PlayerObject == null) continue;

            GameObject playerObj = client.PlayerObject.gameObject;
            
            switch (eventType)
            {
                case ModifierType.SlimeShift:
                    ApplySlimeShift(playerObj, client.ClientId, activate);
                    break;
                    
                case ModifierType.SizeChange:
                    // SizeChange is handled by SizeChangeHandler listening to SizeMultiplier
                    // But we can notify it to refresh
                    NotifySizeChange(playerObj, activate);
                    break;
                    
                // SpeedBoost and ReverseControls are handled by PlayerController reading ActiveGlobalEvent
            }
        }
    }

    private void StorePlayerForms()
    {
        preSlimeShiftForms.Clear();
        
        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            if (client.PlayerObject == null) continue;
            
            SlimeController slime = client.PlayerObject.GetComponent<SlimeController>();
            if (slime != null)
            {
                preSlimeShiftForms[client.ClientId] = slime.isSlimeForm.Value;
            }
        }
    }

    private void ApplySlimeShift(GameObject playerObj, ulong clientId, bool activate)
    {
        SlimeController slime = playerObj.GetComponent<SlimeController>();
        if (slime == null) return;

        if (activate)
        {
            slime.TransformToSlime();
        }
        else
        {
            bool wasSlime = preSlimeShiftForms.ContainsKey(clientId) && preSlimeShiftForms[clientId];
            
            if (!wasSlime)
            {
                slime.TransformToWizard();
            }
        }
    }

    private void NotifySizeChange(GameObject playerObj, bool activate)
    {
        SizeChangeHandler sizeHandler = playerObj.GetComponent<SizeChangeHandler>();
        if (sizeHandler != null)
        {
            if (activate)
            {
                sizeHandler.RefreshScale();
            }
            else
            {
                sizeHandler.ResetToBaseScale();
            }
        }
    }

    #endregion

    #region UI Updates

    private void OnTimerChanged(int oldValue, int newValue)
    {
        RefreshUI();
    }

    private void OnEventChanged(ModifierType oldValue, ModifierType newValue)
    {
        RefreshUI();
    }

    private void OnSizeMultiplierChanged(float oldValue, float newValue)
    {
        RefreshUI();
    }

    private void RefreshUI()
    {
        if (eventDisplayText == null) return;

        ModifierType currentEvent = ActiveGlobalEvent.Value;
        int timer = EventTimer.Value;

        if (currentEvent == ModifierType.None)
        {
            eventDisplayText.text = $"Next Event In: {timer}s";
            eventDisplayText.color = Color.white;
            
            if (eventBackgroundImage != null)
                eventBackgroundImage.color = new Color(0, 0, 0, 0.5f);
        }
        else
        {
            string eventName = GetEventDisplayName(currentEvent);
            string extra = "";
            
            if (currentEvent == ModifierType.SizeChange)
            {
                float size = SizeMultiplier.Value;
                string sizeDesc = size < 1f ? "SHRINK" : "GROW";
                extra = $" ({size:F1}x {sizeDesc})";
            }
            
            eventDisplayText.text = $"{eventName}{extra} ({timer}s)";
            eventDisplayText.color = GetEventColor(currentEvent);
            
            if (eventBackgroundImage != null)
                eventBackgroundImage.color = GetEventColor(currentEvent) * 0.3f;
        }

        if (timerFillImage != null)
        {
            float maxTime = currentEvent == ModifierType.None ? timeBetweenEvents : eventDuration;
            timerFillImage.fillAmount = timer / maxTime;
        }
    }

    private string GetEventDisplayName(ModifierType eventType)
    {
        return eventType switch
        {
            ModifierType.SpeedBoost => "⚡ SPEED BOOST!",
            ModifierType.ReverseControls => "🔄 REVERSE!",
            ModifierType.SlimeShift => "🟢 SLIME SHIFT!",
            ModifierType.SizeChange => "📏 SIZE CHANGE!",
            _ => "NONE"
        };
    }

    private Color GetEventColor(ModifierType eventType)
    {
        return eventType switch
        {
            ModifierType.SpeedBoost => Color.cyan,
            ModifierType.ReverseControls => Color.magenta,
            ModifierType.SlimeShift => Color.green,
            ModifierType.SizeChange => Color.yellow,
            _ => Color.white
        };
    }

    #endregion

    #region Client RPCs

    [ClientRpc]
    private void PlayEventStartClientRpc(ModifierType eventType)
    {
        if (eventActivationVFX != null)
        {
            Instantiate(eventActivationVFX, Vector3.zero, Quaternion.identity);
        }

        if (eventStartSound != null && audioSource != null)
        {
            audioSource.PlayOneShot(eventStartSound);
        }

        if (eventTriggerSound != null && audioSource != null)
        {
            audioSource.PlayOneShot(eventTriggerSound);
        }
    }

    [ClientRpc]
    private void PlayEventEndClientRpc()
    {
        if (eventEndSound != null && audioSource != null)
        {
            audioSource.PlayOneShot(eventEndSound);
        }
    }

    [ClientRpc]
    private void PlayTickSoundClientRpc()
    {
        if (countdownTickSound != null && audioSource != null)
        {
            audioSource.PlayOneShot(countdownTickSound, 0.5f);
        }
    }

    #endregion

    #region Public API

    public static float GetGlobalSpeedMultiplier()
    {
        if (Instance == null) return 1.0f;
        return Instance.ActiveGlobalEvent.Value == ModifierType.SpeedBoost ? 2.0f : 1.0f;
    }

    public static bool AreControlsReversed()
    {
        if (Instance == null) return false;
        return Instance.ActiveGlobalEvent.Value == ModifierType.ReverseControls;
    }

    public static bool IsSlimeShiftActive()
    {
        if (Instance == null) return false;
        return Instance.ActiveGlobalEvent.Value == ModifierType.SlimeShift;
    }

    public static float GetSizeMultiplier()
    {
        if (Instance == null) return 1.0f;
        if (Instance.ActiveGlobalEvent.Value == ModifierType.SizeChange)
            return Instance.SizeMultiplier.Value;
        return 1.0f;
    }

    public static bool IsSizeChangeActive()
    {
        if (Instance == null) return false;
        return Instance.ActiveGlobalEvent.Value == ModifierType.SizeChange;
    }

    /// <summary>
    /// Force a specific event (for testing)
    /// </summary>
    public void ForceEvent(ModifierType eventType, float sizeMultiplier = 1f)
    {
        if (!IsServer) return;

        StopAllCoroutines();
        
        if (eventType == ModifierType.SlimeShift)
            StorePlayerForms();
        
        if (eventType == ModifierType.SizeChange)
            SizeMultiplier.Value = sizeMultiplier;
        
        ActiveGlobalEvent.Value = eventType;
        ApplyEventToAllPlayers(eventType, true);
        StartCoroutine(ForcedEventCoroutine(eventType));
    }

    private IEnumerator ForcedEventCoroutine(ModifierType eventType)
    {
        for (int i = eventDuration; i > 0; i--)
        {
            EventTimer.Value = i;
            yield return new WaitForSeconds(1f);
        }
        
        ApplyEventToAllPlayers(eventType, false);
        
        if (eventType == ModifierType.SizeChange)
            SizeMultiplier.Value = 1.0f;
        
        ActiveGlobalEvent.Value = ModifierType.None;
        StartCoroutine(GlobalEventCycle());
    }

    #endregion
}
