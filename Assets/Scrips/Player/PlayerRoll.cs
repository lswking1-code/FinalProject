using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 枪械 Player 翻滚：全身动画、带实体碰撞的短抛物线，期间无敌。
/// 仅挂在 Player prefab，勿挂到 PlayerMachinist。
/// </summary>
[DefaultExecutionOrder(100)]
[RequireComponent(typeof(PlayerAnimBase))]
[RequireComponent(typeof(PlayerMovement))]
[RequireComponent(typeof(Character))]
[RequireComponent(typeof(PhysicsCheck))]
[RequireComponent(typeof(Rigidbody2D))]
public class PlayerRoll : MonoBehaviour
{
    [Header("翻滚")]
    [SerializeField, Min(0.01f)] float rollDuration = 5f / 12f;
    [SerializeField, Min(0f)] float rollDistance = 2.45f;
    [SerializeField, Min(0f)] float rollHeight = 0.25f;
    [SerializeField] float rollCooldown = 1f;

    InputSystem_Actions actions;
    PlayerAnimBase playerAnim;
    PlayerMovement playerMovement;
    Character character;
    PhysicsCheck physicsCheck;
    Rigidbody2D rb;

    float cooldownTimer;
    float rollTimer;
    float rollFaceDir = 1f;
    float duration;
    float acceleration;
    float savedGravity;
    CollisionDetectionMode2D savedCollisionMode;

    public bool IsRolling { get; private set; }

    /// <summary>0 = 冷却结束可用，1 = 刚进入冷却。</summary>
    public float CooldownNormalized =>
        rollCooldown <= 0f ? 0f : Mathf.Clamp01(cooldownTimer / rollCooldown);

    void Awake()
    {
        actions = new InputSystem_Actions();
        playerAnim = PlayerAnimBase.Resolve(gameObject);
        playerMovement = GetComponent<PlayerMovement>();
        character = GetComponent<Character>();
        physicsCheck = GetComponent<PhysicsCheck>();
        rb = GetComponent<Rigidbody2D>();
    }

    void OnEnable() => actions.Player.Enable();

    void OnDisable()
    {
        if (IsRolling)
            EndRoll(startCooldown: false, completed: false);

        actions.Player.Disable();
    }

    void OnDestroy() => actions?.Dispose();

    void Update()
    {
        if (cooldownTimer > 0f)
            cooldownTimer -= Time.deltaTime;

        if (IsRolling)
        {
            if (playerAnim.IsDead || character.IsDead || !playerAnim.IsRolling
                || playerMovement.IsExternallyControlled)
                EndRoll(startCooldown: true, completed: false);
            else if (!GameplayPause.IsPaused && rollTimer >= duration && playerAnim.IsRollAnimationComplete)
                EndRoll(startCooldown: true, completed: true);
            return;
        }

        if (!CanStartRoll())
            return;

        if (!actions.Player.Ability2.WasPressedThisFrame())
            return;

        StartRoll();
    }

    void FixedUpdate()
    {
        if (!IsRolling || playerMovement.IsActionLocked || playerAnim.IsDead || character.IsDead)
            return;

        float step = Mathf.Min(Time.fixedDeltaTime, Mathf.Max(0f, duration - rollTimer));
        physicsCheck.Check();
        bool wall = rollFaceDir > 0f ? physicsCheck.touchRightWall : physicsCheck.touchLeftWall;
        float speed = wall ? 0f : rollFaceDir * rollDistance / duration;
        // Integrate velocity, not position: the collision solver can stop ascent at ceilings.
        float velocityY = rb.linearVelocity.y - acceleration * step;
        rb.linearVelocity = new Vector2(speed * step / Time.fixedDeltaTime, velocityY);
        rollTimer += step;
    }

    bool CanStartRoll()
    {
        if (playerMovement.IsActionLocked || playerAnim.IsDead || character.IsDead)
            return false;

        if (cooldownTimer > 0f)
            return false;

        physicsCheck.Check();
        if (!physicsCheck.isSolidGround || playerMovement.DidGroundJumpThisFixedUpdate
            || actions.Player.Jump.WasPressedThisFrame()
            || (rb.linearVelocity.y > 0.01f && !playerMovement.CanLandOnSlopeWhileAscending))
            return false;

        if (playerAnim.IsThrowing || playerAnim.IsMelee || playerAnim.IsSwitchingWeapon || playerAnim.IsRecalling)
            return false;

        if (playerAnim.IsDispatching || playerAnim.IsCharging || playerAnim.IsHeavySpinFiring
            || playerAnim.IsPlayingMachinistChargeShoot)
            return false;

        return true;
    }

    void StartRoll()
    {
        duration = Mathf.Max(0.01f, rollDuration);
        if (!playerAnim.TryPlayRollAnim(duration))
            return;

        IsRolling = true;
        rollTimer = 0f;
        rollFaceDir = playerMovement.FaceDirection;
        if (Mathf.Approximately(rollFaceDir, 0f))
            rollFaceDir = 1f;

        PlaySessionRecorder.Instance?.RecordAbility2();

        playerMovement.PrepareRollPhysics();
        savedGravity = rb.gravityScale;
        savedCollisionMode = rb.collisionDetectionMode;
        rb.gravityScale = 0f;
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        acceleration = 8f * Mathf.Max(0f, rollHeight) / (duration * duration);
        // Half-step correction for the semi-implicit physics integration.
        rb.linearVelocity = new Vector2(0f, 4f * Mathf.Max(0f, rollHeight) / duration
            + acceleration * Mathf.Min(Time.fixedDeltaTime, duration) * 0.5f);
    }

    void EndRoll(bool startCooldown, bool completed)
    {
        if (!IsRolling)
            return;

        IsRolling = false;
        rollTimer = 0f;

        rb.collisionDetectionMode = savedCollisionMode;
        if (!playerMovement.IsExternallyControlled)
        {
            rb.gravityScale = savedGravity;
            rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
        }
        physicsCheck.Check();
        playerAnim.EndRollAnim(completed, physicsCheck.isSolidGround && rb.linearVelocity.y <= 0.01f);

        if (startCooldown)
            cooldownTimer = Mathf.Max(0f, rollCooldown);
    }
}
