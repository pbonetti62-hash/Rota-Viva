using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Events;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>O que fazer com um objeto quando o atalho abrir.</summary>
public enum GateAction
{
    Destroy, // apaga o objeto de vez
    Hide,    // desativa (some e perde a colisao) -> ABRE o caminho
    Show,    // ativa (aparece) -> ex.: cartao no carrinho, rampa nova
    Toggle   // inverte (se estava ativo, desativa; se estava desativado, ativa)
}

/// <summary>Uma linha da lista: qual objeto + o que fazer com ele.</summary>
[Serializable]
public class GateTarget
{
    [Tooltip("Objeto da cena: escada, catraca, portao, cartao no carrinho, rampa...")]
    public GameObject target;

    [Tooltip("Destroy = apaga | Hide = some | Show = aparece | Toggle = inverte.")]
    public GateAction action = GateAction.Hide;

    [Tooltip("Espera (segundos) depois de abrir antes de aplicar. 0 = na hora.")]
    [Min(0f)] public float delay = 0f;
}

/// <summary>
/// ROTA VIVA - Portao de atalho (fase 2: mae com carrinho).
///
/// COMO USAR:
///  1. Crie um objeto na frente do atalho (pode ser o proprio sprite do portao/catraca).
///     Coloque este script. O Collider2D vira Trigger sozinho; deixe ele um pouco maior
///     que o sprite para o player nao precisar encostar.
///  2. Na lista "Targets", adicione uma linha para CADA coisa que deve mudar:
///        - Escada / catraca / portao fechado   -> Hide (ou Destroy)
///        - Rampa / passagem nova                -> Show
///        - Cartao no carrinho (futuro)          -> Show
///     Para TROCAR (escada some, rampa aparece): uma linha Hide + uma linha Show.
///  3. Player chega perto, aperta E e o atalho abre.
///
/// IMPORTANTE:
///  - Hide desativa o objeto INTEIRO, inclusive o collider. E isso que libera a passagem.
///  - Objetos que comecam escondidos (Show) devem estar DESATIVADOS na Hierarchy
///    (caixinha ao lado do nome desmarcada).
///  - Nao coloque o proprio portao na lista (use "Opened Sprite" para trocar o visual dele).
///  - Na Scene, linhas coloridas ligam o portao a cada alvo:
///    vermelho = some, verde = aparece, amarelo = inverte.
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class ShortcutGate : MonoBehaviour
{
    public enum ActivationMode
    {
        PressE,  // jogador precisa apertar E perto do portao
        OnEnter  // abre assim que o jogador entra na area
    }

    [Header("1) O QUE MUDA QUANDO ABRIR (arraste os objetos aqui)")]
    public GateTarget[] targets;

    [Header("2) Como abre")]
    public ActivationMode activation = ActivationMode.PressE;

    [Tooltip("Vazio = qualquer persona abre. Ex.: 'carrinho' = so a mae com carrinho (usa o campo Persona Name do PlayerController2D).")]
    public string requiredPersona = "";

    [Tooltip("Opcional: aviso 'Pressione E'. Aparece so quando o jogador esta perto e pode abrir.")]
    [SerializeField] private GameObject promptE;

    [Header("3) Visual do proprio portao (opcional)")]
    [Tooltip("Se vazio, usa o SpriteRenderer deste objeto.")]
    [SerializeField] private SpriteRenderer gateRenderer;
    [Tooltip("Sprite que aparece depois de aberto (ex.: portao aberto).")]
    [SerializeField] private Sprite openedSprite;

    [Header("Eventos (som, fala da Cidade, contador...)")]
    public UnityEvent onOpened;
    [Tooltip("Dispara se uma persona errada tentar abrir.")]
    public UnityEvent onWrongPersona;

    public bool IsOpen { get; private set; }

    private PlayerController2D currentPlayer;
    private bool playerInside;

    // ------------------------------------------------------------------

    private void Reset()
    {
        GetComponent<Collider2D>().isTrigger = true;
        gateRenderer = GetComponent<SpriteRenderer>();
    }

    private void Awake()
    {
        GetComponent<Collider2D>().isTrigger = true;
        if (gateRenderer == null) gateRenderer = GetComponent<SpriteRenderer>();
        if (promptE != null) promptE.SetActive(false);
    }

    private void Update()
    {
        if (IsOpen || !playerInside || activation != ActivationMode.PressE) return;
        if (PressedE()) TryOpen();
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (IsOpen) return;

        PlayerController2D p = other.GetComponentInParent<PlayerController2D>();
        if (p == null) return;

        currentPlayer = p;
        playerInside = true;

        if (promptE != null && PersonaAllowed()) promptE.SetActive(true);
        if (activation == ActivationMode.OnEnter) TryOpen();
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        if (other.GetComponentInParent<PlayerController2D>() == null) return;

        playerInside = false;
        currentPlayer = null;
        if (promptE != null) promptE.SetActive(false);
    }

    // ------------------------------------------------------------------

    private void TryOpen()
    {
        if (!PersonaAllowed())
        {
            onWrongPersona?.Invoke();
            return;
        }
        Open();
    }

    /// <summary>
    /// Abre o atalho. Tambem pode ser chamado por outro script ou por um UnityEvent
    /// (ex.: um futuro sistema de placas chama gate.Open() depois de cobrar o custo).
    /// </summary>
    [ContextMenu("Testar: abrir agora (so em Play)")]
    public void Open()
    {
        if (IsOpen) return;
        IsOpen = true;

        if (promptE != null) promptE.SetActive(false);

        if (targets != null)
        {
            foreach (GateTarget t in targets)
            {
                if (t == null || t.target == null) continue;

                if (t.delay <= 0f) Apply(t);
                else StartCoroutine(ApplyAfter(t));
            }
        }

        if (gateRenderer != null && openedSprite != null)
            gateRenderer.sprite = openedSprite;

        GetComponent<Collider2D>().enabled = false;
        onOpened?.Invoke();
    }

    private IEnumerator ApplyAfter(GateTarget t)
    {
        yield return new WaitForSeconds(t.delay);
        if (t.target != null) Apply(t);
    }

    private static void Apply(GateTarget t)
    {
        switch (t.action)
        {
            case GateAction.Destroy: UnityEngine.Object.Destroy(t.target); break;
            case GateAction.Hide: t.target.SetActive(false); break;
            case GateAction.Show: t.target.SetActive(true); break;
            case GateAction.Toggle: t.target.SetActive(!t.target.activeSelf); break;
        }
    }

    private bool PersonaAllowed()
    {
        if (string.IsNullOrWhiteSpace(requiredPersona)) return true;
        if (currentPlayer == null) return false;

        return currentPlayer.personaName != null &&
               currentPlayer.personaName.IndexOf(requiredPersona.Trim(), StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private bool PressedE()
    {
#if ENABLE_LEGACY_INPUT_MANAGER
        return Input.GetKeyDown(KeyCode.E);
#elif ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame;
#else
        return false;
#endif
    }

    // ------------------------------------------------------------------
    //  GIZMOS: linhas ate cada alvo (vermelho = some, verde = aparece, amarelo = inverte)
    // ------------------------------------------------------------------

    private void OnDrawGizmos()
    {
        if (targets == null) return;

        foreach (GateTarget t in targets)
        {
            if (t == null || t.target == null) continue;

            switch (t.action)
            {
                case GateAction.Show: Gizmos.color = Color.green; break;
                case GateAction.Toggle: Gizmos.color = Color.yellow; break;
                default: Gizmos.color = Color.red; break;
            }

            Gizmos.DrawLine(transform.position, t.target.transform.position);
            Gizmos.DrawWireSphere(t.target.transform.position, 0.2f);
        }
    }
}
