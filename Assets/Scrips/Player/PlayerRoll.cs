using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 枪械 Player 翻滚：按 Ability2(I) 触发，全身动画 + Z 轴旋转，水平位移且保留重力，期间强制无敌。
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
    [SerializeField] float rollDuration = 0.35f;
    [SerializeField] float rollSpeed = 7f;
    [SerializeField] float rollCooldown = 1f;
    [SerializeField] float rollRotations = 1f;

    InputSystem_Actions actions;
    PlayerAnimBase playerAnim;
    PlayerMovement playerMovement;
    Character character;
    PhysicsCheck physicsCheck;
    Rigidbody2D rb;

    float cooldownTimer;
    float rollTimer;
    float rollFaceDir = 1f;

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
            EndRoll(startCooldown: false);

        actions.Player.Disable();
    }

    void OnDestroy() => actions?.Dispose();

    void Update()
    {
        if (cooldownTimer > 0f)
            cooldownTimer -= Time.deltaTime;

        if (IsRolling)
        {
            UpdateRolling();
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
        if (!IsRolling)
            return;

        rb.linearVelocity = new Vector2(rollFaceDir * rollSpeed, rb.linearVelocity.y);
    }

    bool CanStartRoll()
    {
        if (playerMovement.IsActionLocked || playerAnim.IsDead)
            return false;

        if (cooldownTimer > 0f)
            return false;

        if (!physicsCheck.isGround)
            return false;

        if (playerAnim.IsThrowing || playerAnim.IsMelee || playerAnim.IsSwitchingWeapon)
            return false;

        if (playerAnim.IsDispatching || playerAnim.IsCharging || playerAnim.IsHeavySpinFiring
            || playerAnim.IsPlayingMachinistChargeShoot)
            return false;

        return true;
    }

    void StartRoll()
    {
        if (!playerAnim.TryPlayRollAnim())
            return;

        IsRolling = true;
        rollTimer = 0f;
        rollFaceDir = playerMovement.FaceDirection;
        if (Mathf.Approximately(rollFaceDir, 0f))
            rollFaceDir = 1f;

        character.SetForcedInvulnerable(true);
        ApplyRollRotation(0f);
    }

    void UpdateRolling()
    {
        if (playerAnim.IsDead)
        {
            EndRoll(startCooldown: true);
            return;
        }

        rollTimer += Time.deltaTime;
        float duration = Mathf.Max(0.01f, rollDuration);
        float t = Mathf.Clamp01(rollTimer / duration);
        ApplyRollRotation(t);

        if (t >= 1f)
            EndRoll(startCooldown: true);
    }

    void ApplyRollRotation(float normalizedTime)
    {
        // 面朝右时顺时针（负 Z），面朝左时逆时针，形成向前翻滚观感
        float degrees = -rollFaceDir * 360f * rollRotations * normalizedTime;
        playerAnim.SetRollRotation(degrees);
    }

    void EndRoll(bool startCooldown)
    {
        if (!IsRolling)
            return;

        IsRolling = false;
        rollTimer = 0f;

        playerAnim.EndRollAnim();

        // 死亡流程会自行维持 forcedInvulnerable，翻滚结束时不要清掉
        if (!playerAnim.IsDead)
            character.SetForcedInvulnerable(false);

        if (startCooldown)
            cooldownTimer = Mathf.Max(0f, rollCooldown);
    }
}
