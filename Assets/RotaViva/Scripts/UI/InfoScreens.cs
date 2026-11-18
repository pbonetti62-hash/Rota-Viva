using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// ROTA VIVA - Telas de informacao (tutorial / explicacao antes da fase).
///
/// AS TELAS SAO FEITAS POR VOCE (nada e gerado automaticamente). Este script so liga tudo:
///
/// MONTAGEM:
///  1. No seu Canvas, crie um PAINEL para cada tela (texto, imagens, o que quiser).
///  2. Em cada painel, crie os botoes: Proxima e (a partir da 2a tela) Voltar.
///     Na ULTIMA tela, crie o botao "Entrar na fase".
///  3. Coloque este script em qualquer objeto (pode ser o proprio Canvas).
///  4. Em "Pages", aperte + para cada tela e arraste: o painel, o botao Proxima, o botao Voltar
///     e os sons de cada botao.
///  5. Arraste o botao "Entrar na fase" em "Start Button" e (opcional) a cena da fase em "Level Scene".
///
/// IMPORTANTE:
///  - NAO ligue nada no OnClick desses botoes. O script faz isso sozinho.
///  - A ordem da lista Pages e a ordem das telas.
///  - Na ultima tela o botao Proxima nao e necessario (use o Start Button).
///  - Precisa de um EventSystem na cena (a Unity cria junto com o Canvas).
///  - A cena da fase precisa estar em File > Build Settings.
/// </summary>
public class InfoScreens : MonoBehaviour
{
    /// <summary>Uma tela: o painel, os dois botoes de navegacao e o som de cada um.</summary>
    [Serializable]
    public class Page
    {
        [Tooltip("So um nome para voce se organizar (aparece na lista).")]
        public string title = "Tela";

        [Tooltip("O painel desta tela (GameObject com textos, imagens...).")]
        public GameObject panel;

        [Tooltip("Botao PROXIMA desta tela. Vazio na ultima tela (use o Start Button).")]
        public Button nextButton;
        [Tooltip("Som do botao Proxima. Vazio = usa o Som Padrao.")]
        public AudioClip nextSound;

        [Tooltip("Botao VOLTAR desta tela (leva para a tela anterior). Vazio na primeira tela.")]
        public Button backButton;
        [Tooltip("Som do botao Voltar. Vazio = usa o Som Padrao.")]
        public AudioClip backSound;
    }

    [Serializable]
    public class IntEvent : UnityEvent<int> { }

    [Header("1) TELAS (aperte + para adicionar quantas quiser, na ordem)")]
    public Page[] pages;

    [Header("2) BOTAO ENTRAR NA FASE (fica na ultima tela)")]
    public Button startButton;
    [Tooltip("Som do botao Entrar. Vazio = usa o Som Padrao.")]
    public AudioClip startSound;
    [Tooltip("Ligado: o Start Button so aparece na ultima tela (util se ele estiver fora dos paineis).")]
    public bool showStartButtonOnlyOnLastPage = true;
#if UNITY_EDITOR
    [Tooltip("Cena da fase que abre ao apertar Entrar. Vazio = so fecha as telas e continua na cena atual.")]
    [SerializeField] private SceneAsset levelScene;
#endif
    [SerializeField, HideInInspector] private string levelSceneName;

    [Header("3) Som")]
    [Tooltip("Som usado por qualquer botao que nao tenha um som proprio.")]
    public AudioClip defaultClickSound;
    [Range(0f, 1f)] public float soundVolume = 1f;
    [Tooltip("Opcional. Se vazio, o script cria um AudioSource sozinho.")]
    public AudioSource audioSource;

    [Header("4) Opcoes")]
    [Tooltip("Opcional: objeto que guarda todas as telas (ex.: o Canvas). E desligado quando as telas fecham.")]
    public GameObject infoRoot;
    [Tooltip("Ligado: as telas aparecem sozinhas quando a cena abre.")]
    public bool showOnStart = true;
    [Tooltip("Ligado: o jogo fica pausado enquanto as telas estao abertas (para telas dentro da propria fase).")]
    public bool pauseGameWhileOpen = false;

    [Header("Eventos")]
    [Tooltip("Dispara toda vez que muda de tela. Recebe o numero da tela (comeca em 0).")]
    public IntEvent onPageChanged;
    [Tooltip("Dispara ao apertar Entrar na fase (ligue o inicio do timer, por exemplo).")]
    public UnityEvent onStart;

    /// <summary>Numero da tela aberta agora (comeca em 0).</summary>
    public int CurrentPage => currentIndex;

    private int currentIndex = -1;
    private bool entering;
    private bool pausedByUs;
    private float savedTimeScale = 1f;

    // ------------------------------------------------------------------
    //  UNITY
    // ------------------------------------------------------------------

    private void Awake()
    {
        if (pages == null || pages.Length == 0)
        {
            Debug.LogWarning("InfoScreens: a lista Pages esta vazia. Adicione as telas no Inspector.", this);
            pages = new Page[0];
        }

        if (audioSource == null)
        {
            audioSource = GetComponent<AudioSource>();
            if (audioSource == null) audioSource = gameObject.AddComponent<AudioSource>();
        }
        audioSource.playOnAwake = false;
        audioSource.loop = false;
        audioSource.spatialBlend = 0f;

        HookButtons();
        HidePanels();
        WarnIfNoWayForward();
    }

    private void Start()
    {
        if (showOnStart) Open();
        else HideAll();
    }

    private void OnDestroy()
    {
        if (pausedByUs) Time.timeScale = 1f;
    }

    private void OnValidate()
    {
#if UNITY_EDITOR
        levelSceneName = levelScene != null ? levelScene.name : "";
#endif
    }

    // ------------------------------------------------------------------
    //  API PUBLICA (tambem pode ligar em UnityEvents, ex.: um botao "?" de ajuda)
    // ------------------------------------------------------------------

    /// <summary>Abre as telas desde a primeira.</summary>
    public void Open()
    {
        if (pages.Length == 0) return;

        entering = false;
        if (infoRoot != null) infoRoot.SetActive(true);

        if (pauseGameWhileOpen && !pausedByUs)
        {
            savedTimeScale = Time.timeScale > 0f ? Time.timeScale : 1f;
            Time.timeScale = 0f;
            pausedByUs = true;
        }

        GoToPage(0);
    }

    /// <summary>Vai para a proxima tela (na ultima, entra na fase).</summary>
    public void Next() => OnNextClicked(currentIndex);

    /// <summary>Volta para a tela anterior.</summary>
    public void Back() => OnBackClicked(currentIndex);

    /// <summary>Vai direto para uma tela (0 = primeira).</summary>
    public void GoToPage(int index)
    {
        if (pages.Length == 0) return;
        index = Mathf.Clamp(index, 0, pages.Length - 1);

        for (int i = 0; i < pages.Length; i++)
        {
            if (pages[i] != null && pages[i].panel != null)
                pages[i].panel.SetActive(i == index);
        }

        currentIndex = index;

        if (startButton != null && showStartButtonOnlyOnLastPage)
            startButton.gameObject.SetActive(index == pages.Length - 1);

        onPageChanged?.Invoke(index);
    }

    /// <summary>Entra na fase (mesma coisa que apertar o Start Button).</summary>
    public void EnterLevel()
    {
        EnterLevel(startSound);
    }

    // ------------------------------------------------------------------
    //  BOTOES
    // ------------------------------------------------------------------

    private void HookButtons()
    {
        for (int i = 0; i < pages.Length; i++)
        {
            int index = i; // copia para o clique lembrar de qual tela e
            Page page = pages[i];
            if (page == null) continue;

            if (page.nextButton != null) page.nextButton.onClick.AddListener(() => OnNextClicked(index));
            if (page.backButton != null) page.backButton.onClick.AddListener(() => OnBackClicked(index));
        }

        if (startButton != null) startButton.onClick.AddListener(() => EnterLevel(startSound));
    }

    private void OnNextClicked(int index)
    {
        if (entering || index != currentIndex || index < 0) return;

        // Na ultima tela, o botao Proxima funciona como "Entrar na fase"
        if (index >= pages.Length - 1)
        {
            EnterLevel(pages[index].nextSound != null ? pages[index].nextSound : startSound);
            return;
        }

        Play(pages[index].nextSound);
        GoToPage(index + 1);
    }

    private void OnBackClicked(int index)
    {
        if (entering || index != currentIndex || index <= 0) return;

        Play(pages[index].backSound);
        GoToPage(index - 1);
    }

    // ------------------------------------------------------------------
    //  ENTRAR NA FASE
    // ------------------------------------------------------------------

    private void EnterLevel(AudioClip sound)
    {
        if (entering) return;
        entering = true;
        StartCoroutine(EnterRoutine(sound));
    }

    private IEnumerator EnterRoutine(AudioClip sound)
    {
        AudioClip clip = sound != null ? sound : defaultClickSound;
        Play(clip);

        // Espera o som terminar (maximo 1,5 s) para ele nao ser cortado
        if (clip != null) yield return new WaitForSecondsRealtime(Mathf.Min(clip.length, 1.5f));

        RestoreTime();
        onStart?.Invoke();

        if (!string.IsNullOrEmpty(levelSceneName))
        {
            if (!Application.CanStreamedLevelBeLoaded(levelSceneName))
            {
                Debug.LogError("InfoScreens: a cena '" + levelSceneName +
                               "' nao esta no Build Settings. Abra a cena e use File > Build Settings > Add Open Scenes.", this);
                entering = false;
                yield break;
            }

            SceneManager.LoadScene(levelSceneName);
            yield break;
        }

        HideAll();
        entering = false;
    }

    // ------------------------------------------------------------------
    //  AUXILIARES
    // ------------------------------------------------------------------

    private void Play(AudioClip specific)
    {
        AudioClip clip = specific != null ? specific : defaultClickSound;
        if (clip == null || audioSource == null) return;
        audioSource.PlayOneShot(clip, soundVolume);
    }

    private void RestoreTime()
    {
        if (!pausedByUs) return;
        Time.timeScale = savedTimeScale;
        pausedByUs = false;
    }

    private void HidePanels()
    {
        foreach (Page page in pages)
        {
            if (page != null && page.panel != null) page.panel.SetActive(false);
        }
    }

    private void HideAll()
    {
        HidePanels();
        if (infoRoot != null) infoRoot.SetActive(false);
    }

    private void WarnIfNoWayForward()
    {
        if (pages.Length == 0) return;

        Page last = pages[pages.Length - 1];
        if (startButton == null && (last == null || last.nextButton == null))
        {
            Debug.LogWarning("InfoScreens: a ultima tela nao tem como avancar. " +
                             "Arraste o botao 'Entrar na fase' em Start Button.", this);
        }
    }
}
