using UnityEngine;
using TMPro;
using System.Collections;

public class TypewriterEffect : MonoBehaviour
{
    [Header("Text Reference")]
    [Tooltip("Arraste o seu TextMeshProUGUI aqui no Inspector")]
    public TextMeshProUGUI targetText;

    [Header("Text Content")]
    [TextArea(3, 10)]
    [Tooltip("Digite aqui o texto que deve aparecer")]
    public string textToType = "Digite seu texto aqui...";

    [Header("Audio Settings")]
    public AudioSource audioSource;
    public AudioClip typeSound;

    [Header("Typewriter Settings")]
    [Range(0.01f, 0.2f)]
    public float letterDelay = 0.05f; // Velocidade que as letras aparecem

    [Header("Shake Effect Settings")]
    public float shakeIntensity = 3.0f; // Intensidade do tremor da última letra

    private Coroutine typingCoroutine;

    void Awake()
    {
        // Se esquecer de arrastar no Inspector, tenta pegar no mesmo objeto automaticamente
        if (targetText == null)
        {
            targetText = GetComponent<TextMeshProUGUI>();
        }

        if (audioSource == null)
        {
            audioSource = GetComponent<AudioSource>();
            if (audioSource == null)
            {
                audioSource = gameObject.AddComponent<AudioSource>();
            }
        }
    }

    // Chamado automaticamente quando a tela/objeto é ATIVADO no Canvas
    void OnEnable()
    {
        if (targetText != null)
        {
            targetText.text = ""; // Limpa tudo ao ativar
            if (typingCoroutine != null)
            {
                StopCoroutine(typingCoroutine);
            }
            typingCoroutine = StartCoroutine(TypeTextRoutine());
        }
    }

    // Chamado automaticamente quando a tela/objeto é DESATIVADO
    void OnDisable()
    {
        if (typingCoroutine != null)
        {
            StopCoroutine(typingCoroutine);
            typingCoroutine = null;
        }

        // Apaga tudo quando sai da tela
        if (targetText != null)
        {
            targetText.text = "";
            targetText.maxVisibleCharacters = 0;
        }
    }

    IEnumerator TypeTextRoutine()
    {
        targetText.text = textToType;
        targetText.maxVisibleCharacters = 0;

        int totalCharacters = textToType.Length;

        for (int i = 0; i <= totalCharacters; i++)
        {
            targetText.maxVisibleCharacters = i;

            // Toca o som a cada letra nova
            if (i > 0 && typeSound != null && audioSource != null)
            {
                audioSource.PlayOneShot(typeSound);
            }

            yield return new WaitForSeconds(letterDelay);
        }

        typingCoroutine = null;
    }

    void LateUpdate()
    {
        // Aplica o efeito de shake na última letra visível enquanto digita
        if (typingCoroutine != null && targetText != null && targetText.maxVisibleCharacters > 0)
        {
            targetText.ForceMeshUpdate();

            TMP_TextInfo textInfo = targetText.textInfo;
            int lastCharIndex = targetText.maxVisibleCharacters - 1;

            if (lastCharIndex < textInfo.characterCount && lastCharIndex >= 0)
            {
                TMP_CharacterInfo charInfo = textInfo.characterInfo[lastCharIndex];

                if (charInfo.isVisible)
                {
                    int materialIndex = charInfo.materialReferenceIndex;
                    int vertexIndex = charInfo.vertexIndex;

                    Vector3 jitter = new Vector3(
                        Random.Range(-shakeIntensity, shakeIntensity),
                        Random.Range(-shakeIntensity, shakeIntensity),
                        0
                    );

                    // Desloca os vértices da última letra para dar o efeito de sacudir
                    textInfo.meshInfo[materialIndex].vertices[vertexIndex + 0] += jitter;
                    textInfo.meshInfo[materialIndex].vertices[vertexIndex + 1] += jitter;
                    textInfo.meshInfo[materialIndex].vertices[vertexIndex + 2] += jitter;
                    textInfo.meshInfo[materialIndex].vertices[vertexIndex + 3] += jitter;

                    for (int i = 0; i < textInfo.meshInfo.Length; i++)
                    {
                        textInfo.meshInfo[i].mesh.vertices = textInfo.meshInfo[i].vertices;
                        targetText.UpdateGeometry(textInfo.meshInfo[i].mesh, i);
                    }
                }
            }
        }
    }
}