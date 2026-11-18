using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// ROTA VIVA - GPS com 2 rotas.
///  - Rota CERTA: percorre os pontos ate o fim -> chama LevelTimer.Instance.TriggerVictory().
///  - Rota ERRADA: percorre ate o bloqueio -> toca som de erro, avisa e REABRE o mapa para escolher de novo.
/// </summary>
public class GPSRouteFollower : MonoBehaviour
{
    [Header("1) PONTOS: objeto pai de cada rota (os pontos sao os FILHOS, na ordem)")]
    [Tooltip("Pai dos pontos da ROTA 1. Se vazio, usa o 1o filho deste objeto.")]
    public Transform rota1Pontos;
    [Tooltip("Pai dos pontos da ROTA 2. Se vazio, usa o 2o filho deste objeto.")]
    public Transform rota2Pontos;

    [Header("2) Qual e a rota CERTA?   0 = Rota 1   |   1 = Rota 2")]
    [Range(0, 1)] public int correctRouteIndex = 0;

    [Header("3) Sons do GPS")]
    [Tooltip("AudioSource para efeitos do GPS (se vazio, cria um sozinho).")]
    public AudioSource sfxSource;
    [Tooltip("Som ao coletar o item / abrir o GPS.")]
    public AudioClip collectItemSound;
    [Tooltip("Som ao escolher a rota CERTA.")]
    public AudioClip routeCorrectSound;
    [Tooltip("Som ao chegar no fim da rota ERRADA (bloqueio/erro).")]
    public AudioClip routeWrongSound;
    [Range(0f, 1f)] public float sfxVolume = 0.85f;

    [Header("4) Interface do GPS (criada automaticamente)")]
    public bool buildUIAutomatically = true;
    public string chooseTitle = "Escolha sua rota";
    public string route1Label = "Rota 1";
    public string route2Label = "Rota 2";
    public string wrongRouteMessage = "Rota bloqueada! Escolha outro caminho!";
    [Tooltip("Opcional. Se vazio, usa a fonte padrao da Unity.")]
    public Font font;

    [Header("Interface PROPRIA do GPS (opcional - so se nao quiser a automatica)")]
    public GameObject gpsCanvas;
    public GameObject chooseGroup;
    public GameObject wrongWarningGroup;

    [Header("Player (opcional - se vazio, acha sozinho)")]
    public PlayerController2D player;

    [Header("Movimento automatico")]
    [Min(0.5f)] public float moveSpeed = 5f;
    [Min(0.02f)] public float arriveDistance = 0.15f;
    [Min(0f)] public float stuckTimeout = 1.5f;
    public bool flipSprite = true;

    [Header("Rota errada")]
    [Tooltip("Tempo mostrando o aviso de bloqueio antes de reabrir o mapa para escolher de novo.")]
    [Min(0f)] public float wrongRouteNoticeDelay = 1.5f;

    [Header("Rota escolhida")]
    [Tooltip("Ligado: comeca no ponto da rota escolhida mais proximo do player. Desligado: sempre no 1o ponto.")]
    public bool startFromNearestPoint = true;

    [Header("Eventos Extras (falas da Cidade, particulas...)")]
    public UnityEvent onGPSOpened;
    public UnityEvent onCorrectRouteSelected;
    public UnityEvent onWrongRouteEnded;
    public UnityEvent onRouteCompleted;

    private Rigidbody2D rb;
    private SpriteRenderer sr;
    private bool gpsOpen;
    private bool running;
    private Coroutine activeRouteCoroutine;

    private bool IsLevelOver => LevelTimer.Instance != null && LevelTimer.Instance.IsFinished;

    // ------------------------------------------------------------------
    //  SETUP
    // ------------------------------------------------------------------

    private void Reset()
    {
        AssignRootsFromChildren();
    }

    private void Awake()
    {
        if (player == null)
        {
#if UNITY_2023_1_OR_NEWER
            player = FindFirstObjectByType<PlayerController2D>();
#else
            player = FindObjectOfType<PlayerController2D>();
#endif
        }

        AssignRootsFromChildren();
        SetupAudioSource();

        if (buildUIAutomatically && gpsCanvas == null)
        {
            BuildGPSUI();
        }

        if (gpsCanvas != null) gpsCanvas.SetActive(false);
    }

    private void Update()
    {
        // Se o tempo do LevelTimer acabou no meio da rota ou com o mapa aberto, encerra tudo na hora
        if (IsLevelOver && (gpsOpen || running))
        {
            gpsOpen = false;
            running = false;
            if (activeRouteCoroutine != null) StopCoroutine(activeRouteCoroutine);
            if (gpsCanvas != null) gpsCanvas.SetActive(false);
            SetVelocity(Vector2.zero);
        }
    }

    private void SetupAudioSource()
    {
        if (sfxSource == null)
        {
            sfxSource = GetComponent<AudioSource>();
            if (sfxSource == null) sfxSource = gameObject.AddComponent<AudioSource>();
        }
        sfxSource.playOnAwake = false;
    }

    private void AssignRootsFromChildren()
    {
        if (rota1Pontos == null && transform.childCount > 0) rota1Pontos = transform.GetChild(0);
        if (rota2Pontos == null && transform.childCount > 1) rota2Pontos = transform.GetChild(1);
    }

    private void PlaySFX(AudioClip clip)
    {
        if (clip != null && sfxSource != null)
        {
            sfxSource.PlayOneShot(clip, sfxVolume);
        }
    }

    // ------------------------------------------------------------------
    //  API PUBLICA
    // ------------------------------------------------------------------

    /// <summary>Chamado pelo GPSPickup ao coletar o item: toca o som de coleta e abre o GPS.</summary>
    public void OpenGPS()
    {
        if (gpsOpen || running || IsLevelOver) return;

        PlaySFX(collectItemSound);
        OpenGPSInternal();
    }

    private void OpenGPSInternal()
    {
        if (IsLevelOver) return;
        gpsOpen = true;

        ShowChoose();
        if (player != null) player.SetCanMove(false);
        onGPSOpened?.Invoke();
    }

    public void SelectRoute1() => SelectRoute(0);
    public void SelectRoute2() => SelectRoute(1);

    public void SelectRoute(int routeIndex)
    {
        if (!gpsOpen || running || IsLevelOver) return;
        gpsOpen = false;

        if (gpsCanvas != null) gpsCanvas.SetActive(false);

        if (routeIndex == correctRouteIndex)
        {
            PlaySFX(routeCorrectSound);
            onCorrectRouteSelected?.Invoke();
        }

        activeRouteCoroutine = StartCoroutine(RouteRoutine(routeIndex));
    }

    public void ReleasePlayer()
    {
        running = false;
        if (player == null) return;
        player.enabled = true;
        player.SetCanMove(true);
    }

    // ------------------------------------------------------------------
    //  ROTINA PRINCIPAL (Rota Certa -> LevelTimer Vitoria | Rota Errada -> Reabre Mapa)
    // ------------------------------------------------------------------

    private IEnumerator RouteRoutine(int chosenRoute)
    {
        running = true;

        List<Transform> chosen = GetPoints(chosenRoute);

        if (player == null || chosen.Count == 0)
        {
            Debug.LogError("GPSRouteFollower: falta o Player ou os pontos da rota!", this);
            ReleasePlayer();
            yield break;
        }

        TakeControl();

        // Percorre a rota ESCOLHIDA (seja certa ou errada)
        int start = startFromNearestPoint ? NearestIndex(chosen) : 0;
        yield return FollowPoints(chosen, start);

        if (IsLevelOver) yield break;

        // Se era a ROTA ERRADA: toca som de erro, avisa e REABRE O MAPA para escolher de novo
        if (chosenRoute != correctRouteIndex)
        {
            PlaySFX(routeWrongSound);
            onWrongRouteEnded?.Invoke();

            if (wrongRouteNoticeDelay > 0f)
            {
                ShowWrongWarning();
                yield return new WaitForSeconds(wrongRouteNoticeDelay);
            }

            if (IsLevelOver) yield break;

            running = false;
            OpenGPSInternal();
            yield break;
        }

        // Se era a ROTA CERTA: aciona a vitoria no LevelTimer!
        running = false;
        onRouteCompleted?.Invoke();

        if (LevelTimer.Instance != null)
        {
            LevelTimer.Instance.TriggerVictory();
        }
        else
        {
            Debug.LogWarning("GPSRouteFollower: rota certa concluida, mas nenhum LevelTimer foi encontrado na cena!", this);
        }
    }

    private IEnumerator FollowPoints(List<Transform> points, int startIndex)
    {
        for (int i = startIndex; i < points.Count; i++)
        {
            if (IsLevelOver) yield break;
            if (points[i] != null) yield return MoveTo(points[i]);
        }
        SetVelocity(Vector2.zero);
    }

    private IEnumerator MoveTo(Transform target)
    {
        float stuckTimer = 0f;
        float stepDistance = moveSpeed * Time.fixedDeltaTime;
        float threshold = Mathf.Max(arriveDistance, stepDistance);

        while (!IsLevelOver)
        {
            Vector2 toTarget = (Vector2)target.position - rb.position;
            if (toTarget.magnitude <= threshold) yield break;

            Vector2 dir = toTarget.normalized;
            SetVelocity(dir * moveSpeed);
            Face(dir);

            Vector2 before = rb.position;
            yield return new WaitForFixedUpdate();

            if (stuckTimeout > 0f)
            {
                float moved = (rb.position - before).magnitude;
                if (moved < stepDistance * 0.2f) stuckTimer += Time.fixedDeltaTime;
                else stuckTimer = 0f;

                if (stuckTimer >= stuckTimeout)
                {
                    rb.position = target.position;
                    yield break;
                }
            }
        }
    }

    // ------------------------------------------------------------------
    //  PAINEL DO GPS
    // ------------------------------------------------------------------

    private void ShowChoose()
    {
        if (gpsCanvas != null) gpsCanvas.SetActive(true);
        if (chooseGroup != null) chooseGroup.SetActive(true);
        if (wrongWarningGroup != null) wrongWarningGroup.SetActive(false);
    }

    private void ShowWrongWarning()
    {
        if (gpsCanvas != null) gpsCanvas.SetActive(true);
        if (chooseGroup != null) chooseGroup.SetActive(false);
        if (wrongWarningGroup != null) wrongWarningGroup.SetActive(true);
    }

    // ------------------------------------------------------------------
    //  AUXILIARES
    // ------------------------------------------------------------------

    private List<Transform> GetPoints(int routeIndex)
    {
        List<Transform> list = new List<Transform>();
        Transform root = routeIndex == 0 ? rota1Pontos : rota2Pontos;
        if (root == null) return list;
        foreach (Transform child in root) list.Add(child);
        return list;
    }

    private void TakeControl()
    {
        rb = player.GetComponent<Rigidbody2D>();
        sr = player.GetComponentInChildren<SpriteRenderer>();

        player.SetCanMove(false);
        player.enabled = false;
        SetVelocity(Vector2.zero);
    }

    private int NearestIndex(List<Transform> points)
    {
        int best = 0;
        float bestDist = float.MaxValue;
        Vector2 pos = rb.position;

        for (int i = 0; i < points.Count; i++)
        {
            if (points[i] == null) continue;
            float d = Vector2.Distance(pos, points[i].position);
            if (d < bestDist)
            {
                bestDist = d;
                best = i;
            }
        }
        return best;
    }

    private void Face(Vector2 dir)
    {
        if (flipSprite && sr != null && Mathf.Abs(dir.x) > 0.3f)
            sr.flipX = dir.x < 0f;
    }

    private void SetVelocity(Vector2 v)
    {
        if (rb == null) return;
#if UNITY_6000_0_OR_NEWER
        rb.linearVelocity = v;
#else
        rb.velocity = v;
#endif
    }

    // ------------------------------------------------------------------
    //  INTERFACE AUTOMATICA DO GPS
    // ------------------------------------------------------------------

    private static readonly Color PanelColor = new Color(0.07f, 0.07f, 0.10f, 0.94f);
    private static readonly Color ButtonColor = new Color(0.95f, 0.10f, 0.50f, 1f);

    private void BuildGPSUI()
    {
        EnsureEventSystem();

        Canvas canvas = CreateCanvas("GPS_Canvas", 100);
        gpsCanvas = canvas.gameObject;

        RectTransform panel = CreateImage(canvas.transform, "Painel", PanelColor, new Vector2(760f, 380f));

        chooseGroup = CreateGroup(panel, "Grupo_Escolher");
        CreateText(chooseGroup.transform, chooseTitle, 46, new Vector2(700f, 90f), new Vector2(0f, 110f));
        CreateButton(chooseGroup.transform, route1Label, new Vector2(-170f, -60f), SelectRoute1);
        CreateButton(chooseGroup.transform, route2Label, new Vector2(170f, -60f), SelectRoute2);

        wrongWarningGroup = CreateGroup(panel, "Grupo_RotaErrada");
        CreateText(wrongWarningGroup.transform, wrongRouteMessage, 42, new Vector2(700f, 160f), Vector2.zero);
        wrongWarningGroup.SetActive(false);
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

    private Canvas CreateCanvas(string name, int sortOrder)
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

    private static RectTransform CreateImage(Transform parent, string name, Color color, Vector2 size)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);

        go.GetComponent<Image>().color = color;

        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = size;
        rt.anchoredPosition = Vector2.zero;
        return rt;
    }

    private static GameObject CreateGroup(Transform parent, string name)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        Stretch(go.GetComponent<RectTransform>());
        return go;
    }

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    private Text CreateText(Transform parent, string content, int fontSize, Vector2 size, Vector2 position)
    {
        GameObject go = new GameObject("Texto", typeof(RectTransform), typeof(Text));
        go.transform.SetParent(parent, false);

        Text t = go.GetComponent<Text>();
        t.text = content;
        t.font = GetFont();
        t.fontSize = fontSize;
        t.alignment = TextAnchor.MiddleCenter;
        t.color = Color.white;
        t.raycastTarget = false;

        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = size;
        rt.anchoredPosition = position;
        return t;
    }

    private void CreateButton(Transform parent, string label, Vector2 position, UnityAction onClick)
    {
        GameObject go = new GameObject("Botao_" + label, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);

        Image img = go.GetComponent<Image>();
        img.color = ButtonColor;

        Button button = go.GetComponent<Button>();
        button.targetGraphic = img;
        button.onClick.AddListener(onClick);

        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(320f, 110f);
        rt.anchoredPosition = position;

        CreateText(go.transform, label, 38, new Vector2(310f, 100f), Vector2.zero);
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

    // ------------------------------------------------------------------
    //  GIZMOS
    // ------------------------------------------------------------------

    private void OnDrawGizmos()
    {
        DrawRoute(rota1Pontos != null ? rota1Pontos : (transform.childCount > 0 ? transform.GetChild(0) : null),
                  new Color(0.2f, 0.9f, 0.4f), new Color(0.2f, 0.6f, 1f), "R1-");
        DrawRoute(rota2Pontos != null ? rota2Pontos : (transform.childCount > 1 ? transform.GetChild(1) : null),
                  new Color(1f, 0.7f, 0.1f), new Color(1f, 0.25f, 0.25f), "R2-");
    }

    private void DrawRoute(Transform root, Color startColor, Color lineColor, string prefix)
    {
        if (root == null) return;

        Transform previous = null;
        int count = root.childCount;

        for (int i = 0; i < count; i++)
        {
            Transform p = root.GetChild(i);

            Gizmos.color = i == 0 ? startColor : lineColor;
            Gizmos.DrawWireSphere(p.position, 0.25f);

            if (previous != null)
            {
                Gizmos.color = lineColor;
                Gizmos.DrawLine(previous.position, p.position);
            }
#if UNITY_EDITOR
            Handles.Label(p.position + Vector3.up * 0.4f, prefix + (i + 1));
#endif
            previous = p;
        }
    }
}