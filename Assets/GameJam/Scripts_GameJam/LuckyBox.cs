using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;
using TMPro;

/// <summary>
/// Enhanced Lucky Box system with Slime Shift modifier.
/// Handles global events that affect ALL players simultaneously.
/// 
/// Modifiers:
/// - SpeedBoost: All players move faster
/// - ReverseControls: All players have inverted controls  
/// - SlimeShift: ALL players transform into Slime form
/// - SizeChange: All players become larger/smaller
/// 
/// Place in GameScene. Server-authoritative.
/// </summary>
public enum ModifierType 
{ 
    None = 0,
    SpeedBoost = 1, 
    ReverseControls = 2,
    SlimeShift = 3,      // NEW: Transform everyone to slime
    SizeChange = 4       // NEW: Make everyone bigger/smaller
}

public class LuckyBox : NetworkBehaviour
{
    #region Singleton
    
    public static LuckyBox Instance { get; private set; }
    
    #endregion

    #region Network Variables

    /// <summary>
    /// Current active global event. All clients read this to know what modifier is active.
    /// </summary>
    public static NetworkVariable<ModifierType> ActiveGlobalEvent = new NetworkVariable<ModifierType>(
        ModifierType.None, 
        NetworkVariableReadPermission.Everyone, 
        NetworkVariableWritePermission.Server
    );

    /// <summary>
    /// Countdown timer (seconds remaining)
    /// </summary>
    public static NetworkVariable<int> EventTimer = new NetworkVariable<int>(0);

    /// <summary>
    /// For SizeChange: the size multiplier (0.5 = half size, 2.0 = double)
    /// </summary>
    public static NetworkVariable<float> SizeMultiplier = new NetworkVariable<float>(
        1.0f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    #endregion

    #region Inspector Fields

    [Header("Event Timing")]
    [Tooltip("Time between events (countdown)")]
    [SerializeField] private int timeBetweenEvents = 5;
    
    [Tooltip("Duration of each event")]
    [SerializeField] private int eventDuration = 20;

    [Header("Modifier Weights")]
    [Tooltip("Chance weights for each modifier type")]
    [SerializeField] private float speedBoostWeight = 1f;
    [SerializeField] private float reverseControlsWeight = 1f;
    [SerializeField] private float slimeShiftWeight = 1f;
    [SerializeField] private float sizeChangeWeight = 1f;

    [Header("Size Change Settings")]
    [SerializeField] private float minSizeMultiplier = 0.5f;
    [SerializeField] private float maxSizeMultiplier = 1.5f;

    [Header("UI References")]
    [SerializeField] private TextMeshProUGUI eventDisplayText;
    [SerializeField] private Image eventBackgroundImage;
    [SerializeField] private Image timerFillImage;

    [Header("Visual Effects")]
    [SerializeField] private GameObject eventActivationVFX;
    [SerializeField] private AudioClip eventStartSound;
    [SerializeField] private AudioClip eventEndSound;
    [SerializeField] private AudioClip countdownTickSound;

    #endregion

    #region Private Fields

    private AudioSource audioSource;
    private Dictionary<ModifierType, float> modifierWeights;
    private float totalWeight;

    // Track which players were wizards before SlimeShift (to restore them after)
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

        // Setup audio
        audioSource = GetComponent<AudioSource>();
        if (audioSource == null)
            audioSource = gameObject.AddComponent<AudioSource>();

        // Setup modifier weights
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
        // Subscribe to value changes for UI updates
        EventTimer.OnValueChanged += OnTimerChanged;
        ActiveGlobalEvent.OnValueChanged += OnEventChanged;
        SizeMultiplier.OnValueChanged += OnSizeMultiplierChanged;

        // Initial UI update
        RefreshUI();

        // Server starts the event cycle
        if (IsServer)
        {
            StartCoroutine(GlobalEventCycle());
        }

        Debug.Log("[LuckyBox] Network spawned. Starting event cycle...");
    }

    public override void OnNetworkDespawn()
    {
        EventTimer.OnValueChanged -= OnTimerChanged;
        ActiveGlobalEvent.OnValueChanged -= OnEventChanged;
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
                
                // Play tick sound in last 3 seconds
                if (i <= 3)
                {
                    PlayTickSoundClientRpc();
                }
                
                yield return new WaitForSeconds(1f);
            }

            // === SELECT RANDOM EVENT ===
            ModifierType selectedEvent = SelectRandomModifier();
            
            // If SlimeShift, store current forms
            if (selectedEvent == ModifierType.SlimeShift)
            {
                StorePlayerForms();
            }
            
            // If SizeChange, randomize the multiplier
            if (selectedEvent == ModifierType.SizeChange)
            {
                SizeMultiplier.Value = Random.Range(minSizeMultiplier, maxSizeMultiplier);
            }

            // Activate the event
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

        return ModifierType.SpeedBoost; // Fallback
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
                    ApplySizeChange(playerObj, activate);
                    break;
                    
                // SpeedBoost and ReverseControls are handled in PlayerController
                // by reading ActiveGlobalEvent value
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
            // Transform to slime
            slime.TransformToSlime();
        }
        else
        {
            // Restore previous form (or default to wizard)
            bool wasSlime = preSlimeShiftForms.ContainsKey(clientId) && preSlimeShiftForms[clientId];
            
            if (!wasSlime)
            {
                slime.TransformToWizard();
            }
            // If they were already slime, they stay slime
        }
    }

    private void ApplySizeChange(GameObject playerObj, bool activate)
    {
        // Size change is read from SizeMultiplier NetworkVariable
        // PlayerController/SlimeController should read this value
        
        // Optional: Directly scale the visual here
        SlimeController slime = playerObj.GetComponent<SlimeController>();
        ProceduralCharacterAnimator animator = playerObj.GetComponent<ProceduralCharacterAnimator>();
        
        // The actual scaling is handled by the player reading SizeMultiplier
        // This method just logs
        if (activate)
        {
            Debug.Log($"[LuckyBox] Size change activated: {SizeMultiplier.Value}x");
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
            // Countdown to next event
            eventDisplayText.text = $"Next Event In: {timer}s";
            eventDisplayText.color = Color.white;
            
            if (eventBackgroundImage != null)
                eventBackgroundImage.color = new Color(0, 0, 0, 0.5f);
        }
        else
        {
            // Event is active
            string eventName = GetEventDisplayName(currentEvent);
            string extra = currentEvent == ModifierType.SizeChange 
                ? $" ({SizeMultiplier.Value:F1}x)" 
                : "";
            
            eventDisplayText.text = $"EVENT: {eventName}{extra} ({timer}s)";
            eventDisplayText.color = GetEventColor(currentEvent);
            
            if (eventBackgroundImage != null)
                eventBackgroundImage.color = GetEventColor(currentEvent) * 0.3f;
        }

        // Update timer fill
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
            ModifierType.ReverseControls => "🔄 REVERSE CONTROLS!",
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

    #region Client RPCs for Audio/VFX

    [ClientRpc]
    private void PlayEventStartClientRpc(ModifierType eventType)
    {
        // Play activation VFX
        if (eventActivationVFX != null)
        {
            // Spawn at center of arena or at each player
            Instantiate(eventActivationVFX, Vector3.zero, Quaternion.identity);
        }

        // Play sound
        if (eventStartSound != null && audioSource != null)
        {
            audioSource.PlayOneShot(eventStartSound);
        }

        Debug.Log($"[LuckyBox] Client received event start: {eventType}");
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

    /// <summary>
    /// Get the current speed multiplier from the global event
    /// </summary>
    public static float GetGlobalSpeedMultiplier()
    {
        return ActiveGlobalEvent.Value == ModifierType.SpeedBoost ? 2.0f : 1.0f;
    }

    /// <summary>
    /// Check if controls should be reversed
    /// </summary>
    public static bool AreControlsReversed()
    {
        return ActiveGlobalEvent.Value == ModifierType.ReverseControls;
    }

    /// <summary>
    /// Check if everyone is in forced slime form
    /// </summary>
    public static bool IsSlimeShiftActive()
    {
        return ActiveGlobalEvent.Value == ModifierType.SlimeShift;
    }

    /// <summary>
    /// Get the current size multiplier (for SizeChange event)
    /// </summary>
    public static float GetSizeMultiplier()
    {
        if (ActiveGlobalEvent.Value == ModifierType.SizeChange)
            return SizeMultiplier.Value;
        return 1.0f;
    }

    /// <summary>
    /// Force a specific event (for testing/debugging)
    /// Server-only
    /// </summary>
    public void ForceEvent(ModifierType eventType)
    {
        if (!IsServer)
        {
            Debug.LogWarning("[LuckyBox] ForceEvent can only be called on server!");
            return;
        }

        StopAllCoroutines();
        ActiveGlobalEvent.Value = eventType;
        
        if (eventType == ModifierType.SlimeShift)
            StorePlayerForms();
        
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
        ActiveGlobalEvent.Value = ModifierType.None;
        StartCoroutine(GlobalEventCycle());
    }

    #endregion
}