using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// ROTA VIVA - Linha de chegada (fases 2 e 3).
///
/// COMO USAR:
///  1. Crie um objeto no destino (creche, casa...) e coloque este script.
///     O Collider2D vira Trigger sozinho. Deixe-o do tamanho da area de chegada.
///  2. Arraste a PROXIMA CENA no campo "Next Scene".
///  3. Pronto. Nao precisa criar canvas: ele e criado sozinho.
///
/// O QUE FAZ:
///  Player entra na area -> player para -> espera um pouco -> aparece "Voce chegou!"
///  (sem botoes) -> espera alguns segundos -> abre a proxima cena.
///  Se existir um LevelTimer na cena, ele e parado automaticamente.
///
/// IMPORTANTE: a proxima cena precisa estar em File > Build Settings (Add Open Scenes).
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class LevelGoal : MonoBehaviour
{
    [Header("1) Ao entrar na area")]
    [Tooltip("Segundos entre encostar na chegada e aparecer a tela de vitoria.")]
    [Min(0f)] public float delayBeforeVictory = 0.5f;
    [Tooltip("Para o player assim que ele encosta.")]
    public bool freezePlayer = true;

    [Header("2) Tela de vitoria (criada automaticamente)")]
    public bool buildUIAutomatically = true;
    public string victoryText = "Você chegou!";
    [Tooltip("Opcional. Se vazio, usa a fonte padrao da Unity.")]
    public Font font;
    [Tooltip("Opcional: seu proprio canvas de vitoria (desmarque a opcao automatica).")]
    public GameObject victoryCanvas;

    [Header("3) Depois da vitoria")]
    [Tooltip("Segundos na tela de vitoria antes de abrir a proxima cena.")]
    [Min(0f)] public float nextSceneDelay = 3f;
#if UNITY_EDITOR
    [Tooltip("Arraste a PROXIMA CENA aqui. Vazio = nao troca de cena.")]
    [SerializeField] private SceneAsset nextScene;
#endif
    [SerializeField, HideInInspector] private string nextSceneName;

    [Header("Eventos (som, narrador da Cidade...)")]
    [Tooltip("Dispara no instante em que o player encosta.")]
    public UnityEvent onReached;
    [Tooltip("Dispara quando a tela de vitoria aparece.")]
    public UnityEvent onVictory;

    private bool reached;

    // ------------------------------------------------------------------

    private void Reset()
    {
        GetComponent<Collider2D>().isTrigger = true;
    }

    private void Awake()
    {
        GetComponent<Collider2D>().isTrigger = true;

        if (victoryCanvas == null && buildUIAutomatically) BuildVictoryUI();
        if (victoryCanvas != null) victoryCanvas.SetActive(false);
    }

    private void OnValidate()
    {
#if UNITY_EDITOR
        nextSceneName = nextScene != null ? nextScene.name : "";
#endif
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (reached) return;

        PlayerController2D player = other.GetComponentInParent<PlayerController2D>();
        if (player == null) return;

        reached = true;
        StartCoroutine(VictoryRoutine(player));
    }

    // ------------------------------------------------------------------

    private IEnumerator VictoryRoutine(PlayerController2D player)
    {
        if (freezePlayer) player.SetCanMove(false);
        if (LevelTimer.Instance != null) LevelTimer.Instance.StopTimer();
        onReached?.Invoke();

        yield return new WaitForSeconds(delayBeforeVictory);

        if (victoryCanvas != null) victoryCanvas.SetActive(true);
        onVictory?.Invoke();

        if (string.IsNullOrEmpty(nextSceneName)) yield break;

        yield return new WaitForSecondsRealtime(nextSceneDelay);

        if (!Application.CanStreamedLevelBeLoaded(nextSceneName))
        {
            Debug.LogError("LevelGoal: a cena '" + nextSceneName +
                           "' nao esta no Build Settings. Abra a cena e use File > Build Settings > Add Open Scenes.", this);
            yield break;
        }

        SceneManager.LoadScene(nextSceneName);
    }

    // ------------------------------------------------------------------
    //  INTERFACE AUTOMATICA
    // ------------------------------------------------------------------

    private void BuildVictoryUI()
    {
        GameObject canvasGO = new GameObject("Vitoria_Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        victoryCanvas = canvasGO;

        Canvas canvas = canvasGO.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 110;

        CanvasScaler scaler = canvasGO.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        // Fundo escuro em tela cheia
        GameObject bg = new GameObject("Fundo", typeof(RectTransform), typeof(Image));
        bg.transform.SetParent(canvasGO.transform, false);
        bg.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.75f);
        RectTransform bgRect = bg.GetComponent<RectTransform>();
        bgRect.anchorMin = Vector2.zero;
        bgRect.anchorMax = Vector2.one;
        bgRect.offsetMin = Vector2.zero;
        bgRect.offsetMax = Vector2.zero;

        // Texto central
        GameObject textGO = new GameObject("Texto", typeof(RectTransform), typeof(Text));
        textGO.transform.SetParent(bg.transform, false);

        Text text = textGO.GetComponent<Text>();
        text.text = victoryText;
        text.font = GetFont();
        text.fontSize = 96;
        text.fontStyle = FontStyle.Bold;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = Color.white;
        text.raycastTarget = false;

        RectTransform textRect = textGO.GetComponent<RectTransform>();
        textRect.anchorMin = textRect.anchorMax = textRect.pivot = new Vector2(0.5f, 0.5f);
        textRect.sizeDelta = new Vector2(1500f, 220f);
        textRect.anchoredPosition = Vector2.zero;
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
