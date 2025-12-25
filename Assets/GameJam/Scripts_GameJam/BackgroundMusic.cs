using UnityEngine;
using UnityEngine.SceneManagement;

[RequireComponent(typeof(AudioSource))]
public class BackgroundMusic : MonoBehaviour
{
    [SerializeField] private AudioClip musicClip;
    [Range(0f, 1f)]
    [SerializeField] private float volume = 0.6f;
    [SerializeField] private bool playOnAwake = true;
    [SerializeField] private bool loop = true;
    [SerializeField] private bool persistAcrossScenes = false;
    [SerializeField] private string[] stopOnScenes;

    private AudioSource audioSource;
    private static BackgroundMusic persistentInstance;

    private void Awake()
    {
        audioSource = GetComponent<AudioSource>();
        audioSource.playOnAwake = false;
        audioSource.loop = loop;
        audioSource.volume = volume;
        audioSource.spatialBlend = 0f;
        audioSource.clip = musicClip;

        if (persistAcrossScenes)
        {
            if (persistentInstance != null && persistentInstance != this)
            {
                Destroy(gameObject);
                return;
            }

            persistentInstance = this;
            DontDestroyOnLoad(gameObject);
        }
    }

    private void OnEnable()
    {
        if (persistAcrossScenes)
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
        }
    }

    private void OnDisable()
    {
        if (persistAcrossScenes)
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }
    }

    private void Start()
    {
        if (playOnAwake && musicClip != null && !audioSource.isPlaying)
        {
            audioSource.Play();
        }
    }

    private void OnValidate()
    {
        if (audioSource == null) audioSource = GetComponent<AudioSource>();
        if (audioSource == null) return;

        audioSource.loop = loop;
        audioSource.volume = volume;
        audioSource.spatialBlend = 0f;
        audioSource.clip = musicClip;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (stopOnScenes == null || stopOnScenes.Length == 0) return;
        for (int i = 0; i < stopOnScenes.Length; i++)
        {
            if (scene.name == stopOnScenes[i])
            {
                Destroy(gameObject);
                return;
            }
        }
    }
}
