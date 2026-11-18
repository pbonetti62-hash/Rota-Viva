using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// ROTA VIVA - Timer da fase (contagem regressiva), sons de tensao/vitoria/derrota e telas finais.
///
/// COMO USAR:
///  1. Crie um objeto vazio "Timer" na cena e coloque este script. So um por cena.
///  2. Defina o tempo em Minutes e Seconds (ex.: 0 min e 30 s -> mostra 0:30, 0:29...).
///  3. Arraste os sons de Reta Final (10s), Vitoria e Derrota no Inspector.
///  4. Para vencer: chame LevelTimer.Instance.TriggerVictory() (ou ligue no evento On Victory).
/// </summary>
public class LevelTimer : MonoBehaviour
{
    public static LevelTimer Instance { get; private set; }

    [Header("1) TEMPO (contagem regressiva)")]
    [Min(0)] public int minutes = 0;
    [Range(0, 59)] public int seconds = 30;
    [Tooltip("Ligado: o relogio comeca a correr quando a cena abre.")]
    public bool startOnAwake = true;

    [Header("2) Visual do timer (criado automaticamente no topo, centralizado)")]
    public bool buildTimerUI = true;
    [Min(20)] public int timerFontSize = 64;
    public Color normalColor = Color.white;
    public Color warningColor = new Color(1f, 0.25f, 0.30f, 1f);
    [Tooltip("Nos ultimos X segundos o relogio fica vermelho, pulsa e toca os ponteiros tensos. 0 = desliga.")]
    [Min(0)] public int warningSeconds = 10;
    [Tooltip("Distancia do topo da tela (pixels).")]
    public float topMargin = 24f;
    [Tooltip("Opcional. Se vazio, usa a fonte padrao da Unity.")]
    public Font font;

    [Header("3) Sons (Reta Final, Vitoria e Derrota)")]
    [Tooltip("AudioSource para efeitos (se vazio, cria um sozinho).")]
    public AudioSource sfxSource;
    [Tooltip("Som de ponteiros tensos / relogio nos ultimos segundos (toca em loop ate acabar ou vencer).")]
    public AudioClip finalStretchTickSound;
    [Tooltip("Som de VITORIA ao completar a fase.")]
    public AudioClip victorySound;
    [Tooltip("Som de DERROTA quando o tempo acaba.")]
    public AudioClip defeatSound;
    [Range(0f, 1f)] public float sfxVolume = 0.85f;

    [Header("4) Tela de Vitoria (opcional)")]
    [Tooltip("Se ligado, cria e exibe a tela de vitoria ao chamar TriggerVictory().")]
    public bool buildVictoryUI = true;
    public string victoryTitle = "Você chegou!";
    [Tooltip("Segundos na tela de vitoria antes de abrir a proxima cena (se houver).")]
    [Min(0f)] public float nextSceneDelay = 3f;
#if UNITY_EDITOR
    [Tooltip("Opcional: arraste a PROXIMA CENA aqui. Vazio = nao troca de cena automaticamente.")]
    [SerializeField] private SceneAsset nextScene;
#endif
    [SerializeField, HideInInspector] private string nextSceneName;

    [Header("5) Game Over / Derrota (criado automaticamente)")]
    public string gameOverTitle = "Tempo esgotado!";
    public string restartLabel = "Reiniciar";
    public string exitLabel = "Sair";
#if UNITY_EDITOR
    [Tooltip("Arraste a cena do MENU aqui (botao Sair). Vazio = abre a primeira cena do Build Settings.")]
    [SerializeField] private SceneAsset menuScene;
#endif
    [SerializeField, HideInInspector] private string menuSceneName;

    [Header("Eventos")]
    [Tooltip("Dispara quando entra nos ultimos segundos (reta final).")]
    public UnityEvent onWarningStarted;
    [Tooltip("Dispara quando vence a fase.")]
    public UnityEvent onVictory;
    [Tooltip("Dispara quando o tempo acaba (antes da tela de Game Over).")]
    public UnityEvent onTimeUp;

    /// <summary>Tempo restante em segundos.</summary>
    public float TimeLeft { get; private set; }
    /// <summary>O relogio esta correndo?</summary>
    public bool IsRunning => running;
    /// <summary>A fase ja terminou (vitoria ou derrota)?</summary>
    public bool IsFinished => timeIsUp || hasWon;

    private Text timerText;
    private RectTransform timerRect;
    private GameObject gameOverCanvas;
    private GameObject victoryCanvas;
    private AudioSource tickLoopSource;

    private bool running;
    private bool timeIsUp;
    private bool hasWon;
    private bool inWarning;
    private bool warningSoundStarted;
    private int lastShownSeconds = -1;

    // ------------------------------------------------------------------
    //  UNITY
    // ------------------------------------------------------------------

    private void Awake()
    {
        Instance = this;
        Time.timeScale = 1f;

        TimeLeft = TotalSeconds();
        SetupAudioSources();

        if (buildTimerUI) BuildTimerUI();
        if (buildVictoryUI) BuildVictoryUI();
        BuildGameOverUI();

        RefreshDisplay();
        running = startOnAwake;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        Time.timeScale = 1f;
    }

    private void OnValidate()
    {
        if (minutes == 0 && seconds == 0) seconds = 1;
#if UNITY_EDITOR
        menuSceneName = menuScene != null ? menuScene.name : "";
        nextSceneName = nextScene != null ? nextScene.name : "";
#endif
    }

    private void SetupAudioSources()
    {
        if (sfxSource == null)
        {
            sfxSource = GetComponent<AudioSource>();
            if (sfxSource == null) sfxSource = gameObject.AddComponent<AudioSource>();
        }
        sfxSource.playOnAwake = false;

        // AudioSource exclusivo para o som de ponteiros tensos em loop
        tickLoopSource = gameObject.AddComponent<AudioSource>();
        tickLoopSource.playOnAwake = false;
        tickLoopSource.loop = true;
    }

    private void Update()
    {
        if (running && !IsFinished)
        {
            TimeLeft -= Time.deltaTime;

            if (TimeLeft <= 0f)
            {
                TimeLeft = 0f;
                RefreshDisplay();
                TimeUp();
                return;
            }

            RefreshDisplay();
        }

        // Pulsar nos ultimos segundos
        if (timerRect != null)
        {
            float pulse = (running && inWarning) ? 1f + 0.07f * Mathf.Sin(Time.unscaledTime * 8f) : 1f;
            timerRect.localScale = new Vector3(pulse, pulse, 1f);
        }
    }

    // ------------------------------------------------------------------
    //  API PUBLICA
    // ------------------------------------------------------------------

    /// <summary>Comeca ou retoma a contagem.</summary>
    public void StartTimer()
    {
        if (!IsFinished) running = true;
    }

    /// <summary>Para apenas a contagem e o som de tique-taque.</summary>
    public void StopTimer()
    {
        running = false;
        StopTickSound();
    }

    /// <summary>Chame ao chegar no objetivo / concluir a rota certa: para o timer, toca som de vitoria e abre tela.</summary>
    public void TriggerVictory()
    {
        if (IsFinished) return;
        hasWon = true;
        running = false;

        StopTickSound();
        PlaySFX(victorySound);

        if (victoryCanvas != null) victoryCanvas.SetActive(true);
        onVictory?.Invoke();

        if (!string.IsNullOrEmpty(nextSceneName))
        {
            StartCoroutine(LoadNextSceneRoutine());
        }
    }

    /// <summary>Volta ao tempo inicial.</summary>
    public void ResetTimer()
    {
        TimeLeft = TotalSeconds();
        timeIsUp = false;
        hasWon = false;
        inWarning = false;
        warningSoundStarted = false;
        lastShownSeconds = -1;
        Time.timeScale = 1f;

        StopTickSound();
        if (gameOverCanvas != null) gameOverCanvas.SetActive(false);
        if (victoryCanvas != null) victoryCanvas.SetActive(false);
        RefreshDisplay();
    }

    /// <summary>Soma (ou subtrai, com valor negativo) segundos. Ex.: bonus ou penalidade.</summary>
    public void AddSeconds(float amount)
    {
        if (IsFinished) return;
        TimeLeft = Mathf.Max(0.01f, TimeLeft + amount);
        RefreshDisplay();
    }

    /// <summary>Botao Reiniciar: recarrega a fase atual.</summary>
    public void RestartLevel()
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    /// <summary>Botao Sair: volta para o menu.</summary>
    public void ExitToMenu()
    {
        Time.timeScale = 1f;

        if (string.IsNullOrEmpty(menuSceneName))
        {
            Debug.LogWarning("LevelTimer: nenhuma cena de menu definida. Abrindo a primeira cena do Build Settings.", this);
            SceneManager.LoadScene(0);
            return;
        }

        if (!Application.CanStreamedLevelBeLoaded(menuSceneName))
        {
            Debug.LogError("LevelTimer: a cena '" + menuSceneName +
                           "' nao esta no Build Settings. Abra a cena e use File > Build Settings > Add Open Scenes.", this);
            return;
        }

        SceneManager.LoadScene(menuSceneName);
    }

    // ------------------------------------------------------------------
    //  LOGICA E SOM
    // ------------------------------------------------------------------

    private float TotalSeconds()
    {
        return Mathf.Max(1, minutes * 60 + seconds);
    }

    private void TimeUp()
    {
        if (IsFinished) return;
        running = false;
        timeIsUp = true;

        StopTickSound();
        PlaySFX(defeatSound);

        onTimeUp?.Invoke();

        Time.timeScale = 0f; // pausa o jogo
        if (gameOverCanvas != null) gameOverCanvas.SetActive(true);
    }

    private void RefreshDisplay()
    {
        int total = Mathf.CeilToInt(TimeLeft);
        if (total == lastShownSeconds) return;
        lastShownSeconds = total;

        inWarning = warningSeconds > 0 && total <= warningSeconds && total > 0;

        // Inicia o som de ponteiros tensos ao entrar na reta final
        if (inWarning && !warningSoundStarted && running)
        {
            warningSoundStarted = true;
            StartTickSound();
            onWarningStarted?.Invoke();
        }
        else if (!inWarning && warningSoundStarted)
        {
            // Caso o jogador ganhe bonus de tempo e saia da zona de perigo
            warningSoundStarted = false;
            StopTickSound();
        }

        if (timerText != null)
        {
            timerText.text = (total / 60) + ":" + (total % 60).ToString("00");
            timerText.color = (warningSeconds > 0 && total <= warningSeconds) ? warningColor : normalColor;
        }
    }

    private void StartTickSound()
    {
        if (finalStretchTickSound == null || tickLoopSource == null) return;
        tickLoopSource.clip = finalStretchTickSound;
        tickLoopSource.volume = sfxVolume;
        tickLoopSource.Play();
    }

    private void StopTickSound()
    {
        if (tickLoopSource != null && tickLoopSource.isPlaying)
        {
            tickLoopSource.Stop();
        }
    }

    private void PlaySFX(AudioClip clip)
    {
        if (clip != null && sfxSource != null)
        {
            sfxSource.PlayOneShot(clip, sfxVolume);
        }
    }

    private IEnumerator LoadNextSceneRoutine()
    {
        yield return new WaitForSecondsRealtime(nextSceneDelay);

        if (!Application.CanStreamedLevelBeLoaded(nextSceneName))
        {
            Debug.LogError("LevelTimer: a cena '" + nextSceneName +
                           "' nao esta no Build Settings. Abra a cena e use File > Build Settings > Add Open Scenes.", this);
            yield break;
        }

        SceneManager.LoadScene(nextSceneName);
    }

    // ------------------------------------------------------------------
    //  INTERFACE AUTOMATICA
    // ------------------------------------------------------------------

    private void BuildTimerUI()
    {
        Canvas canvas = CreateCanvas("Timer_Canvas", 90);

        GameObject panel = new GameObject("Painel_Timer", typeof(RectTransform), typeof(Image));
        panel.transform.SetParent(canvas.transform, false);
        panel.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.55f);
        panel.GetComponent<Image>().raycastTarget = false;

        timerRect = panel.GetComponent<RectTransform>();
        timerRect.anchorMin = timerRect.anchorMax = timerRect.pivot = new Vector2(0.5f, 1f);
        timerRect.sizeDelta = new Vector2(320f, 110f);
        timerRect.anchoredPosition = new Vector2(0f, -topMargin);

        GameObject textGO = new GameObject("Texto_Timer", typeof(RectTransform), typeof(Text));
        textGO.transform.SetParent(panel.transform, false);

        timerText = textGO.GetComponent<Text>();
        timerText.font = GetFont();
        timerText.fontSize = timerFontSize;
        timerText.fontStyle = FontStyle.Bold;
        timerText.alignment = TextAnchor.MiddleCenter;
        timerText.color = normalColor;
        timerText.raycastTarget = false;

        RectTransform tr = textGO.GetComponent<RectTransform>();
        tr.anchorMin = Vector2.zero;
        tr.anchorMax = Vector2.one;
        tr.offsetMin = Vector2.zero;
        tr.offsetMax = Vector2.zero;
    }

    private void BuildVictoryUI()
    {
        Canvas canvas = CreateCanvas("Vitoria_Canvas", 115);
        victoryCanvas = canvas.gameObject;

        GameObject bg = new GameObject("Fundo", typeof(RectTransform), typeof(Image));
        bg.transform.SetParent(canvas.transform, false);
        bg.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.78f);
        RectTransform bgRect = bg.GetComponent<RectTransform>();
        bgRect.anchorMin = Vector2.zero;
        bgRect.anchorMax = Vector2.one;
        bgRect.offsetMin = Vector2.zero;
        bgRect.offsetMax = Vector2.zero;

        CreateText(bg.transform, victoryTitle, 96, new Vector2(1500f, 220f), Vector2.zero);
        victoryCanvas.SetActive(false);
    }

    private void BuildGameOverUI()
    {
        EnsureEventSystem();

        Canvas canvas = CreateCanvas("GameOver_Canvas", 120);
        gameOverCanvas = canvas.gameObject;

        GameObject bg = new GameObject("Fundo", typeof(RectTransform), typeof(Image));
        bg.transform.SetParent(canvas.transform, false);
        bg.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.82f);
        RectTransform bgRect = bg.GetComponent<RectTransform>();
        bgRect.anchorMin = Vector2.zero;
        bgRect.anchorMax = Vector2.one;
        bgRect.offsetMin = Vector2.zero;
        bgRect.offsetMax = Vector2.zero;

        CreateText(bg.transform, gameOverTitle, 100, new Vector2(1500f, 200f), new Vector2(0f, 150f));
        CreateButton(bg.transform, restartLabel, new Vector2(-210f, -80f), new Color(0.95f, 0.10f, 0.50f, 1f), RestartLevel);
        CreateButton(bg.transform, exitLabel, new Vector2(210f, -80f), new Color(0.25f, 0.27f, 0.35f, 1f), ExitToMenu);

        gameOverCanvas.SetActive(false);
    }

    private static void EnsureEventSystem()
    {
#if UNITY_2023_1_OR_NEWER
        if (FindFirstObjectByType<EventSystem>() != null) return;
#else
        if (FindObjectOfType<EventSystem>() != null) return;
#endif
        GameObject es = new GameObject("EventSystem", typeof(EventSystem));
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
        es.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
#else
        es.AddComponent<StandaloneInputModule>();
#endif
    }

    private static Canvas CreateCanvas(string name, int sortOrder)
    {
        GameObject go = new GameObject(name, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));

        Canvas canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = sortOrder;

        CanvasScaler scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        return canvas;
    }

    private Text CreateText(Transform parent, string content, int fontSize, Vector2 size, Vector2 position)
    {
        GameObject go = new GameObject("Texto", typeof(RectTransform), typeof(Text));
        go.transform.SetParent(parent, false);

        Text t = go.GetComponent<Text>();
        t.text = content;
        t.font = GetFont();
        t.fontSize = fontSize;
        t.fontStyle = FontStyle.Bold;
        t.alignment = TextAnchor.MiddleCenter;
        t.color = Color.white;
        t.raycastTarget = false;

        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = size;
        rt.anchoredPosition = position;
        return t;
    }

    private void CreateButton(Transform parent, string label, Vector2 position, Color color, UnityAction onClick)
    {
        GameObject go = new GameObject("Botao_" + label, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);

        Image img = go.GetComponent<Image>();
        img.color = color;

        Button button = go.GetComponent<Button>();
        button.targetGraphic = img;
        button.onClick.AddListener(onClick);

        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(340f, 120f);
        rt.anchoredPosition = position;

        CreateText(go.transform, label, 46, new Vector2(340f, 120f), Vector2.zero);
    }

    private Font GetFont()
    {
        if (font != null) return font;
#if UNITY_2022_2_OR_NEWER
        return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
#else
        return Resources.GetBuiltinResource<Font>("Arial.ttf");
#endif
    }
}