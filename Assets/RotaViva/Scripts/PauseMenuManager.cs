using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class PauseMenuManager : MonoBehaviour
{
    [Header("Panels Reference")]
    [Tooltip("Arraste o GameObject do painel do Menu de Pause aqui.")]
    public GameObject pauseMenuPanel;
    
    [Tooltip("Arraste o GameObject do painel de Opções (Settings) aqui.")]
    public GameObject optionsMenuPanel;

    [Header("Quit Settings")]
    [Tooltip("Digite o nome exato da cena para onde o botão Quit deve ir (ex: MenuPrincipal).")]
    public string quitSceneName = "MainMenu";

    private bool isPaused = false;

    private void Start()
    {
        // Garante que o jogo começa rodando e os menus fechados
        ResumeGame();
    }

    private void Update()
    {
        // Tecla ESC para abrir/fechar o pause (Compatível com o Input System em modo Both)
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            if (isPaused)
            {
                // Se o player estiver no menu de options, o ESC volta para o pause principal
                if (optionsMenuPanel != null && optionsMenuPanel.activeSelf)
                {
                    CloseOptions();
                }
                else
                {
                    ResumeGame();
                }
            }
            else
            {
                PauseGame();
            }
        }
    }

    /// <summary>
    /// Congela o jogo, silencia os sons e abre o painel de pausa.
    /// </summary>
    public void PauseGame()
    {
        isPaused = true;
        Time.timeScale = 0f;          // Congela o tempo da física e animações
        AudioListener.pause = true;   // Pausa todos os sons do jogo instantaneamente

        if (pauseMenuPanel != null) pauseMenuPanel.SetActive(true);
        if (optionsMenuPanel != null) optionsMenuPanel.SetActive(false);
    }

    /// <summary>
    /// Retorna o jogo ao estado normal, restaurando o tempo e o som.
    /// </summary>
    public void ResumeGame()
    {
        isPaused = false;
        Time.timeScale = 1f;          // Restaura o tempo normal
        AudioListener.pause = false;  // Restaura o som

        if (pauseMenuPanel != null) pauseMenuPanel.SetActive(false);
        if (optionsMenuPanel != null) optionsMenuPanel.SetActive(false);
    }

    /// <summary>
    /// Abre o menu de Opções e fecha o menu de Pause atual.
    /// </summary>
    public void OpenOptions()
    {
        if (pauseMenuPanel != null) pauseMenuPanel.SetActive(false);
        if (optionsMenuPanel != null) optionsMenuPanel.SetActive(true);
    }

    /// <summary>
    /// Fecha o menu de Opções e retorna para o menu de Pause (Botão Back).
    /// </summary>
    public void CloseOptions()
    {
        if (optionsMenuPanel != null) optionsMenuPanel.SetActive(false);
        if (pauseMenuPanel != null) pauseMenuPanel.SetActive(true);
    }

    /// <summary>
    /// Restaura o tempo e muda para a cena configurada no Inspector.
    /// </summary>
    public void QuitToScene()
    {
        // IMPORTANTE: Sempre resete o TimeScale e o Audio antes de mudar de cena!
        Time.timeScale = 1f;
        AudioListener.pause = false;

        // Se você estiver usando o SceneTransitionManager com Fade criado anteriormente, ele usa o fade. Se não, vai direto.
        if (SceneTransitionManager.Instance != null)
        {
            SceneTransitionManager.Instance.LoadScene(quitSceneName);
        }
        else
        {
            SceneManager.LoadScene(quitSceneName);
        }
    }
}