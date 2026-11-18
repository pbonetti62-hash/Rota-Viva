using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using System.Collections;
using TMPro;

public class SceneTransitionManager : MonoBehaviour
{
    public static SceneTransitionManager Instance;

    [Header("UI References")]
    [Tooltip("Arraste o CanvasGroup do seu Painel Preto de Fade aqui.")]
    public CanvasGroup fadeCanvasGroup;

    [Tooltip("Arraste a Image da sua barra de carregamento personalizada aqui (Image Type deve estar como Filled).")]
    public Image loadingBarImage;

    [Tooltip("(Opcional) Arraste o texto de porcentagem (TextMeshPro) aqui.")]
    public TextMeshProUGUI loadingPercentageText;

    [Header("Settings")]
    [Tooltip("Duração do efeito de Fade In e Fade Out em segundos.")]
    public float fadeDuration = 1.0f;
    [Tooltip("Tempo mínimo obrigatório de espera/carregamento (ex: 3 segundos).")]
    public float minLoadingTime = 3.0f;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
            return;
        }
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (fadeCanvasGroup != null)
        {
            fadeCanvasGroup.gameObject.SetActive(true);
            StartCoroutine(FadeInRoutine());
        }
    }

    /// <summary>
    /// Ponto único e obrigatório para mudança de cena com Fade e Loading.
    /// </summary>
    public void LoadScene(string sceneName)
    {
        // Se o tempo estiver congelado (ex: menu de pause), destrava para a transição rodar fluida
        Time.timeScale = 1f;
        AudioListener.pause = false;

        if (fadeCanvasGroup != null)
        {
            fadeCanvasGroup.gameObject.SetActive(true);
            StartCoroutine(FadeOutAndLoadRoutine(sceneName));
        }
        else
        {
            SceneManager.LoadScene(sceneName);
        }
    }

    private IEnumerator FadeInRoutine()
    {
        fadeCanvasGroup.alpha = 1f;

        if (loadingBarImage != null) loadingBarImage.gameObject.SetActive(false);
        if (loadingPercentageText != null) loadingPercentageText.gameObject.SetActive(false);

        float timer = 0f;
        while (timer < fadeDuration)
        {
            timer += Time.unscaledDeltaTime; // Usa unscaledDeltaTime para funcionar mesmo se o jogo estivesse pausado
            fadeCanvasGroup.alpha = Mathf.Lerp(1f, 0f, timer / fadeDuration);
            yield return null;
        }

        fadeCanvasGroup.alpha = 0f;
        fadeCanvasGroup.gameObject.SetActive(false);
    }

    private IEnumerator FadeOutAndLoadRoutine(string sceneName)
    {
        fadeCanvasGroup.alpha = 0f;
        fadeCanvasGroup.gameObject.SetActive(true);

        // 1. Fade Out
        float timer = 0f;
        while (timer < fadeDuration)
        {
            timer += Time.unscaledDeltaTime;
            fadeCanvasGroup.alpha = Mathf.Lerp(0f, 1f, timer / fadeDuration);
            yield return null;
        }
        fadeCanvasGroup.alpha = 1f;

        // 2. Ativa a barra personalizada e o texto
        if (loadingBarImage != null)
        {
            loadingBarImage.gameObject.SetActive(true);
            loadingBarImage.fillAmount = 0f;
        }
        if (loadingPercentageText != null)
        {
            loadingPercentageText.gameObject.SetActive(true);
            loadingPercentageText.text = "0%";
        }

        // 3. Carregamento Assíncrono travado
        AsyncOperation operation = SceneManager.LoadSceneAsync(sceneName);
        operation.allowSceneActivation = false;

        float fakeTimer = 0f;
        float progress = 0f;

        // 4. Loop de espera (3 segundos mínimos + carregamento real)
        while (!operation.isDone)
        {
            fakeTimer += Time.unscaledDeltaTime;

            float realProgress = Mathf.Clamp01(operation.progress / 0.9f);
            progress = Mathf.Clamp01(fakeTimer / minLoadingTime);

            if (loadingBarImage != null) loadingBarImage.fillAmount = progress;
            if (loadingPercentageText != null) loadingPercentageText.text = Mathf.RoundToInt(progress * 100f) + "%";

            if (fakeTimer >= minLoadingTime && operation.progress >= 0.9f)
            {
                operation.allowSceneActivation = true;
            }

            yield return null;
        }
    }
}