using UnityEngine;

public class SceneChangerButton : MonoBehaviour
{
    [Tooltip("Nome exato da cena que deseja carregar.")]
    public string sceneName;

    // Basta ligar este método no OnClick() de qualquer botão da sua UI!
    public void ChangeScene()
    {
        if (SceneTransitionManager.Instance != null)
        {
            SceneTransitionManager.Instance.LoadScene(sceneName);
        }
        else
        {
            Debug.LogError("SceneTransitionManager não encontrado na cena!");
            UnityEngine.SceneManagement.SceneManager.LoadScene(sceneName);
        }
    }
}