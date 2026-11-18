using System.Collections;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// UIPanel - coloque em qualquer painel/menu (Pause, GameOver, Opções, Inventário...).
/// Ele NUNCA desativa o GameObject: usa CanvasGroup (alpha/interactable/raycast),
/// por isso a tecla de atalho funciona mesmo com o painel escondido.
///
/// O UIButtonAction reconhece este script automaticamente (Abrir/Fechar/Alternar/Trocar).
///
/// Para abrir via código (ex: Game Over):  meuPainel.Show();
/// </summary>
[RequireComponent(typeof(CanvasGroup))]
[AddComponentMenu("UI/UI Panel")]
public class UIPanel : MonoBehaviour
{
    [Header("Estado")]
    public bool comecaEscondido = true;

    [Header("Animação")]
    [Tooltip("0 = instantâneo. Usa tempo não escalado (funciona com o jogo pausado).")]
    public float duracaoDoFade = 0.15f;

    [Header("Comportamento ao abrir")]
    public bool pausarOJogo = false;
    public bool mostrarCursor = false;
    [Tooltip("Fecha os outros UIPanels da mesma 'Categoria' ao abrir este (ex: só um menu por vez).")]
    public string categoria = "";

    [Header("Atalho (Input antigo)")]
    [Tooltip("Ex: Escape para pause. None = sem atalho.")]
    public KeyCode teclaAlternar = KeyCode.None;

    [Header("Eventos")]
    public UnityEvent aoAbrir;
    public UnityEvent aoFechar;

    public bool IsOpen { get; private set; }

    CanvasGroup _cg;
    Coroutine _fade;
    float _timeScaleAnterior = 1f;
    bool _euPausei;
    bool _cursorVisivelAnterior;
    CursorLockMode _lockAnterior;

    static readonly System.Collections.Generic.List<UIPanel> _todos = new System.Collections.Generic.List<UIPanel>();

    void Awake()
    {
        _cg = GetComponent<CanvasGroup>();
        if (comecaEscondido) Aplicar(false, 0f, false);
        else { IsOpen = true; Aplicar(true, 0f, false); }
    }

    void OnEnable()  { _todos.Add(this); }
    void OnDisable()
    {
        _todos.Remove(this);
        if (_euPausei) { Time.timeScale = _timeScaleAnterior; _euPausei = false; }
    }

    void Update()
    {
#if ENABLE_LEGACY_INPUT_MANAGER
        if (teclaAlternar != KeyCode.None && Input.GetKeyDown(teclaAlternar)) Toggle();
#endif
    }

    // ------------------------------------------------------------------
    public void Show()
    {
        if (IsOpen) return;

        if (!string.IsNullOrEmpty(categoria))
            foreach (var p in _todos)
                if (p != this && p.IsOpen && p.categoria == categoria) p.Hide();

        IsOpen = true;
        Aplicar(true, duracaoDoFade, true);

        if (pausarOJogo && !_euPausei)
        {
            _timeScaleAnterior = Time.timeScale == 0f ? 1f : Time.timeScale;
            Time.timeScale = 0f;
            _euPausei = true;
        }
        if (mostrarCursor)
        {
            _cursorVisivelAnterior = Cursor.visible;
            _lockAnterior = Cursor.lockState;
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
        }
        aoAbrir?.Invoke();
    }

    public void Hide()
    {
        if (!IsOpen) return;
        IsOpen = false;
        Aplicar(false, duracaoDoFade, true);

        if (_euPausei) { Time.timeScale = _timeScaleAnterior; _euPausei = false; }
        if (mostrarCursor) { Cursor.visible = _cursorVisivelAnterior; Cursor.lockState = _lockAnterior; }
        aoFechar?.Invoke();
    }

    public void Toggle() { if (IsOpen) Hide(); else Show(); }

    // ------------------------------------------------------------------
    void Aplicar(bool visivel, float duracao, bool animar)
    {
        if (_fade != null) StopCoroutine(_fade);

        if (!animar || duracao <= 0f || !gameObject.activeInHierarchy)
        {
            DefinirEstado(visivel, visivel ? 1f : 0f);
            return;
        }
        _fade = StartCoroutine(Fade(visivel, duracao));
    }

    IEnumerator Fade(bool visivel, float duracao)
    {
        float de = _cg.alpha, para = visivel ? 1f : 0f, t = 0f;

        // Ao abrir, já libera a interação; ao fechar, bloqueia imediatamente.
        _cg.blocksRaycasts = visivel;
        _cg.interactable = visivel;

        while (t < duracao)
        {
            t += Time.unscaledDeltaTime;
            _cg.alpha = Mathf.Lerp(de, para, t / duracao);
            yield return null;
        }
        DefinirEstado(visivel, para);
    }

    void DefinirEstado(bool visivel, float alpha)
    {
        _cg.alpha = alpha;
        _cg.interactable = visivel;
        _cg.blocksRaycasts = visivel;
    }
}
