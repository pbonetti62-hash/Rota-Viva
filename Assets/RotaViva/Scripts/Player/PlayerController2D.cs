using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// ROTA VIVA - Controlador de player 2D top-down MODULAR com Animador por Array e Som de Passos/Rodas.
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(Collider2D))]
public class PlayerController2D : MonoBehaviour
{
    // ------------------------------------------------------------------
    //  ESTRUTURA DE ANIMACAO POR ARRAY (Editavel no Inspector)
    // ------------------------------------------------------------------

    [Serializable]
    public class AnimationSet
    {
        [Tooltip("Ex: WalkDown, WalkUp, WalkSide, IdleDown, IdleUp, IdleSide")]
        public string animationName = "WalkDown";

        [Tooltip("Tempo em segundos que cada frame fica na tela (ex: 0.12 = ~8 FPS)")]
        [Min(0.01f)] public float frameDuration = 0.12f;

        [Tooltip("Anexar quantos frames quiser para esta animacao")]
        public Sprite[] frames;

        [Tooltip("Repetir a animacao continuamente?")]
        public bool loop = true;
    }

    // ------------------------------------------------------------------
    //  CONFIGURACAO (tudo editavel no Inspector)
    // ------------------------------------------------------------------

    [Header("Persona")]
    [Tooltip("Nome so para identificar (UI, narrador, relatorio).")]
    public string personaName = "Cadeirante";

    [Header("Animacao por Script (Arrays)")]
    [Tooltip("SpriteRenderer do personagem. Se vazio, procura nos filhos.")]
    [SerializeField] private SpriteRenderer spriteRenderer;

    [Tooltip("Crie conjuntos de animacao com os nomes: WalkDown, WalkUp, WalkSide (e opcionalmente IdleDown, IdleUp, IdleSide).")]
    [SerializeField]
    private AnimationSet[] animations = new AnimationSet[]
    {
        new AnimationSet { animationName = "WalkDown", frameDuration = 0.12f },
        new AnimationSet { animationName = "WalkUp",   frameDuration = 0.12f },
        new AnimationSet { animationName = "WalkSide", frameDuration = 0.12f }
    };

    [Header("Som de Passos / Rodas (Sincronizado com Movimento)")]
    [Tooltip("AudioSource do personagem. Se vazio, o script cria/pega um sozinho.")]
    [SerializeField] private AudioSource stepAudioSource;
    [Tooltip("Arraste quantos sons quiser (passos, bengala batendo, rodas girando...). Ele sorteia um a cada passo.")]
    [SerializeField] private AudioClip[] footstepSounds;
    [Tooltip("Distancia em unidades Unity que o jogador precisa andar para tocar o proximo som. Menor = mais frequente.")]
    [Min(0.05f)] public float stepDistance = 0.85f;
    [Tooltip("Volume do som dos passos/rodas.")]
    [Range(0f, 1f)] public float stepVolume = 0.7f;
    [Tooltip("Variacao aleatoria no tom (pitch) para o som nao parecer robotico (0 = sem variacao).")]
    [Range(0f, 0.4f)] public float pitchVariation = 0.08f;

    [Header("Movimento")]
    [Tooltip("Velocidade maxima (unidades Unity por segundo).")]
    [Min(0f)] public float maxSpeed = 4f;
    [Tooltip("Quao rapido chega na velocidade maxima. BAIXO = pesado/lento pra arrancar.")]
    [Min(0.1f)] public float acceleration = 20f;
    [Tooltip("Quao rapido para quando solta a tecla. BAIXO = derrapa.")]
    [Min(0.1f)] public float deceleration = 25f;
    [Tooltip("Desligado = so anda em 4 direcoes (cima/baixo/esq/dir), estilo grade.")]
    public bool allowDiagonal = true;

    [Header("Impulso (opcional) - tecla ESPACO")]
    [Tooltip("Liga o impulso. Ex.: cadeirante da um 'empurrao' rapido nas rodas.")]
    public bool enableBoost = false;
    [Tooltip("Multiplicador de velocidade durante o impulso.")]
    [Min(1f)] public float boostMultiplier = 1.7f;
    [Tooltip("Quanto tempo dura o impulso (segundos).")]
    [Min(0.05f)] public float boostDuration = 0.5f;
    [Tooltip("Tempo ate poder usar de novo (segundos).")]
    [Min(0f)] public float boostCooldown = 4f;
#if ENABLE_LEGACY_INPUT_MANAGER
    [Tooltip("Tecla do impulso.")]
    public KeyCode boostKey = KeyCode.Space;
#endif

    [Header("Controle externo (celular / joystick virtual)")]
    [Tooltip("Ligue se for mandar o input por script usando SetMoveInput().")]
    public bool useExternalInput = false;

    [Header("Eventos (ligue no Inspector: som, narrador, animacao...)")]
    public UnityEvent onStartMoving;
    public UnityEvent onStopMoving;
    public UnityEvent onStep; // Evento extra caso queira soltar particula de poeira a cada passo!
    public UnityEvent onBoost;
    public UnityEvent onBoostReady;

    // ------------------------------------------------------------------
    //  ESTADO PUBLICO (somente leitura - para UI e outros scripts)
    // ------------------------------------------------------------------

    public float CurrentSpeed => Velocity.magnitude;
    public bool IsMoving { get; private set; }
    public bool IsBoosting => Time.time < boostEndTime;
    public bool IsStunned => Time.time < stunEndTime;
    public bool CanMoveNow => canMove && !IsStunned;
    public Vector2 FacingDirection { get; private set; } = Vector2.down;
    public float TotalSpeedMultiplier { get; private set; } = 1f;
    public float BoostReadyNormalized =>
        boostCooldown <= 0f ? 1f : Mathf.Clamp01((Time.time - boostStartTime) / (boostDuration + boostCooldown));

    // ------------------------------------------------------------------
    //  INTERNO
    // ------------------------------------------------------------------

    private class SpeedModifier
    {
        public string id;
        public float multiplier;
        public float expireTime;
    }

    private Rigidbody2D rb;
    private readonly List<SpeedModifier> modifiers = new List<SpeedModifier>();

    private Vector2 moveInput;
    private bool canMove = true;
    private float stunEndTime;
    private float boostEndTime;
    private float boostStartTime = -999f;
    private bool boostWasReady = true;

    // Controle interno da animacao por array
    private AnimationSet currentAnim;
    private int currentFrameIndex;
    private float animTimer;

    // Controle interno dos passos / rodas
    private Vector2 lastStepPosition;
    private float distanceAccumulator;
    private int lastSoundIndex = -1;

    private Vector2 Velocity
    {
        get
        {
#if UNITY_6000_0_OR_NEWER
            return rb.linearVelocity;
#else
            return rb.velocity;
#endif
        }
        set
        {
#if UNITY_6000_0_OR_NEWER
            rb.linearVelocity = value;
#else
            rb.velocity = value;
#endif
        }
    }

    // ------------------------------------------------------------------
    //  UNITY
    // ------------------------------------------------------------------

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();

        rb.gravityScale = 0f;
        rb.freezeRotation = true;
        rb.interpolation = RigidbodyInterpolation2D.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

        if (spriteRenderer == null)
            spriteRenderer = GetComponentInChildren<SpriteRenderer>();

        if (stepAudioSource == null)
        {
            stepAudioSource = GetComponent<AudioSource>();
            if (stepAudioSource == null)
                stepAudioSource = gameObject.AddComponent<AudioSource>();
        }
        stepAudioSource.playOnAwake = false;

        lastStepPosition = rb.position;

        AnimationSet startAnim = FindAnimation("IdleDown") ?? FindAnimation("WalkDown");
        if (startAnim != null && startAnim.frames != null && startAnim.frames.Length > 0)
        {
            currentAnim = startAnim;
            spriteRenderer.sprite = startAnim.frames[0];
        }
    }

    private void Update()
    {
        ReadInput();
        CleanExpiredModifiers();
        UpdateAnimation();
        CheckBoostReady();
    }

    private void FixedUpdate()
    {
        Move();
        UpdateMovingEvents();
        UpdateFootsteps();
    }

    // ------------------------------------------------------------------
    //  INPUT
    // ------------------------------------------------------------------

    private void ReadInput()
    {
        Vector2 input = moveInput;
        bool boostPressed = false;

        if (!useExternalInput)
        {
            input = Vector2.zero;

#if ENABLE_INPUT_SYSTEM
            Keyboard k = Keyboard.current;
            if (k != null)
            {
                input.x = ((k.dKey.isPressed || k.rightArrowKey.isPressed) ? 1f : 0f)
                        - ((k.aKey.isPressed || k.leftArrowKey.isPressed) ? 1f : 0f);
                input.y = ((k.wKey.isPressed || k.upArrowKey.isPressed) ? 1f : 0f)
                        - ((k.sKey.isPressed || k.downArrowKey.isPressed) ? 1f : 0f);
                boostPressed = k.spaceKey.wasPressedThisFrame;
            }
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
            if (input == Vector2.zero)
            {
                input.x = Input.GetAxisRaw("Horizontal");
                input.y = Input.GetAxisRaw("Vertical");
            }
            if (!boostPressed)
            {
                boostPressed = Input.GetKeyDown(boostKey);
            }
#endif
        }

        if (!allowDiagonal && input != Vector2.zero)
        {
            if (Mathf.Abs(input.x) >= Mathf.Abs(input.y)) input.y = 0f;
            else input.x = 0f;
        }

        moveInput = Vector2.ClampMagnitude(input, 1f);

        if (moveInput.sqrMagnitude > 0.01f && CanMoveNow)
            FacingDirection = moveInput.normalized;

        if (boostPressed) TryBoost();
    }

    public void SetMoveInput(Vector2 input) => moveInput = input;

    // ------------------------------------------------------------------
    //  MOVIMENTO
    // ------------------------------------------------------------------

    private void Move()
    {
        Vector2 dir = CanMoveNow ? moveInput : Vector2.zero;

        float speed = maxSpeed * TotalSpeedMultiplier * (IsBoosting ? boostMultiplier : 1f);
        Vector2 target = dir * speed;
        Vector2 current = Velocity;

        float rate = target.sqrMagnitude > current.sqrMagnitude ? acceleration : deceleration;
        Velocity = Vector2.MoveTowards(current, target, rate * Time.fixedDeltaTime);
    }

    private void UpdateMovingEvents()
    {
        bool movingNow = CurrentSpeed > 0.15f;
        if (movingNow && !IsMoving)
        {
            // Ao comecar a andar, prepara para tocar o primeiro passo logo no inicio do movimento
            distanceAccumulator = stepDistance * 0.75f;
            onStartMoving?.Invoke();
        }
        else if (!movingNow && IsMoving)
        {
            distanceAccumulator = 0f;
            onStopMoving?.Invoke();
        }
        IsMoving = movingNow;
    }

    // ------------------------------------------------------------------
    //  SISTEMA DE PASSOS / RODAS POR DISTANCIA
    // ------------------------------------------------------------------

    private void UpdateFootsteps()
    {
        Vector2 currentPos = rb.position;
        float movedThisStep = Vector2.Distance(currentPos, lastStepPosition);
        lastStepPosition = currentPos;

        if (!IsMoving || movedThisStep <= 0.0001f) return;

        distanceAccumulator += movedThisStep;

        if (distanceAccumulator >= stepDistance)
        {
            distanceAccumulator %= stepDistance;
            PlayRandomStepSound();
            onStep?.Invoke();
        }
    }

    private void PlayRandomStepSound()
    {
        if (stepAudioSource == null || footstepSounds == null || footstepSounds.Length == 0) return;

        // Sorteia um som do array sem repetir o ultimo (se houver mais de 1 som)
        int index = 0;
        if (footstepSounds.Length > 1)
        {
            do
            {
                index = UnityEngine.Random.Range(0, footstepSounds.Length);
            } while (index == lastSoundIndex);
        }
        lastSoundIndex = index;

        AudioClip clip = footstepSounds[index];
        if (clip == null) return;

        stepAudioSource.pitch = 1f + UnityEngine.Random.Range(-pitchVariation, pitchVariation);
        stepAudioSource.PlayOneShot(clip, stepVolume);
    }

    /// <summary>Permite trocar o array de sons em tempo real (ex: entrou na areia/calçada quebrada).</summary>
    public void SetFootstepSounds(AudioClip[] newClips, float newStepDistance = -1f)
    {
        footstepSounds = newClips;
        lastSoundIndex = -1;
        if (newStepDistance > 0f) stepDistance = newStepDistance;
    }

    // ------------------------------------------------------------------
    //  IMPULSO
    // ------------------------------------------------------------------

    private void TryBoost()
    {
        if (!enableBoost || !CanMoveNow) return;
        if (Time.time - boostStartTime < boostDuration + boostCooldown) return;

        boostStartTime = Time.time;
        boostEndTime = Time.time + boostDuration;
        boostWasReady = false;
        onBoost?.Invoke();
    }

    private void CheckBoostReady()
    {
        if (!enableBoost || boostWasReady) return;
        if (BoostReadyNormalized >= 1f)
        {
            boostWasReady = true;
            onBoostReady?.Invoke();
        }
    }

    // ------------------------------------------------------------------
    //  API PUBLICA
    // ------------------------------------------------------------------

    public void AddSpeedModifier(string id, float multiplier, float duration = -1f)
    {
        float expire = duration > 0f ? Time.time + duration : -1f;
        SpeedModifier existing = modifiers.Find(m => m.id == id);
        if (existing != null)
        {
            existing.multiplier = multiplier;
            existing.expireTime = expire;
        }
        else
        {
            modifiers.Add(new SpeedModifier { id = id, multiplier = multiplier, expireTime = expire });
        }
        RecalculateMultiplier();
    }

    public void RemoveSpeedModifier(string id)
    {
        modifiers.RemoveAll(m => m.id == id);
        RecalculateMultiplier();
    }

    public void ClearSpeedModifiers()
    {
        modifiers.Clear();
        RecalculateMultiplier();
    }

    public void Stun(float seconds)
    {
        stunEndTime = Mathf.Max(stunEndTime, Time.time + seconds);
    }

    public void SetCanMove(bool value)
    {
        canMove = value;
        if (!value) Velocity = Vector2.zero;
    }

    public void StopImmediately() => Velocity = Vector2.zero;

    public void Teleport(Vector2 position)
    {
        rb.position = position;
        transform.position = position;
        lastStepPosition = position;
        distanceAccumulator = 0f;
        Velocity = Vector2.zero;
        stunEndTime = 0f;
        boostEndTime = 0f;
        ClearSpeedModifiers();
    }

    // ------------------------------------------------------------------
    //  MODIFICADORES (interno)
    // ------------------------------------------------------------------

    private void CleanExpiredModifiers()
    {
        bool changed = false;
        for (int i = modifiers.Count - 1; i >= 0; i--)
        {
            if (modifiers[i].expireTime > 0f && Time.time >= modifiers[i].expireTime)
            {
                modifiers.RemoveAt(i);
                changed = true;
            }
        }
        if (changed) RecalculateMultiplier();
    }

    private void RecalculateMultiplier()
    {
        float total = 1f;
        foreach (SpeedModifier m in modifiers) total *= m.multiplier;
        TotalSpeedMultiplier = Mathf.Max(0f, total);
    }

    // ------------------------------------------------------------------
    //  SISTEMA DE ANIMACAO POR ARRAY
    // ------------------------------------------------------------------

    private void UpdateAnimation()
    {
        if (spriteRenderer == null || animations == null || animations.Length == 0) return;

        bool isWalking = CanMoveNow && moveInput.sqrMagnitude > 0.01f;
        Vector2 d = FacingDirection;

        string dirSuffix = "Down";
        bool flipX = false;

        if (Mathf.Abs(d.x) > Mathf.Abs(d.y))
        {
            dirSuffix = "Side";
            flipX = d.x < 0f;
        }
        else if (d.y > 0f)
        {
            dirSuffix = "Up";
        }

        spriteRenderer.flipX = flipX;

        AnimationSet targetAnim = null;

        if (isWalking)
        {
            targetAnim = FindAnimation("Walk" + dirSuffix) ?? FindAnimation("WalkDown");
        }
        else
        {
            AnimationSet idleAnim = FindAnimation("Idle" + dirSuffix) ?? FindAnimation("IdleDown");
            if (idleAnim != null && idleAnim.frames != null && idleAnim.frames.Length > 0)
            {
                targetAnim = idleAnim;
            }
            else
            {
                AnimationSet walkAnim = FindAnimation("Walk" + dirSuffix) ?? FindAnimation("WalkDown");
                if (walkAnim != null && walkAnim.frames != null && walkAnim.frames.Length > 0)
                {
                    currentAnim = walkAnim;
                    currentFrameIndex = 0;
                    animTimer = 0f;
                    spriteRenderer.sprite = walkAnim.frames[0];
                }
                return;
            }
        }

        if (targetAnim == null || targetAnim.frames == null || targetAnim.frames.Length == 0) return;

        if (currentAnim != targetAnim)
        {
            currentAnim = targetAnim;
            currentFrameIndex = 0;
            animTimer = 0f;
            spriteRenderer.sprite = currentAnim.frames[0];
            return;
        }

        if (currentAnim.frames.Length > 1)
        {
            // Sincroniza a velocidade da animacao proporcionalmente aos modificadores de velocidade!
            float animSpeedScale = Mathf.Max(0.35f, TotalSpeedMultiplier * (IsBoosting ? boostMultiplier : 1f));
            animTimer += Time.deltaTime * animSpeedScale;

            if (animTimer >= currentAnim.frameDuration)
            {
                animTimer -= currentAnim.frameDuration;
                currentFrameIndex++;

                if (currentFrameIndex >= currentAnim.frames.Length)
                {
                    currentFrameIndex = currentAnim.loop ? 0 : currentAnim.frames.Length - 1;
                }

                if (currentAnim.frames[currentFrameIndex] != null)
                {
                    spriteRenderer.sprite = currentAnim.frames[currentFrameIndex];
                }
            }
        }
    }

    public void PlayCustomAnimation(string animName)
    {
        AnimationSet anim = FindAnimation(animName);
        if (anim != null && anim.frames != null && anim.frames.Length > 0)
        {
            currentAnim = anim;
            currentFrameIndex = 0;
            animTimer = 0f;
            spriteRenderer.sprite = anim.frames[0];
        }
    }

    private AnimationSet FindAnimation(string animName)
    {
        if (animations == null) return null;
        for (int i = 0; i < animations.Length; i++)
        {
            if (animations[i] != null &&
                string.Equals(animations[i].animationName, animName, StringComparison.OrdinalIgnoreCase))
            {
                return animations[i];
            }
        }
        return null;
    }

    // ------------------------------------------------------------------
    //  PRESETS (Ja configuram tambem o Step Distance ideal de cada persona)
    // ------------------------------------------------------------------

    [ContextMenu("Preset: Cadeirante")]
    private void PresetCadeirante()
    {
        personaName = "Cadeirante";
        maxSpeed = 3.6f; acceleration = 14f; deceleration = 18f; allowDiagonal = true;
        enableBoost = true; boostMultiplier = 1.7f; boostDuration = 0.5f; boostCooldown = 4f;
        stepDistance = 0.6f; // Som mais continuo para rolamento das rodas
    }

    [ContextMenu("Preset: Mae com carrinho")]
    private void PresetMaeComCarrinho()
    {
        personaName = "Mae com carrinho";
        maxSpeed = 4.2f; acceleration = 9f; deceleration = 7f; allowDiagonal = true;
        enableBoost = false;
        stepDistance = 0.9f; // Passos largos e rapidos
    }

    [ContextMenu("Preset: Baixa visao")]
    private void PresetBaixaVisao()
    {
        personaName = "Baixa visao";
        maxSpeed = 2.4f; acceleration = 30f; deceleration = 40f; allowDiagonal = false;
        enableBoost = false;
        stepDistance = 0.65f; // Passos mais curtos e cautelosos (pode colocar som de passo + toque da bengala!)
    }

    private void OnValidate()
    {
        maxSpeed = Mathf.Max(0f, maxSpeed);
        acceleration = Mathf.Max(0.1f, acceleration);
        deceleration = Mathf.Max(0.1f, deceleration);
        boostMultiplier = Mathf.Max(1f, boostMultiplier);
        stepDistance = Mathf.Max(0.05f, stepDistance);
    }
}