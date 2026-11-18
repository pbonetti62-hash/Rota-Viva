using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// ROTA VIVA - Bengala Inteligente (fase 3, pessoa com baixa visao).
///
/// COMO USAR:
///  1. Na Hierarchy, crie um objeto vazio chamado exatamente  Map  e coloque DENTRO dele
///     todos os objetos da fase (postes, barreiras, paredes, buracos...).
///     Cada um precisa de um Collider2D (pode ser Trigger) para a bengala "sentir".
///  2. Coloque este script no player da persona de baixa visao.
///  3. (Opcional) Arraste um som de alarme no campo "Alarm Clip".
///  4. Dê Play.
///
/// O QUE FAZ:
///  - Todo o conteudo do Map comeca INVISIVEL (so o Renderer; colisao continua ativa).
///  - Uma raycast invisivel sai do player na direcao da ultima seta apertada,
///    atualizada em tempo real (mudou a seta, a bengala muda junto).
///  - O PRIMEIRO objeto do Map que a raycast toca fica visivel.
///    Quando a bengala para de apontar para ele, ele volta a ficar invisivel.
///  - Enquanto a bengala toca em algo, o alarme toca em loop. Sem toque = silencio total.
///
/// DICAS:
///  - Deixe o CHAO e as RUAS fora do Map para o jogador ter uma nocao do caminho.
///  - Colliders de objetos FORA do Map sao ignorados pela bengala (nao bloqueiam a raycast).
///  - Para a "bengala melhorada" (item), chame SetCaneLength(4f) quando coletar o item.
/// </summary>
[RequireComponent(typeof(PlayerController2D))]
public class CaneScanner : MonoBehaviour
{
    [Header("1) Mapa (procurado automaticamente pelo nome)")]
    [Tooltip("Pai de todos os objetos da fase. Se vazio, procura na cena um objeto com o nome abaixo.")]
    public Transform mapRoot;
    [Tooltip("Nome do objeto vazio que guarda todos os objetos.")]
    public string mapName = "Map";
    [Tooltip("Desligue para ver o mapa INTEIRO (montagem e testes). Ligado = so aparece o que a bengala toca.")]
    public bool scannerEnabled = true;

    [Header("2) Bengala")]
    [Tooltip("Alcance da bengala (unidades Unity). Aumente com o item 'Bengala Inteligente'.")]
    [Min(0.1f)] public float caneLength = 2.5f;
    [Tooltip("0 = raio fino. Valores pequenos (0.1 a 0.3) deixam a bengala mais 'gorda' e facil de acertar.")]
    [Min(0f)] public float castRadius = 0f;
    [Tooltip("Deslocamento do ponto de partida em relacao ao centro do player.")]
    public Vector2 originOffset = Vector2.zero;
    [Tooltip("Camadas que a bengala detecta. Deixe Everything se nao usa layers.")]
    public LayerMask detectLayers = ~0;
    [Tooltip("Ligado: objetos Trigger (ex.: calcada quebrada) NAO sao detectados.")]
    public bool ignoreTriggers = false;

    [Header("3) Alarme (opcional)")]
    [Tooltip("Liga/desliga o alarme. Pode mudar durante o Play.")]
    public bool playAlarm = true;
    public AudioClip alarmClip;
    [Range(0f, 1f)] public float alarmVolume = 1f;
    [Tooltip("Ligado: quanto mais perto do objeto, mais agudo o alarme.")]
    public bool changePitchWithDistance = false;
    [Tooltip("X = tom no alcance maximo (longe). Y = tom colado no objeto (perto).")]
    public Vector2 pitchRange = new Vector2(0.9f, 1.6f);

    [Header("Eventos (falas da Cidade, vibracao...)")]
    [Tooltip("Dispara quando a bengala passa a tocar em um objeto.")]
    public UnityEvent onObstacleDetected;
    [Tooltip("Dispara quando a bengala deixa de tocar em qualquer objeto.")]
    public UnityEvent onPathClear;

    [Header("Debug")]
    [Tooltip("Desenha a bengala na Scene (verde = livre, vermelho = tocando). Nao aparece no jogo.")]
    public bool drawDebugRay = true;

    // ------------------------------------------------------------------
    //  ESTADO PUBLICO (somente leitura)
    // ------------------------------------------------------------------

    /// <summary>Direcao atual da bengala (sempre a ultima seta apertada).</summary>
    public Vector2 CaneDirection { get; private set; } = Vector2.down;
    /// <summary>Objeto que a bengala esta tocando agora (null = nada).</summary>
    public Collider2D CurrentObstacle => currentTarget;
    /// <summary>A bengala esta tocando em algo?</summary>
    public bool IsDetecting => currentTarget != null;
    /// <summary>Distancia ate o objeto tocado.</summary>
    public float CurrentDistance { get; private set; }

    // ------------------------------------------------------------------
    //  INTERNO
    // ------------------------------------------------------------------

    private PlayerController2D player;
    private AudioSource alarm;
    private Collider2D currentTarget;
    private bool? appliedScannerState;

    private readonly RaycastHit2D[] hits = new RaycastHit2D[16];
    private readonly Dictionary<Collider2D, Renderer[]> renderersCache = new Dictionary<Collider2D, Renderer[]>();

    private void Awake()
    {
        player = GetComponent<PlayerController2D>();

        alarm = gameObject.AddComponent<AudioSource>();
        alarm.playOnAwake = false;
        alarm.loop = true;
        alarm.spatialBlend = 0f; // som 2D, sempre audivel
    }

    private void Start()
    {
        FindMap();

        if (mapRoot == null)
        {
            Debug.LogError("CaneScanner: nao achei nenhum objeto chamado '" + mapName +
                           "' na cena. Crie um objeto vazio com esse nome e coloque os objetos da fase dentro dele.", this);
            enabled = false;
        }
    }

    private void OnDisable()
    {
        if (alarm != null && alarm.isPlaying) alarm.Stop();
    }

    private void LateUpdate()
    {
        // LateUpdate: roda depois do PlayerController2D, entao a seta ja esta atualizada.
        ApplyScannerState();

        if (!scannerEnabled)
        {
            UpdateAlarm();
            return;
        }

        CaneDirection = player.FacingDirection;
        Scan();
        UpdateAlarm();
    }

    // ------------------------------------------------------------------
    //  API PUBLICA
    // ------------------------------------------------------------------

    /// <summary>Muda o alcance da bengala (use ao coletar o item de bengala melhorada).</summary>
    public void SetCaneLength(float length)
    {
        caneLength = Mathf.Max(0.1f, length);
    }

    /// <summary>Liga/desliga a bengala. Desligada = mapa todo visivel.</summary>
    public void SetScannerEnabled(bool value)
    {
        scannerEnabled = value;
    }

    /// <summary>Chame se criar objetos novos dentro do Map durante o jogo (eles voltam a ficar escondidos).</summary>
    public void RefreshMap()
    {
        renderersCache.Clear();
        currentTarget = null;
        appliedScannerState = null;
    }

    // ------------------------------------------------------------------
    //  BENGALA
    // ------------------------------------------------------------------

    private void Scan()
    {
        Vector2 origin = (Vector2)transform.position + originOffset;

        ContactFilter2D filter = new ContactFilter2D();
        filter.SetLayerMask(detectLayers);
        filter.useTriggers = !ignoreTriggers;

        int count = castRadius > 0f
            ? Physics2D.CircleCast(origin, castRadius, CaneDirection, filter, hits, caneLength)
            : Physics2D.Raycast(origin, CaneDirection, filter, hits, caneLength);

        // Pega o objeto do Map MAIS PERTO. Tudo que nao e do Map (inclusive o proprio player) e ignorado.
        Collider2D nearest = null;
        float bestDistance = float.MaxValue;

        for (int i = 0; i < count; i++)
        {
            Collider2D col = hits[i].collider;
            if (col == null || !col.transform.IsChildOf(mapRoot)) continue;

            if (hits[i].distance < bestDistance)
            {
                bestDistance = hits[i].distance;
                nearest = col;
            }
        }

        CurrentDistance = nearest != null ? bestDistance : 0f;
        SetTarget(nearest);
    }

    private void SetTarget(Collider2D newTarget)
    {
        if (newTarget == currentTarget) return; // mesmo objeto (ou continua sem nada)

        Collider2D previous = currentTarget;

        SetVisible(previous, false); // o que saiu da mira volta a sumir
        SetVisible(newTarget, true); // o que entrou na mira aparece

        currentTarget = newTarget;

        if (newTarget != null) onObstacleDetected?.Invoke();
        else if (previous != null) onPathClear?.Invoke();
    }

    // ------------------------------------------------------------------
    //  VISIBILIDADE (so o Renderer! A colisao continua funcionando)
    // ------------------------------------------------------------------

    private void ApplyScannerState()
    {
        if (appliedScannerState.HasValue && appliedScannerState.Value == scannerEnabled) return;
        appliedScannerState = scannerEnabled;

        if (mapRoot == null) return;

        foreach (Renderer r in mapRoot.GetComponentsInChildren<Renderer>(true))
        {
            if (r != null) r.enabled = !scannerEnabled; // ligado = tudo escondido; desligado = tudo visivel
        }

        currentTarget = null;
        CurrentDistance = 0f;
    }

    private void SetVisible(Collider2D col, bool visible)
    {
        if (col == null) return;

        foreach (Renderer r in GetRenderers(col))
        {
            if (r != null) r.enabled = visible;
        }
    }

    private Renderer[] GetRenderers(Collider2D col)
    {
        if (renderersCache.TryGetValue(col, out Renderer[] list)) return list;

        // Renderers no proprio objeto e nos filhos
        list = col.GetComponentsInChildren<Renderer>(true);

        // Se o collider esta num filho e o desenho no pai, procura para cima
        if (list.Length == 0)
        {
            Renderer parentRenderer = col.GetComponentInParent<Renderer>();
            list = parentRenderer != null ? new[] { parentRenderer } : new Renderer[0];
        }

        renderersCache[col] = list;
        return list;
    }

    private void FindMap()
    {
        if (mapRoot != null) return;

        GameObject go = GameObject.Find(mapName);
        if (go != null) mapRoot = go.transform;
    }

    // ------------------------------------------------------------------
    //  ALARME (so toca enquanto a bengala toca em algo)
    // ------------------------------------------------------------------

    private void UpdateAlarm()
    {
        bool shouldPlay = playAlarm && alarmClip != null && scannerEnabled
                          && IsDetecting && Time.timeScale > 0f;

        if (shouldPlay)
        {
            if (alarm.clip != alarmClip) alarm.clip = alarmClip;
            alarm.volume = alarmVolume;

            if (changePitchWithDistance)
            {
                float t = Mathf.Clamp01(CurrentDistance / caneLength); // 0 = colado, 1 = no limite
                alarm.pitch = Mathf.Lerp(pitchRange.y, pitchRange.x, t);
            }
            else
            {
                alarm.pitch = 1f;
            }

            if (!alarm.isPlaying) alarm.Play();
        }
        else if (alarm.isPlaying)
        {
            alarm.Stop(); // sem toque = silencio
        }
    }

    // ------------------------------------------------------------------
    //  GIZMO: a bengala aparece so na Scene (invisivel no jogo)
    // ------------------------------------------------------------------

    private void OnDrawGizmos()
    {
        if (!drawDebugRay) return;

        bool playing = Application.isPlaying;
        Vector2 dir = playing ? CaneDirection : Vector2.down;
        bool detecting = playing && IsDetecting;

        Vector3 origin = transform.position + (Vector3)originOffset;
        float length = detecting ? CurrentDistance : caneLength;
        Vector3 end = origin + (Vector3)(dir.normalized * length);

        Gizmos.color = detecting ? Color.red : Color.green;
        Gizmos.DrawLine(origin, end);
        if (castRadius > 0f) Gizmos.DrawWireSphere(end, castRadius);
    }
}
