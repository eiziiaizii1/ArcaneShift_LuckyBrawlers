using Unity.Netcode;
using UnityEngine;
using System.Collections;

/// <summary>
/// SlimeController handles the Slime form's unique mechanics.
/// 
/// IMPORTANT: This script coordinates with ProceduralCharacterAnimator for visual changes.
/// It does NOT directly toggle visual GameObjects to avoid conflicts with Health.cs death handling.
/// </summary>
public class SlimeController : NetworkBehaviour
{
    #region Network Variables
    
    /// <summary>
    /// Current scale of the slime (1.0 = full size, 0.3 = minimum)
    /// </summary>
    public NetworkVariable<float> currentScale = new NetworkVariable<float>(
        1.0f,
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

    #endregion

    #region Inspector Fields

    [Header("Adaptive Scaling Settings")]
    [SerializeField] private float minScale = 0.3f;
    [SerializeField] private float maxScale = 1.0f;
    [SerializeField] private float scalePerDamage = 0.007f;
    [SerializeField] private float maxSpeedMultiplier = 2.0f;
    [SerializeField] private float minSpeedMultiplier = 1.0f;

    [Header("Gloop Projectile")]
    [SerializeField] private GameObject gloopPrefab;
    [SerializeField] private float gloopSpeed = 7f;
    [SerializeField] private int gloopDamage = 20;

    [Header("Visual Effects")]
    [SerializeField] private GameObject transformVFX;
    [SerializeField] private AudioClip transformSound;

    [Header("References")]
    [SerializeField] private ProceduralCharacterAnimator animator;

    #endregion

    #region Private Fields

    private AudioSource audioSource;

    #endregion

    #region Unity Lifecycle

    private void Awake()
    {
        if (animator == null)
            animator = GetComponent<ProceduralCharacterAnimator>();
        
        audioSource = GetComponent<AudioSource>();
        if (audioSource == null)
            audioSource = gameObject.AddComponent<AudioSource>();
    }

    public override void OnNetworkSpawn()
    {
        currentScale.OnValueChanged += OnScaleChanged;
        isSlimeForm.OnValueChanged += OnFormChanged;

        // Initial sync with animator
        SyncAnimatorForm(isSlimeForm.Value);
        
        Debug.Log($"[SlimeController] Spawned. Form: {(isSlimeForm.Value ? "Slime" : "Wizard")}, Scale: {currentScale.Value}");
    }

    public override void OnNetworkDespawn()
    {
        currentScale.OnValueChanged -= OnScaleChanged;
        isSlimeForm.OnValueChanged -= OnFormChanged;
    }

    #endregion

    #region Form Management

    /// <summary>
    /// Server-only: Transform player into slime form
    /// </summary>
    public void TransformToSlime()
    {
        if (!IsServer) return;
        
        if (isSlimeForm.Value)
        {
            // Already slime - just ensure visuals are correct
            ForceRefreshVisualsClientRpc();
            return;
        }
        
        isSlimeForm.Value = true;
        currentScale.Value = maxScale; // Reset scale on transformation
        
        PlayTransformEffectClientRpc();
        
        Debug.Log($"[SlimeController] Player {OwnerClientId} transformed to SLIME");
    }

    /// <summary>
    /// Server-only: Transform player back to wizard form
    /// </summary>
    public void TransformToWizard()
    {
        if (!IsServer) return;
        
        if (!isSlimeForm.Value)
        {
            // Already wizard - just ensure visuals are correct
            ForceRefreshVisualsClientRpc();
            return;
        }
        
        isSlimeForm.Value = false;
        currentScale.Value = maxScale;
        
        PlayTransformEffectClientRpc();
        
        Debug.Log($"[SlimeController] Player {OwnerClientId} transformed to WIZARD");
    }

    /// <summary>
    /// Server-only: Toggle between forms
    /// </summary>
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
        Debug.Log($"[SlimeController] Form changed: {(oldValue ? "Slime" : "Wizard")} -> {(newValue ? "Slime" : "Wizard")}");
        
        // Sync with animator
        SyncAnimatorForm(newValue);
        
        // Update leaderboard
        UpdateLeaderboardState();
    }

    /// <summary>
    /// Sync the animator's form state
    /// </summary>
    private void SyncAnimatorForm(bool isSlime)
    {
        if (animator != null)
        {
            // Use the animator's method to properly switch visuals
            animator.SetFormState(isSlime);
        }
        else
        {
            // Fallback: directly control visuals
            FallbackFormSwitch(isSlime);
        }
    }

    /// <summary>
    /// Fallback if no animator - directly switch visuals
    /// </summary>
    private void FallbackFormSwitch(bool isSlime)
    {
        Transform wizardVisual = transform.Find("WizardVisual");
        Transform slimeVisual = transform.Find("SlimeVisual");
        
        // Check if player is visible (not dead)
        Health health = GetComponent<Health>();
        bool isVisible = health == null || health.IsVisible;
        
        if (wizardVisual != null)
            wizardVisual.gameObject.SetActive(isVisible && !isSlime);
        
        if (slimeVisual != null)
            slimeVisual.gameObject.SetActive(isVisible && isSlime);
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

    /// <summary>
    /// Force refresh visuals on all clients (used after respawn or when ensuring correct state)
    /// </summary>
    [ClientRpc]
    private void ForceRefreshVisualsClientRpc()
    {
        SyncAnimatorForm(isSlimeForm.Value);
        Debug.Log($"[SlimeController] Force refreshed visuals. IsSlime: {isSlimeForm.Value}");
    }

    #endregion

    #region Adaptive Scaling

    public void OnDamageTaken(int damageAmount)
    {
        if (!IsServer || !isSlimeForm.Value) return;

        float scaleReduction = damageAmount * scalePerDamage;
        float newScale = Mathf.Max(minScale, currentScale.Value - scaleReduction);
        currentScale.Value = newScale;
        
        Debug.Log($"[SlimeController] Damage taken: {damageAmount}, New scale: {newScale:F2}");
    }

    public void OnDamageDealt(int damageAmount)
    {
        if (!IsServer) return;
        // Ultimate charging is handled by LaserBeamUltimate now
    }

    private void OnScaleChanged(float oldScale, float newScale)
    {
        UpdateLeaderboardState();
    }

    public float GetSpeedMultiplier()
    {
        if (!isSlimeForm.Value) return 1f;
        
        float scaleNormalized = (currentScale.Value - minScale) / (maxScale - minScale);
        float speedMultiplier = Mathf.Lerp(maxSpeedMultiplier, minSpeedMultiplier, scaleNormalized);
        
        return speedMultiplier;
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

    #region State Management

    /// <summary>
    /// Reset state (called on respawn)
    /// </summary>
    public void ResetState()
    {
        if (!IsServer) return;
        
        currentScale.Value = maxScale;
        
        // Force refresh visuals after reset
        ForceRefreshVisualsClientRpc();
        
        Debug.Log($"[SlimeController] State reset for player {OwnerClientId}");
    }

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

    #region Public Properties

    public bool IsSlime => isSlimeForm.Value;
    public float CurrentScale => currentScale.Value;

    #endregion
}