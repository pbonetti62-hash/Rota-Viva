using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// ROTA VIVA - Tocador de musica persistente por lista de cenas.
/// Mantem a musica tocando continuamente nas cenas escolhidas e pausa/retoma
/// exatamente de onde parou ao entrar e sair de outras cenas.
/// </summary>
[RequireComponent(typeof(AudioSource))]
public class PersistentMusicPlayer : MonoBehaviour
{
    private static readonly Dictionary<AudioClip, PersistentMusicPlayer> instances = new Dictionary<AudioClip, PersistentMusicPlayer>();

    [Header("1) Musica")]
    [Tooltip("Arraste aqui o arquivo de musica.")]
    public AudioClip musicClip;
    [Range(0f, 1f)] public float volume = 0.5f;

    [Header("2) Cenas onde esta musica deve tocar")]
#if UNITY_EDITOR
    [Tooltip("Arraste aqui as cenas (do seu Project) onde esta musica deve tocar.")]
    [SerializeField] private List<SceneAsset> allowedScenes = new List<SceneAsset>();
#endif
    [SerializeField, HideInInspector] private List<string> allowedSceneNames = new List<string>();

    [Header("3) Opcoes Extras")]
    [Tooltip("Se ligado, quando entrar numa cena fora da lista ele PAUSA (e depois continua de onde parou). Se desligado, apenas muta mas a musica continua rodando no fundo.")]
    public bool pauseWhenOutsideScenes = true;

    private AudioSource audioSource;
    private float savedPlaybackTime = 0f;
    private bool wasPaused = false;

    // ------------------------------------------------------------------
    //  UNITY
    // ------------------------------------------------------------------

    private void Awake()
    {
        if (musicClip == null)
        {
            Debug.LogWarning("PersistentMusicPlayer: nenhuma musica foi anexada!", this);
            Destroy(gameObject);
            return;
        }

        // Garante que nao duplique a mesma musica se voce voltar para a cena inicial
        if (instances.TryGetValue(musicClip, out PersistentMusicPlayer existing) && existing != null && existing != this)
        {
            Destroy(gameObject);
            return;
        }

        instances[musicClip] = this;
        DontDestroyOnLoad(gameObject);

        audioSource = GetComponent<AudioSource>();
        audioSource.clip = musicClip;
        audioSource.loop = true;
        audioSource.playOnAwake = false;
        audioSource.volume = volume;

        SceneManager.sceneLoaded += OnSceneLoaded;

        // Verifica imediatamente a cena atual ao iniciar
        EvaluateScene(SceneManager.GetActiveScene().name);
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        if (musicClip != null && instances.TryGetValue(musicClip, out PersistentMusicPlayer current) && current == this)
        {
            instances.Remove(musicClip);
        }
    }

    private void OnValidate()
    {
#if UNITY_EDITOR
        allowedSceneNames.Clear();
        if (allowedScenes != null)
        {
            foreach (SceneAsset scene in allowedScenes)
            {
                if (scene != null && !allowedSceneNames.Contains(scene.name))
                {
                    allowedSceneNames.Add(scene.name);
                }
            }
        }
#endif
        if (audioSource != null)
        {
            audioSource.volume = volume;
        }
    }

    // ------------------------------------------------------------------
    //  CONTROLE DE CENAS E POSICAO DA MUSICA
    // ------------------------------------------------------------------

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        EvaluateScene(scene.name);
    }

    private void EvaluateScene(string currentSceneName)
    {
        bool shouldPlay = allowedSceneNames.Contains(currentSceneName);

        if (shouldPlay)
        {
            audioSource.volume = volume;

            if (wasPaused)
            {
                audioSource.time = Mathf.Clamp(savedPlaybackTime, 0f, musicClip.length - 0.01f);
                audioSource.UnPause();
                wasPaused = false;
            }
            else if (!audioSource.isPlaying)
            {
                audioSource.time = Mathf.Clamp(savedPlaybackTime, 0f, musicClip.length - 0.01f);
                audioSource.Play();
            }
        }
        else
        {
            if (pauseWhenOutsideScenes)
            {
                if (audioSource.isPlaying)
                {
                    savedPlaybackTime = audioSource.time;
                    audioSource.Pause();
                    wasPaused = true;
                }
            }
            else
            {
                audioSource.volume = 0f;
            }
        }
    }

    // ------------------------------------------------------------------
    //  METODOS PUBLICOS (Caso queira controlar por botao ou evento)
    // ------------------------------------------------------------------

    public void SetVolume(float newVolume)
    {
        volume = Mathf.Clamp01(newVolume);
        if (audioSource != null && allowedSceneNames.Contains(SceneManager.GetActiveScene().name))
        {
            audioSource.volume = volume;
        }
    }
}