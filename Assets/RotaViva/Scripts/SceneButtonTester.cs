using UnityEngine;

public class SceneButtonTester : MonoBehaviour
{
    [Header("Scene Settings")]
    [Tooltip("Digite exatamente o nome da cena para onde você quer ir (Build Settings).")]
    public string targetSceneName;

    /// <summary>
    /// Esta função será acionada pelo botão da UI.
    /// </summary>
    public void GoToTargetScene()
    {
        if (SceneTransitionManager.Instance != null)
        {
            // Usa o seu gerenciador de transição com Fade e Loading
            SceneTransitionManager.Instance.LoadScene(targetSceneName);
        }
        else
        {
            // Fallback caso esqueça de colocar o gerenciador na cena inicial
            Debug.LogWarning("SceneTransitionManager não encontrado! Carregando cena diretamente...");
            UnityEngine.SceneManagement.SceneManager.LoadScene(targetSceneName);
        }
    }
}