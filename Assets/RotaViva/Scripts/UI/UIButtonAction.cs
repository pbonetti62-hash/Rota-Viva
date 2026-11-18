using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// UIButtonAction - UM script para TODOS os botões do seu projeto.
///
/// COMO USAR:
///  1. Coloque este script em qualquer Button (ele adiciona o Button sozinho se faltar).
///  2. Na lista "Ações", clique no "+" e escolha o tipo da ação.
///  3. Preencha só os campos que aparecerem relevantes (cena, painel, valor...).
///  4. Pode empilhar várias ações: elas rodam em ordem (use "Esperar" para pausas).
///
/// TAMBÉM FUNCIONA SEM BOTÃO:
///  Chame Executar() de qualquer lugar (UnityEvent, outro script, animação...).
///  Ex: no seu Player: onDeath -> UIButtonAction.Executar()
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("UI/UI Button Action")]
public class UIButtonAction : MonoBehaviour
{
    public enum Tipo
    {
        // ---------- CENAS ----------
        CarregarCena,            // Pela cena escolhida (arraste a cena no campo)
        CarregarCenaPorIndice,   // Pelo índice do Build Settings
        ReiniciarCena,           // Recarrega a cena atual
        ProximaCena,             // Próximo índice (volta ao 0 no final)
        CenaAnterior,
        CarregarCenaAditiva,     // Soma a cena sem descarregar a atual
        // ---------- JOGO ----------
        SairDoJogo,
        Pausar,
        Retomar,
        AlternarPausa,
        DefinirTimeScale,        // Usa "Valor"
        // ---------- PAINÉIS / OBJETOS ----------
        AbrirPainel,             // Ativa "Alvo"
        FecharPainel,            // Desativa "Alvo"
        AlternarPainel,          // Liga/desliga "Alvo"
        TrocarPainel,            // Fecha "Alvo" e abre "Alvo 2"
        FecharEsteMenu,          // Fecha o painel pai mais próximo (Canvas não conta)
        // ---------- ÁUDIO / TELA ----------
        TocarSom,                // Usa "Som"
        AlternarMudo,
        DefinirVolumeGeral,      // Usa "Valor" (0 a 1)
        AlternarTelaCheia,
        // ---------- EXTRAS ----------
        AbrirURL,                // Usa "Texto"
        CopiarParaAreaTransferencia, // Usa "Texto"
        MostrarCursor,           // Usa "Booleano"
        Esperar,                 // Usa "Valor" (segundos, ignora pausa)
        LogDebug,                // Usa "Texto"
        EventoPersonalizado      // Usa o UnityEvent "Evento"
    }

    [Serializable]
    public class Acao
    {
        public Tipo tipo = Tipo.CarregarCena;

        // --- Cenas ---
#if UNITY_EDITOR
        [Tooltip("Arraste a cena aqui (só no Editor). O nome é copiado para 'Nome Da Cena'.")]
        public SceneAsset cena;
#endif
        [Tooltip("Nome da cena (precisa estar no Build Settings).")]
        public string nomeDaCena;
        public int indiceDaCena;

        // --- Objetos ---
        public GameObject alvo;
        public GameObject alvo2;

        // --- Valores genéricos ---
        public float valor = 1f;
        public string texto;
        public bool booleano;
        public AudioClip som;
        public UnityEvent evento;
    }

    [Header("Ações (executadas em ordem)")]
    public List<Acao> acoes = new List<Acao> { new Acao() };

    [Header("Opções")]
    [Tooltip("Som tocado em TODO clique deste botão (opcional).")]
    public AudioClip somDoClique;
    [Range(0f, 1f)] public float volumeDoClique = 1f;

    [Tooltip("Espera antes de começar a executar (segundos, ignora pausa).")]
    public float atrasoInicial = 0f;

    [Tooltip("Impede cliques repetidos enquanto as ações estão rodando.")]
    public bool bloquearCliquesDuplos = true;

    [Tooltip("Ao carregar cena, devolve Time.timeScale para 1 (evita jogo travado após sair do pause).")]
    public bool resetarTimeScaleAoTrocarCena = true;

    [Tooltip("Pede confirmação antes de executar (ex: 'Tem certeza que quer sair?'). Arraste um painel com botões Sim/Não.")]
    public GameObject painelDeConfirmacao;

    Button _botao;
    bool _executando;
    static AudioSource _sfx;
    static bool _mudo;
    static float _volumeAntesDoMudo = 1f;

    // ------------------------------------------------------------------
    void Awake()
    {
        _botao = GetComponent<Button>();
        if (_botao != null) _botao.onClick.AddListener(AoClicar);
    }

    void OnDestroy()
    {
        if (_botao != null) _botao.onClick.RemoveListener(AoClicar);
    }

#if UNITY_EDITOR
    void Reset()
    {
        if (GetComponent<Button>() == null) Undo.AddComponent<Button>(gameObject);
    }

    // Copia o nome da cena arrastada para a string automaticamente.
    void OnValidate()
    {
        if (acoes == null) return;
        foreach (var a in acoes)
        {
            if (a == null) continue;
            if (a.cena != null) a.nomeDaCena = a.cena.name;
        }
    }
#endif

    // ------------------------------------------------------------------
    void AoClicar()
    {
        if (somDoClique != null) TocarSomUI(somDoClique, volumeDoClique);

        if (painelDeConfirmacao != null)
        {
            // Mostra a confirmação; o botão "Sim" dela deve chamar ExecutarSemConfirmar()
            // (adicione um UIButtonAction só com 'EventoPersonalizado' apontando para ExecutarSemConfirmar).
            painelDeConfirmacao.SetActive(true);
            return;
        }
        Executar();
    }

    /// <summary>Executa as ações (chame de qualquer lugar).</summary>
    public void Executar()
    {
        if (bloquearCliquesDuplos && _executando) return;
        StartCoroutine(Rotina());
    }

    /// <summary>Ignora o painel de confirmação (use no botão "Sim").</summary>
    public void ExecutarSemConfirmar()
    {
        if (painelDeConfirmacao != null) painelDeConfirmacao.SetActive(false);
        if (bloquearCliquesDuplos && _executando) return;
        StartCoroutine(Rotina());
    }

    IEnumerator Rotina()
    {
        _executando = true;

        if (atrasoInicial > 0f) yield return new WaitForSecondsRealtime(atrasoInicial);

        foreach (var a in acoes)
        {
            if (a == null) continue;

            if (a.tipo == Tipo.Esperar)
            {
                yield return new WaitForSecondsRealtime(Mathf.Max(0f, a.valor));
                continue;
            }
            Rodar(a);
        }

        _executando = false;
    }

    // ------------------------------------------------------------------
    void Rodar(Acao a)
    {
        switch (a.tipo)
        {
            // ----- CENAS -----
            case Tipo.CarregarCena:
                if (string.IsNullOrEmpty(a.nomeDaCena)) { Debug.LogWarning($"[UIButtonAction] '{name}': nome da cena vazio.", this); break; }
                PrepararTrocaDeCena();
                SceneManager.LoadScene(a.nomeDaCena);
                break;

            case Tipo.CarregarCenaPorIndice:
                PrepararTrocaDeCena();
                SceneManager.LoadScene(a.indiceDaCena);
                break;

            case Tipo.ReiniciarCena:
                PrepararTrocaDeCena();
                SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
                break;

            case Tipo.ProximaCena:
                PrepararTrocaDeCena();
                SceneManager.LoadScene((SceneManager.GetActiveScene().buildIndex + 1) % SceneManager.sceneCountInBuildSettings);
                break;

            case Tipo.CenaAnterior:
            {
                PrepararTrocaDeCena();
                int total = SceneManager.sceneCountInBuildSettings;
                SceneManager.LoadScene((SceneManager.GetActiveScene().buildIndex - 1 + total) % total);
                break;
            }

            case Tipo.CarregarCenaAditiva:
                SceneManager.LoadScene(a.nomeDaCena, LoadSceneMode.Additive);
                break;

            // ----- JOGO -----
            case Tipo.SairDoJogo:
#if UNITY_EDITOR
                EditorApplication.isPlaying = false;
#else
                Application.Quit();
#endif
                break;

            case Tipo.Pausar:         Time.timeScale = 0f; break;
            case Tipo.Retomar:        Time.timeScale = 1f; break;
            case Tipo.AlternarPausa:  Time.timeScale = Time.timeScale == 0f ? 1f : 0f; break;
            case Tipo.DefinirTimeScale: Time.timeScale = Mathf.Max(0f, a.valor); break;

            // ----- PAINÉIS -----
            case Tipo.AbrirPainel:     Mostrar(a.alvo, true); break;
            case Tipo.FecharPainel:    Mostrar(a.alvo, false); break;
            case Tipo.AlternarPainel:  if (a.alvo != null) Mostrar(a.alvo, !EstaVisivel(a.alvo)); break;
            case Tipo.TrocarPainel:    Mostrar(a.alvo, false); Mostrar(a.alvo2, true); break;
            case Tipo.FecharEsteMenu:  FecharPaiMaisProximo(); break;

            // ----- ÁUDIO / TELA -----
            case Tipo.TocarSom:
                if (a.som != null) TocarSomUI(a.som, Mathf.Approximately(a.valor, 0f) ? 1f : Mathf.Clamp01(a.valor));
                break;

            case Tipo.AlternarMudo:
                if (!_mudo) _volumeAntesDoMudo = AudioListener.volume;
                _mudo = !_mudo;
                AudioListener.volume = _mudo ? 0f : _volumeAntesDoMudo;
                break;

            case Tipo.DefinirVolumeGeral:
                AudioListener.volume = Mathf.Clamp01(a.valor);
                _mudo = false;
                break;

            case Tipo.AlternarTelaCheia:
                Screen.fullScreen = !Screen.fullScreen;
                break;

            // ----- EXTRAS -----
            case Tipo.AbrirURL:
                if (!string.IsNullOrEmpty(a.texto)) Application.OpenURL(a.texto);
                break;

            case Tipo.CopiarParaAreaTransferencia:
                GUIUtility.systemCopyBuffer = a.texto ?? "";
                break;

            case Tipo.MostrarCursor:
                Cursor.visible = a.booleano;
                Cursor.lockState = a.booleano ? CursorLockMode.None : CursorLockMode.Locked;
                break;

            case Tipo.LogDebug:
                Debug.Log($"[UIButtonAction] {a.texto}", this);
                break;

            case Tipo.EventoPersonalizado:
                a.evento?.Invoke();
                break;
        }
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------
    void PrepararTrocaDeCena()
    {
        if (resetarTimeScaleAoTrocarCena) Time.timeScale = 1f;
    }

    static void Mostrar(GameObject go, bool visivel)
    {
        if (go == null) return;
        var painel = go.GetComponent<UIPanel>();   // Se tiver UIPanel, usa fade/pausa/cursor dele
        if (painel != null) { if (visivel) painel.Show(); else painel.Hide(); }
        else go.SetActive(visivel);
    }

    static bool EstaVisivel(GameObject go)
    {
        var painel = go.GetComponent<UIPanel>();
        return painel != null ? painel.IsOpen : go.activeSelf;
    }

    void FecharPaiMaisProximo()
    {
        // Sobe na hierarquia até achar um UIPanel; se não achar, fecha o pai direto (que não seja o Canvas).
        var painel = GetComponentInParent<UIPanel>();
        if (painel != null) { painel.Hide(); return; }

        var pai = transform.parent;
        if (pai != null && pai.GetComponent<Canvas>() == null) pai.gameObject.SetActive(false);
    }

    static void TocarSomUI(AudioClip clip, float volume)
    {
        if (_sfx == null)
        {
            var go = new GameObject("UI_SFX");
            DontDestroyOnLoad(go);
            _sfx = go.AddComponent<AudioSource>();
            _sfx.playOnAwake = false;
            _sfx.ignoreListenerPause = true; // toca mesmo com o jogo pausado
        }
        _sfx.PlayOneShot(clip, volume);
    }
}
