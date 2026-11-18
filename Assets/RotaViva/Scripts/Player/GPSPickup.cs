using UnityEngine;
using UnityEngine.Events;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// ROTA VIVA - Item GPS (totem no mapa).
///
/// COMO USAR:
///  1. Crie um objeto no mapa (sprite do totem) e coloque este script nele.
///     (Ele cria/ajusta o Collider2D como Trigger sozinho.)
///  2. Arraste o objeto "GPS_Rota" (que tem o GPSRouteFollower) no campo Follower.
///  3. (Opcional) Arraste um objeto de aviso "Pressione E" no campo Prompt E.
///
/// O QUE FAZ:
///  Player entra na area -> aparece "Pressione E" -> aperta E -> coleta o item,
///  o totem some e o canvas do GPS abre (com o player parado).
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class GPSPickup : MonoBehaviour
{
    [Header("Obrigatorio")]
    [Tooltip("Objeto que tem o script GPSRouteFollower.")]
    [SerializeField] private GPSRouteFollower follower;

    [Header("Opcional")]
    [Tooltip("Aviso 'Pressione E' (texto, icone...). Aparece so quando o player esta perto.")]
    [SerializeField] private GameObject promptE;

    [Tooltip("Evento ao coletar (ligue um som, por exemplo).")]
    public UnityEvent onCollected;

    private bool playerInside;
    private bool collected;

    private void Reset()
    {
        GetComponent<Collider2D>().isTrigger = true;
    }

    private void Awake()
    {
        GetComponent<Collider2D>().isTrigger = true;
        if (promptE != null) promptE.SetActive(false);
    }

    private void Update()
    {
        if (collected || !playerInside) return;
        if (PressedE()) Collect();
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (collected) return;
        if (other.GetComponentInParent<PlayerController2D>() == null) return;

        playerInside = true;
        if (promptE != null) promptE.SetActive(true);
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        if (other.GetComponentInParent<PlayerController2D>() == null) return;

        playerInside = false;
        if (promptE != null) promptE.SetActive(false);
    }

    private void Collect()
    {
        collected = true;
        if (promptE != null) promptE.SetActive(false);

        onCollected?.Invoke();

        if (follower != null) follower.OpenGPS();
        else Debug.LogError("GPSPickup: arraste o GPSRouteFollower no campo Follower!", this);

        gameObject.SetActive(false); // totem some do mapa
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
}
