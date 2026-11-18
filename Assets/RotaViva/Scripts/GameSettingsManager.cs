using UnityEngine;
using UnityEngine.UI;
using TMPro; // Biblioteca padrão de texto do Unity 6 (TextMeshPro)

public class GameSettingsManager : MonoBehaviour
{
    public enum UIControlType { Slider, Scrollbar }

    [Header("--- MUSIC SETTINGS ---")]
    public UIControlType musicUIType = UIControlType.Slider;
    public Slider musicSlider;
    public Scrollbar musicScrollbar;
    public TextMeshProUGUI musicPercentageText; // Arraste o TextMeshPro da porcentagem aqui

    [Header("--- AMBIENCE SETTINGS ---")]
    public UIControlType ambienceUIType = UIControlType.Slider;
    public Slider ambienceSlider;
    public Scrollbar ambienceScrollbar;
    public TextMeshProUGUI ambiencePercentageText; // Arraste o TextMeshPro da porcentagem aqui

    [Header("--- SFX SETTINGS ---")]
    public UIControlType sfxUIType = UIControlType.Slider;
    public Slider sfxSlider;
    public Scrollbar sfxScrollbar;
    public TextMeshProUGUI sfxPercentageText; // Arraste o TextMeshPro da porcentagem aqui

    [Header("--- SCREEN MODE BUTTONS (Arraste aqui) ---")]
    public Button fullScreenButton;
    public Button windowedButton;

    // Chaves de salvamento
    private const string MUSIC_KEY = "MusicVolume";
    private const string AMBIENCE_KEY = "AmbienceVolume";
    private const string SFX_KEY = "SFXVolume";

    private void Awake()
    {
        // Carrega os volumes salvos no disco (funciona ao fechar e abrir o jogo)
        float musicVol = PlayerPrefs.GetFloat(MUSIC_KEY, 1f);
        float ambienceVol = PlayerPrefs.GetFloat(AMBIENCE_KEY, 1f);
        float sfxVol = PlayerPrefs.GetFloat(SFX_KEY, 1f);

        // Inicializa a UI e os textos de porcentagem com os valores salvos
        InitUIElement(musicUIType, musicSlider, musicScrollbar, musicPercentageText, musicVol);
        InitUIElement(ambienceUIType, ambienceSlider, ambienceScrollbar, ambiencePercentageText, ambienceVol);
        InitUIElement(sfxUIType, sfxSlider, sfxScrollbar, sfxPercentageText, sfxVol);

        // Aplica o som inicial em todos os objetos marcados com as tags
        ApplyVolume("Music", musicVol);
        ApplyVolume("Ambience", ambienceVol);
        ApplyVolume("SFX", sfxVol);
    }

    private void Start()
    {
        SetupListeners();
        SetupButtons();
    }

    private void OnEnable()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDisable()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
    {
        // Garante que a nova cena receba o volume correto salvo
        ApplyVolume("Music", GetCurrentValue(musicUIType, musicSlider, musicScrollbar));
        ApplyVolume("Ambience", GetCurrentValue(ambienceUIType, ambienceSlider, ambienceScrollbar));
        ApplyVolume("SFX", GetCurrentValue(sfxUIType, sfxSlider, sfxScrollbar));
    }

    private void SetupListeners()
    {
        if (musicUIType == UIControlType.Slider && musicSlider != null)
            musicSlider.onValueChanged.AddListener((val) => OnVolumeChanged("Music", val, MUSIC_KEY, musicPercentageText));
        else if (musicUIType == UIControlType.Scrollbar && musicScrollbar != null)
            musicScrollbar.onValueChanged.AddListener((val) => OnVolumeChanged("Music", val, MUSIC_KEY, musicPercentageText));

        if (ambienceUIType == UIControlType.Slider && ambienceSlider != null)
            ambienceSlider.onValueChanged.AddListener((val) => OnVolumeChanged("Ambience", val, AMBIENCE_KEY, ambiencePercentageText));
        else if (ambienceUIType == UIControlType.Scrollbar && ambienceScrollbar != null)
            ambienceScrollbar.onValueChanged.AddListener((val) => OnVolumeChanged("Ambience", val, AMBIENCE_KEY, ambiencePercentageText));

        if (sfxUIType == UIControlType.Slider && sfxSlider != null)
            sfxSlider.onValueChanged.AddListener((val) => OnVolumeChanged("SFX", val, SFX_KEY, sfxPercentageText));
        else if (sfxUIType == UIControlType.Scrollbar && sfxScrollbar != null)
            sfxScrollbar.onValueChanged.AddListener((val) => OnVolumeChanged("SFX", val, SFX_KEY, sfxPercentageText));
    }

    private void OnVolumeChanged(string tag, float value, string prefKey, TextMeshProUGUI percentageText)
    {
        PlayerPrefs.SetFloat(prefKey, value);
        PlayerPrefs.Save(); // Força o salvamento imediato no PlayerPrefs
        ApplyVolume(tag, value);
        UpdatePercentageDisplay(percentageText, value);
    }

    private void InitUIElement(UIControlType type, Slider slider, Scrollbar scrollbar, TextMeshProUGUI percentageText, float value)
    {
        if (type == UIControlType.Slider && slider != null)
        {
            slider.SetValueWithoutNotify(value);
        }
        else if (type == UIControlType.Scrollbar && scrollbar != null)
        {
            scrollbar.value = value;
        }

        UpdatePercentageDisplay(percentageText, value);
    }

    private void UpdatePercentageDisplay(TextMeshProUGUI textComponent, float value)
    {
        if (textComponent != null)
        {
            int percentage = Mathf.RoundToInt(value * 100f);
            textComponent.text = percentage + "%";
        }
    }

    private float GetCurrentValue(UIControlType type, Slider slider, Scrollbar scrollbar)
    {
        if (type == UIControlType.Slider && slider != null) return slider.value;
        if (type == UIControlType.Scrollbar && scrollbar != null) return scrollbar.value;
        return 1f;
    }

    private void ApplyVolume(string tag, float volume)
    {
        GameObject[] taggedObjects = GameObject.FindGameObjectsWithTag(tag);
        foreach (GameObject obj in taggedObjects)
        {
            AudioSource audioSource = obj.GetComponent<AudioSource>();
            if (audioSource != null)
            {
                audioSource.volume = volume;
            }
        }
    }

    private void SetupButtons()
    {
        if (fullScreenButton != null)
        {
            fullScreenButton.onClick.RemoveAllListeners();
            fullScreenButton.onClick.AddListener(() => SetFullScreenMode());
        }

        if (windowedButton != null)
        {
            windowedButton.onClick.RemoveAllListeners();
            windowedButton.onClick.AddListener(() => SetWindowedMode());
        }
    }

    public void SetWindowedMode()
    {
        Screen.fullScreenMode = FullScreenMode.Windowed;
    }

    public void SetFullScreenMode()
    {
        Screen.fullScreenMode = FullScreenMode.ExclusiveFullScreen;
    }
}