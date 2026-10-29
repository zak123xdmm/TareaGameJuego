using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Controlador de Cheems para Endless Runner 3D (corre hacia +Z).
///
/// ESTRUCTURA CORRECTA:
///    CheemsPlayer  (este script + Rigidbody + CapsuleCollider, pivote en los pies)
///      └─ Cheems   (el modelo 3D, campo "Visual")
/// El menú Tools > Runner > Configurar escena arma todo esto solo.
///
/// - El collider se ajusta SOLO al tamaño del modelo (así no se hunde en el camino).
/// - Flecha Arriba / W / Espacio = saltar, Flecha Abajo / S = agacharse (el collider se reduce).
/// - Usa el Animator con los parámetros: Jump (Trigger), IsCrouching (Bool), Grounded (Bool), Running (Bool), Speed (Float).
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(CapsuleCollider))]
public class RunnerPlayer : MonoBehaviour
{
    [Header("Componentes")]
    [Tooltip("Arrastra aquí el Animator del modelo. Si está vacío se busca en los hijos.")]
    public Animator animator;

    [Header("Movimiento")]
    public float forwardSpeed = 8f;
    public float maxSpeed = 20f;
    [Tooltip("Cuánto sube la velocidad cada segundo (dificultad progresiva).")]
    public float speedIncreasePerSecond = 0.15f;
    [Tooltip("Normalmente apagado: el GameManager inicia la carrera con el botón.")]
    public bool runOnStart = false;

    [Header("Salto")]
    [Tooltip("Altura máxima del salto en metros.")]
    public float jumpHeight = 2.2f;
    [Tooltip("Más alto = salto más rápido y 'seco'.")]
    public float gravityMultiplier = 2.5f;
    [Tooltip("Velocidad de caída al presionar Flecha Abajo en el aire.")]
    public float fastFallSpeed = 18f;
    public float jumpBufferTime = 0.15f;
    [Tooltip("Da una voltereta durante el salto (solo animación procedural si no hay Animator).")]
    public bool flipOnJump = true;

    [Header("Agacharse")]
    [Tooltip("Segundos que dura la postura agachada (si 'Hold To Crouch' está apagado).")]
    public float crouchDuration = 0.8f;
    [Tooltip("Si está activo, se mantiene agachado mientras se mantiene la tecla.")]
    public bool holdToCrouch = false;
    [Tooltip("Proporción de la altura del collider al agacharse.")]
    [Range(0.3f, 0.8f)] public float crouchHeightFactor = 0.5f;

    [Header("Vidas y choques")]
    public int lives = 3;
    public float invulnerableTime = 1.5f;
    [Tooltip("Apágalo si otro script ya maneja los choques.")]
    public bool handleHitsHere = true;
    public UnityEvent<int> onHit;      // vidas restantes
    public UnityEvent onDied;
    public UnityEvent<int> onPickup;   // puntos de la hamburguesa

    [Header("Collider")]
    [Tooltip("Ajusta el collider al modelo al iniciar (evita que el personaje se hunda).")]
    public bool autoFitCollider = true;
    [Range(0.4f, 1f)] public float colliderRadiusFactor = 0.7f;

    [Header("Visual")]
    [Tooltip("Hijo que contiene el modelo. Si está vacío se usa el primer hijo con Renderer.")]
    public Transform visual;
    public bool forceProceduralAnimation = false;
    public float runCycleSpeed = 12f;
    public float bobHeight = 0.12f;

    [Header("Parámetros del Animator")]
    public string paramJump = "Jump";             // Trigger
    public string paramCrouch = "IsCrouching";     // Bool
    public string paramDie = "Die";                // Trigger
    public string paramGrounded = "Grounded";      // Bool
    public string paramRunning = "Running";        // Bool
    public string paramSpeed = "Speed";            // Float

    // --- Lectura (spawner, UI, etc.) ---
    public float CurrentSpeed { get { return currentSpeed; } }
    public bool IsDead { get { return dead; } }
    public bool IsRunning { get { return running; } }
    public int Lives { get { return lives; } }
    public bool IsGrounded { get { return grounded; } }
    public bool IsCrouching { get { return crouching; } }
    public float StandHeight { get { return standHeight * Mathf.Abs(transform.lossyScale.y); } }
    public float CrouchHeight { get { return StandHeight * crouchHeightFactor; } }

    // --- Estado interno ---
    Rigidbody rb;
    CapsuleCollider cap;
    Animator anim;
    Renderer[] renderers;
    bool hasSkeleton;

    float currentSpeed;
    bool running, dead, celebrating;
    bool grounded, groundContact;
    float groundedUntil;
    const float groundGrace = 0.08f;

    float jumpBufferUntil = -1f;
    bool crouchRequested, crouchHeld, crouching, crouchQueued;
    float crouchEndTime;
    float invulnerableUntil;
    bool blinking;

    float standHeight, standRadius;
    Vector3 standCenter;

    // animación procedural
    Vector3 baseScale, basePos, feetLocal, centerLocal;
    Quaternion baseRot = Quaternion.identity;
    Vector3 curScale = Vector3.one;
    float curPitch, curRoll, curYaw, landSquash, hitShake, flipPitch;
    bool jumping;
    float airTime, totalAir;

    readonly Dictionary<string, AnimatorControllerParameterType> animParams =
        new Dictionary<string, AnimatorControllerParameterType>();

    // ------------------------------------------------------------------ inicialización

    void Reset()
    {
        FitCollider(GetComponent<CapsuleCollider>());
    }

    [ContextMenu("Ajustar collider al modelo")]
    void FitColliderMenu()
    {
        CapsuleCollider c = GetComponent<CapsuleCollider>();
#if UNITY_EDITOR
        UnityEditor.Undo.RecordObject(c, "Ajustar collider al modelo");
#endif
        FitCollider(c);
    }

    bool TryGetBounds(Transform root, out Bounds b)
    {
        b = new Bounds();
        bool any = false;
        foreach (Renderer r in root.GetComponentsInChildren<Renderer>())
        {
            if (r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;
            if (!any) { b = r.bounds; any = true; }
            else b.Encapsulate(r.bounds);
        }
        return any;
    }

    void FitCollider(CapsuleCollider c)
    {
        if (c == null) return;
        Bounds b;
        if (!TryGetBounds(transform, out b)) return;

        Vector3 s = transform.lossyScale;
        float sx = Mathf.Max(0.0001f, Mathf.Abs(s.x));
        float sy = Mathf.Max(0.0001f, Mathf.Abs(s.y));
        float sz = Mathf.Max(0.0001f, Mathf.Abs(s.z));

        c.direction = 1;
        c.height = b.size.y / sy;
        c.center = transform.InverseTransformPoint(b.center);
        c.radius = Mathf.Min(b.extents.x / sx, b.extents.z / sz) * colliderRadiusFactor;
    }

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        cap = GetComponent<CapsuleCollider>();

        rb.useGravity = false; // la gravedad la aplica este script (gravityMultiplier)
        rb.constraints = RigidbodyConstraints.FreezeRotation | RigidbodyConstraints.FreezePositionX;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        rb.sleepThreshold = 0f;

        if (autoFitCollider) FitCollider(cap);
        cap.direction = 1;
        standHeight = cap.height;
        standRadius = cap.radius;
        standCenter = cap.center;

        renderers = GetComponentsInChildren<Renderer>(true);

        // Animator: el asignado a mano tiene prioridad; si no, se busca en este objeto o en los hijos.
        anim = animator != null ? animator : GetComponent<Animator>();
        if (anim == null) anim = GetComponentInChildren<Animator>();

        if (visual == null)
        {
            foreach (Transform ch in transform)
            {
                if (ch.GetComponentInChildren<Renderer>() != null) { visual = ch; break; }
            }
        }

        bool hasController = anim != null && anim.runtimeAnimatorController != null;
        // Forzamos el uso del esqueleto/Animator si hay un controlador asignado y no se forzó proceduralmente
        hasSkeleton = !forceProceduralAnimation && hasController;

        if (hasSkeleton)
        {
            anim.enabled = true;
            anim.applyRootMotion = false; // el movimiento lo hace el Rigidbody
            animParams.Clear();
            foreach (AnimatorControllerParameter p in anim.parameters)
            {
                animParams[p.name] = p.type;
            }
        }
        else
        {
            if (anim != null) anim.enabled = false; // que no pelee con la animación por código
            if (visual != null)
            {
                baseScale = visual.localScale;
                basePos = visual.localPosition;
                baseRot = visual.localRotation;

                Transform vp = visual.parent != null ? visual.parent : transform;
                Bounds vb;
                if (TryGetBounds(visual, out vb))
                {
                    feetLocal = vp.InverseTransformPoint(new Vector3(vb.center.x, vb.min.y, vb.center.z));
                    centerLocal = vp.InverseTransformPoint(vb.center);
                }
                else
                {
                    feetLocal = basePos;
                    centerLocal = basePos;
                }
            }
        }

        Debug.Log("[RunnerPlayer] Animación: " +
                  (hasSkeleton ? "Animator (controlador asignado)" : "Procedural (sin Animator/controlador)"));
    }

    void Start()
    {
        currentSpeed = forwardSpeed;
        running = runOnStart;
    }

    // ------------------------------------------------------------------ API pública

    /// <summary>Coloca al personaje con los pies sobre el punto indicado.</summary>
    public void Teleport(Vector3 groundPoint)
    {
        float bottomLocal = standCenter.y - standHeight * 0.5f;
        Vector3 pos = groundPoint - Vector3.up * (bottomLocal * Mathf.Abs(transform.lossyScale.y)) + Vector3.up * 0.05f;
        rb.position = pos;
        transform.position = pos;
        rb.linearVelocity = Vector3.zero;
        Physics.SyncTransforms();
        grounded = false;
        groundedUntil = 0f;
    }

    public void SetLives(int n) { lives = Mathf.Max(0, n); }

    public void BeginRun()
    {
        currentSpeed = forwardSpeed;
        running = true;
        celebrating = false;
    }

    public void StopRun() { running = false; }

    public void Celebrate()
    {
        running = false;
        celebrating = true;
    }

    public void ResetState()
    {
        dead = false; celebrating = false; running = false;
        crouching = false; crouchQueued = false; crouchRequested = false;
        jumpBufferUntil = -1f; invulnerableUntil = 0f;
        currentSpeed = forwardSpeed;
        jumping = false; flipPitch = 0f; landSquash = 0f; hitShake = 0f;
        curScale = Vector3.one; curPitch = 0f; curRoll = 0f; curYaw = 0f;
        blinking = false;
        SetRenderers(true);
        if (rb != null) rb.linearVelocity = Vector3.zero;
        ApplyColliderShape();
        if (hasSkeleton && anim != null)
        {
            anim.Rebind();
            anim.Update(0f);
        }
    }

    // ------------------------------------------------------------------ entrada

    static bool JumpDown()
    {
#if ENABLE_INPUT_SYSTEM
        Keyboard k = Keyboard.current;
        return k != null && (k.upArrowKey.wasPressedThisFrame || k.wKey.wasPressedThisFrame || k.spaceKey.wasPressedThisFrame);
#else
        return Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.W) || Input.GetKeyDown(KeyCode.Space);
#endif
    }

    static bool CrouchDown()
    {
#if ENABLE_INPUT_SYSTEM
        Keyboard k = Keyboard.current;
        return k != null && (k.downArrowKey.wasPressedThisFrame || k.sKey.wasPressedThisFrame);
#else
        return Input.GetKeyDown(KeyCode.DownArrow) || Input.GetKeyDown(KeyCode.S);
#endif
    }

    static bool CrouchHeld()
    {
#if ENABLE_INPUT_SYSTEM
        Keyboard k = Keyboard.current;
        return k != null && (k.downArrowKey.isPressed || k.sKey.isPressed);
#else
        return Input.GetKey(KeyCode.DownArrow) || Input.GetKey(KeyCode.S);
#endif
    }

    // ------------------------------------------------------------------ bucle

    void Update()
    {
        float dt = Time.deltaTime;

        if (!dead && running)
        {
            currentSpeed = Mathf.Min(maxSpeed, currentSpeed + speedIncreasePerSecond * dt);
            if (JumpDown()) jumpBufferUntil = Time.time + jumpBufferTime;
            if (CrouchDown()) crouchRequested = true;
            crouchHeld = CrouchHeld();
        }

        UpdateBlink();
        if (hasSkeleton) UpdateAnimator();
        else UpdateProcedural(dt);
    }

    void FixedUpdate()
    {
        float dt = Time.fixedDeltaTime;
        Vector3 v = rb.linearVelocity;

        // --- Suelo ---
        bool wasGrounded = grounded;
        if (groundContact && v.y <= 0.5f) groundedUntil = Time.time + groundGrace;
        groundContact = false;
        grounded = Time.time <= groundedUntil;
        if (grounded && !wasGrounded) OnLanded();

        // --- Gravedad propia ---
        v.y += Physics.gravity.y * gravityMultiplier * dt;
        v.y = Mathf.Max(v.y, -60f);

        // --- Acciones ---
        if (!dead && running)
        {
            if (grounded && Time.time <= jumpBufferUntil)
            {
                jumpBufferUntil = -1f;
                DoJump(ref v);
            }

            if (crouchRequested)
            {
                crouchRequested = false;
                if (grounded) StartCrouch();
                else
                {
                    crouchQueued = true;      // se agacha al aterrizar
                    v.y = -fastFallSpeed;     // y cae rápido
                }
            }
        }
        else crouchRequested = false;

        if (crouching)
        {
            bool keep = !dead && running && (holdToCrouch ? crouchHeld : Time.time < crouchEndTime);
            if (!keep) StopCrouch();
        }

        // --- Avance ---
        v.x = 0f;
        if (running && !dead) v.z = currentSpeed;
        else v.z = Mathf.MoveTowards(v.z, 0f, 40f * dt);

        rb.linearVelocity = v;
        ApplyColliderShape();
    }

    // ------------------------------------------------------------------ acciones

    void DoJump(ref Vector3 v)
    {
        if (crouching) StopCrouch();
        float g = -Physics.gravity.y * gravityMultiplier;
        v.y = Mathf.Sqrt(2f * g * jumpHeight);
        grounded = false;
        groundedUntil = 0f;
        jumping = true;
        airTime = 0f;
        totalAir = 2f * v.y / Mathf.Max(0.01f, g);
        SetTrigger(paramJump);
        SetBool(paramGrounded, false);
    }

    void StartCrouch()
    {
        crouching = true;
        crouchEndTime = Time.time + crouchDuration;
        SetBool(paramCrouch, true);
    }

    void StopCrouch()
    {
        crouching = false;
        SetBool(paramCrouch, false);
    }

    void OnLanded()
    {
        landSquash = 1f;
        jumping = false;
        flipPitch = 0f;
        SetBool(paramGrounded, true);
        if (crouchQueued)
        {
            crouchQueued = false;
            if (!holdToCrouch || crouchHeld) StartCrouch();
        }
    }

    // El collider se reduce desde arriba: los pies se quedan en el mismo lugar.
    void ApplyColliderShape()
    {
        float h = crouching ? standHeight * crouchHeightFactor : standHeight;
        if (Mathf.Approximately(cap.height, h)) return;
        cap.height = h;
        cap.radius = Mathf.Min(standRadius, h * 0.5f);
        cap.center = standCenter - Vector3.up * ((standHeight - h) * 0.5f);
    }

    // ------------------------------------------------------------------ vidas, choques y objetos

    public void TakeHit()
    {
        if (dead || Time.time < invulnerableUntil) return;
        lives = Mathf.Max(0, lives - 1);
        invulnerableUntil = Time.time + invulnerableTime;
        hitShake = 1f;
        if (onHit != null) onHit.Invoke(lives);
        if (lives <= 0) Die();
    }

    public void Die()
    {
        if (dead) return;
        dead = true;
        running = false;
        crouching = false;
        crouchQueued = false;
        SetBool(paramCrouch, false);
        SetTrigger(paramDie);
        if (onDied != null) onDied.Invoke();
    }

    void HandleObstacle(Collider other, RunnerObstacle ob)
    {
        if (!handleHitsHere || ob == null) return;
        // Atraviesa el obstáculo golpeado para que no frene ni empuje al personaje.
        if (!other.isTrigger) Physics.IgnoreCollision(cap, other, true);
        TakeHit();
    }

    void OnCollisionEnter(Collision c)
    {
        CheckGround(c);
        HandleObstacle(c.collider, c.collider.GetComponentInParent<RunnerObstacle>());
    }

    void OnCollisionStay(Collision c)
    {
        CheckGround(c);
    }

    void OnTriggerEnter(Collider other)
    {
        RunnerObstacle ob = other.GetComponentInParent<RunnerObstacle>();
        if (ob != null) { HandleObstacle(other, ob); return; }

        RunnerPickup pk = other.GetComponentInParent<RunnerPickup>();
        if (pk != null && !pk.collected && !dead)
        {
            int pts = pk.points;
            pk.Collect();
            if (onPickup != null) onPickup.Invoke(pts);
        }
    }

    void CheckGround(Collision c)
    {
        for (int i = 0; i < c.contactCount; i++)
        {
            if (c.GetContact(i).normal.y > 0.5f)
            {
                groundContact = true;
                return;
            }
        }
    }

    // ------------------------------------------------------------------ visual

    void UpdateBlink()
    {
        bool inv = !dead && Time.time < invulnerableUntil;
        if (inv)
        {
            SetRenderers(((int)(Time.time * 14f) & 1) == 0);
            blinking = true;
        }
        else if (blinking)
        {
            SetRenderers(true);
            blinking = false;
        }
    }

    void SetRenderers(bool on)
    {
        if (renderers == null) return;
        for (int i = 0; i < renderers.Length; i++)
            if (renderers[i] != null) renderers[i].enabled = on;
    }

    void UpdateAnimator()
    {
        SetFloat(paramSpeed, currentSpeed);
        SetBool(paramGrounded, grounded);
        SetBool(paramRunning, running && !dead);
    }

    // Animación por código para modelos SIN huesos.
    void UpdateProcedural(float dt)
    {
        if (visual == null) return;

        Vector3 tScale = Vector3.one;
        float tPitch = 0f, tRoll = 0f, tYaw = 0f, bob = 0f, lift = 0f, directYaw = 0f;
        float vy = rb.linearVelocity.y;
        float t = Time.time;
        flipPitch = 0f;

        if (dead)
        {
            tRoll = 90f;                      // cae de lado
            lift = standRadius * 0.8f;
        }
        else if (celebrating)
        {
            bob = Mathf.Abs(Mathf.Sin(t * 7f)) * 0.45f;         // saltitos de alegría
            directYaw = (t * 280f) % 360f;                     // giro
            tScale = new Vector3(0.95f, 1.08f, 0.95f);
        }
        else if (!running)
        {
            // Idle: respira y mira a los lados mientras espera en la salida
            float breathe = Mathf.Sin(t * 2.6f);
            tScale = new Vector3(1f - 0.015f * breathe, 1f + 0.03f * breathe, 1f - 0.015f * breathe);
            tRoll = Mathf.Sin(t * 1.3f) * 2.5f;
            tYaw = Mathf.Sin(t * 0.9f) * 8f;
        }
        else if (!grounded)
        {
            if (flipOnJump && jumping && totalAir > 0.05f)
            {
                airTime += dt;
                flipPitch = Mathf.Clamp01(airTime / totalAir) * 360f;   // voltereta hacia adelante
            }
            else
            {
                tScale = new Vector3(0.92f, 1.12f, 0.92f);
                tPitch = Mathf.Clamp(-vy * 2f, -25f, 25f);
            }
        }
        else if (crouching)
        {
            tScale = new Vector3(1.15f, crouchHeightFactor, 1.15f);   // aplastado
            tPitch = 22f;                                     // inclinado al deslizar
        }
        else
        {
            float rate = runCycleSpeed * Mathf.Clamp(currentSpeed / Mathf.Max(0.1f, forwardSpeed), 0.8f, 2f);
            float phase = t * rate;
            bob = Mathf.Abs(Mathf.Sin(phase)) * bobHeight;            // saltitos al correr
            tRoll = Mathf.Sin(phase) * 7f;                            // balanceo
            tYaw = Mathf.Sin(phase) * 6f;                             // contoneo
            tPitch = 8f;                                              // inclinado hacia adelante
            float squash = Mathf.Sin(phase * 2f) * 0.04f;
            tScale = new Vector3(1f - squash * 0.5f, 1f + squash, 1f - squash * 0.5f);
        }

        // golpe: sacudida y aplastón
        hitShake = Mathf.MoveTowards(hitShake, 0f, dt * 2.5f);
        if (hitShake > 0f)
        {
            tRoll += Mathf.Sin(t * 60f) * 14f * hitShake;
            tScale.y *= 1f - 0.2f * hitShake;
        }

        // aterrizaje: aplastón que rebota
        landSquash = Mathf.MoveTowards(landSquash, 0f, dt * 4f);
        tScale.y *= 1f - 0.3f * landSquash;
        tScale.x *= 1f + 0.18f * landSquash;
        tScale.z *= 1f + 0.18f * landSquash;

        float k = 1f - Mathf.Exp(-18f * dt);
        curScale = Vector3.Lerp(curScale, tScale, k);
        curPitch = Mathf.Lerp(curPitch, tPitch, k);
        curRoll = Mathf.Lerp(curRoll, tRoll, k);
        curYaw = Mathf.Lerp(curYaw, tYaw, k);

        // En el aire gira alrededor del centro del cuerpo; en el suelo, alrededor de los pies.
        Vector3 pivot = (grounded && flipPitch <= 0.01f) ? feetLocal : centerLocal;
        Quaternion rot = Quaternion.Euler(curPitch + flipPitch, curYaw + directYaw, curRoll);

        visual.localScale = Vector3.Scale(baseScale, ToLocalScale(curScale));
        visual.localRotation = rot * baseRot;
        visual.localPosition = pivot + rot * Vector3.Scale(basePos - pivot, curScale) + Vector3.up * (bob + lift);
    }

    // Convierte una escala "en ejes del padre" a escala local del modelo (por si el modelo viene girado).
    Vector3 ToLocalScale(Vector3 sp)
    {
        Vector3 ux = baseRot * Vector3.right;
        Vector3 uy = baseRot * Vector3.up;
        Vector3 uz = baseRot * Vector3.forward;
        return new Vector3(AxisScale(ux, sp), AxisScale(uy, sp), AxisScale(uz, sp));
    }

    static float AxisScale(Vector3 u, Vector3 sp)
    {
        return Mathf.Sqrt(u.x * u.x * sp.x * sp.x + u.y * u.y * sp.y * sp.y + u.z * u.z * sp.z * sp.z);
    }

    // ------------------------------------------------------------------ helpers de Animator

    void SetBool(string name, bool value)
    {
        AnimatorControllerParameterType t;
        if (hasSkeleton && anim != null && animParams.TryGetValue(name, out t) && t == AnimatorControllerParameterType.Bool)
            anim.SetBool(name, value);
    }

    void SetTrigger(string name)
    {
        AnimatorControllerParameterType t;
        if (hasSkeleton && anim != null && animParams.TryGetValue(name, out t) && t == AnimatorControllerParameterType.Trigger)
            anim.SetTrigger(name);
    }

    void SetFloat(string name, float value)
    {
        AnimatorControllerParameterType t;
        if (hasSkeleton && anim != null && animParams.TryGetValue(name, out t) && t == AnimatorControllerParameterType.Float)
            anim.SetFloat(name, value);
    }
}