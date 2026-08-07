using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(PhysicsCheck))]
[RequireComponent(typeof(DataDefination))]
public class PlayerMovement : MonoBehaviour, ISaveable // 玩家移动：输入/动画在 Update，物理在 FixedUpdate；需挂 PlayerAnim 或 PlayerFullBodyAnim
{
    const string FacingKeySuffix = "facing";

    [Header("移动")]
    public float runSpeed = 4f;
    public float crouchMoveSpeed = 2f;
    public float jumpHeight = 2.5f;      // 起跳目标高度，用于反算初速度
    public float inputThreshold = 0.5f;  // 摇杆死区，低于此值视为无输入
    public float jumpBufferTime = 0.15f; // 跳跃输入缓冲（秒），弥补 Update 与 FixedUpdate 不同步

    [Header("蹲伏碰撞")]
    [SerializeField] Vector2 crouchColliderSize = new Vector2(1.08f, 1.2f);

    [Header("空中下射滞空")]
    [Tooltip("每次向下射击刷新的滞空时长（秒）")]
    [SerializeField] float airHangDuration = 0.12f;
    [Tooltip("滞空期间的重力倍率（相对 Prefab 正常 gravityScale）")]
    [SerializeField] float airHangGravityScale = 0.25f;
    [Tooltip("触发/维持时竖直速度下限；过快下落会被抬到此值")]
    [SerializeField] float airHangVelocityY = 0f;

    Rigidbody2D rb;
    PhysicsCheck physicsCheck;
    PlatformDropThrough platformDropThrough;
    PlayerAnimBase playerAnim;
    InputSystem_Actions actions;
    CapsuleCollider2D capsuleCollider;
    Vector2 standingColliderSize;
    Vector2 standingColliderOffset;
    bool lastCrouchColliderState;

    float savedGravityScale;
    bool savedColliderEnabled;
    float normalGravityScale;
    /// <summary>斜坡起跳/下穿后短时间内脱离坡面贴合，避免速度被改写。</summary>
    float slopeDetachTimer;
    float airHangTimer;
    float knockbackUntil;

    public bool IsActionLocked { get; private set; }
    public bool IsKnockbackActive => Time.time < knockbackUntil;
    public bool IsSlopeDetached => slopeDetachTimer > 0f;
    public bool IsAirHanging => airHangTimer > 0f || playerAnim.IsForcedAirCombo;

    Vector2 moveInput;
    bool jumpPressed;
    float jumpBufferCounter; // >0 表示近期按过跳跃键，在 FixedUpdate 中消费
    float faceDir = 1f; // 面朝：1 右，-1 左，通过 localScale.x 翻转
    public float FaceDirection => faceDir;
    public Vector2 MoveInput => moveInput;
    public float InputThreshold => inputThreshold;
    public bool GetShootLookUp() => actions.Player.Move.ReadValue<Vector2>().y > inputThreshold;
    public bool GetShootLookDown() =>
        !physicsCheck.isGround && actions.Player.Move.ReadValue<Vector2>().y < -inputThreshold;
    int lastKPressFrame = -1; // 最近一次在 Update 检测到 K 的帧号

    [Header("事件监听")]
    [SerializeField] VoidEventSO newGameEvent;
    [SerializeField] VoidEventSO afterSceneLoadedEvent;

    [Header("跳跃调试（Play 时查看）")]
    [SerializeField] bool dbgKPressedThisUpdate;   // Update：本帧 WasPressedThisFrame
    [SerializeField] bool dbgJumpBuffered;         // FixedUpdate：缓冲内仍有跳跃意图
    [SerializeField] float dbgJumpBufferRemaining; // 剩余缓冲时间（秒）
    [SerializeField] bool dbgIsGroundInFixed;      // FixedUpdate：TryJump 时的 isGround
    [SerializeField] bool dbgDidJump;              // 本次 FixedUpdate 是否起跳成功
    [SerializeField] int dbgLastKPressFrame;       // 上次按 K 的帧号
    [SerializeField] int dbgLastTryJumpFrame;      // 上次 TryJump 的帧号
    [SerializeField] string dbgResult = "—";       // 最近一次 TryJump 结果说明

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        physicsCheck = GetComponent<PhysicsCheck>();
        platformDropThrough = GetComponent<PlatformDropThrough>();
        playerAnim = PlayerAnimBase.Resolve(gameObject);
        if (playerAnim == null)
            Debug.LogError("PlayerMovement 需要 PlayerAnim 或 PlayerFullBodyAnim 组件。", this);
        capsuleCollider = GetComponent<CapsuleCollider2D>();
        if (capsuleCollider != null)
        {
            standingColliderSize = capsuleCollider.size;
            standingColliderOffset = capsuleCollider.offset;
        }
        actions = new InputSystem_Actions();
        normalGravityScale = rb.gravityScale;

        // 禁用期间也要能收到新游戏 / 场景加载事件
        if (newGameEvent != null)
            newGameEvent.OnEventRaised += OnNewGame;
        if (afterSceneLoadedEvent != null)
            afterSceneLoadedEvent.OnEventRaised += OnSceneLoaded;
    }

    void OnEnable()
    {
        actions.Player.Enable();
        ((ISaveable)this).RegisterSaveData();
    }

    void OnDisable()
    {
        if (IsActionLocked)
            EndExternalControl();

        ((ISaveable)this).UnregisterSaveData();
        actions.Player.Disable();
    }

    void OnDestroy()
    {
        if (newGameEvent != null)
            newGameEvent.OnEventRaised -= OnNewGame;
        if (afterSceneLoadedEvent != null)
            afterSceneLoadedEvent.OnEventRaised -= OnSceneLoaded;
        actions?.Dispose();
    }

    void Update()
    {
        if (!IsActionLocked && !playerAnim.IsRolling)
        {
            ReadInput();
            HandleLook();
            TryTurn();
            SyncAnimation(); // 先推进空中/落地，再处理蹲姿，才能同帧打断 Land
            HandleCrouch();
        }
        else if (!IsActionLocked && playerAnim.IsRolling)
        {
            // 翻滚中仍刷新地面检测，结束时空气阶段才能正确衔接
            moveInput = Vector2.zero;
            jumpPressed = false;
        }

        ApplyCrouchCollider(playerAnim.IsCrouching);

        if (!IsActionLocked)
            physicsCheck.Check();
    }

    void FixedUpdate()
    {
        if (IsActionLocked)
            return;

        if (slopeDetachTimer > 0f)
            slopeDetachTimer -= Time.fixedDeltaTime;

        if (platformDropThrough != null)
            platformDropThrough.UpdateCollisions();

        physicsCheck.Check();
        UpdateSlopeGravity();
        UpdateAirHang();

        if (playerAnim.IsRolling)
        {
            jumpBufferCounter = 0f;
            return;
        }

        if (actions.Player.Jump.WasPressedThisFrame()) // Fixed 里也读一次，覆盖同帧时序差
            jumpBufferCounter = jumpBufferTime;
        jumpBufferCounter -= Time.fixedDeltaTime;

        if (TryJump()) // 起跳覆盖本帧速度，跳过后不再水平移动
        {
            HandleLook(); // 蹲跳等：离开地面后同帧补判空中向下看
            ApplyRobotTopPlatformCarry(applyVertical: false);
            return;
        }

        TryTurn(); // 与 Update 双调用无害；保证 FixedUpdate 先于 Update 时也能先转身
        if (!IsKnockbackActive)
            ApplyHorizontalMovement();
        ApplyRobotTopPlatformCarry(applyVertical: true);
        CancelVelocityIntoObstacle();
        CancelVelocityIntoSlope();
    }

    /// <summary>
    /// 受击推动：施加冲量并短时跳过水平速度覆写，避免下一帧被移动逻辑盖掉。
    /// </summary>
    public void BeginKnockback(Vector2 impulse, float duration)
    {
        if (IsActionLocked)
            return;

        rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
        rb.AddForce(impulse, ForceMode2D.Impulse);
        knockbackUntil = Time.time + Mathf.Max(0.05f, duration);
    }

    /// <summary>
    /// 空中向下射击 / 空中终结动画开始时调用：刷新短暂低重力滞空。
    /// </summary>
    public void NotifyAirHangFromDownShot()
    {
        if (IsActionLocked || physicsCheck.isGround)
            return;

        if (platformDropThrough != null && platformDropThrough.IsDroppingThrough)
            return;

        airHangTimer = airHangDuration;
        ApplyAirHangPhysics();
    }

    /// <summary>结束滞空并恢复重力（空中终结动画退出时调用）。</summary>
    public void ClearAirHang()
    {
        ClearAirHang(restoreGravity: true);
    }

    void UpdateAirHang()
    {
        if (physicsCheck.isGround
            || (platformDropThrough != null && platformDropThrough.IsDroppingThrough))
        {
            if (airHangTimer > 0f || playerAnim.IsForcedAirCombo)
                ClearAirHang(restoreGravity: true);
            return;
        }

        // 空中全身终结：整段动画维持滞空
        if (playerAnim.IsForcedAirCombo)
        {
            ApplyAirHangPhysics();
            return;
        }

        if (airHangTimer <= 0f)
            return;

        airHangTimer -= Time.fixedDeltaTime;
        if (airHangTimer > 0f)
        {
            ApplyAirHangPhysics();
            return;
        }

        airHangTimer = 0f;
        RestoreGravityAfterAirHang();
    }

    void ApplyAirHangPhysics()
    {
        // 上升阶段不减重力，否则起跳瞬间下射/空中终结会抬高跳跃顶点
        if (rb.linearVelocity.y > airHangVelocityY)
        {
            rb.gravityScale = normalGravityScale;
            return;
        }

        rb.gravityScale = normalGravityScale * airHangGravityScale;
        if (rb.linearVelocity.y < airHangVelocityY)
            rb.linearVelocity = new Vector2(rb.linearVelocity.x, airHangVelocityY);
    }

    void ClearAirHang(bool restoreGravity)
    {
        airHangTimer = 0f;
        if (restoreGravity)
            RestoreGravityAfterAirHang();
    }

    void RestoreGravityAfterAirHang()
    {
        if (IsActionLocked)
            return;

        // 斜坡贴合时由 UpdateSlopeGravity / MaintainSlopeContact 管重力
        if (!IsSlopeDetached
            && physicsCheck.isGround
            && physicsCheck.isOnSlope
            && (platformDropThrough == null || !platformDropThrough.IsDroppingThrough))
            return;

        rb.gravityScale = normalGravityScale;
    }

    void OnCollisionStay2D(Collision2D collision)
    {
        if (IsActionLocked || IsSlopeDetached)
            return;

        if (platformDropThrough != null && platformDropThrough.IsDroppingThrough)
            return;

        if (((1 << collision.gameObject.layer) & physicsCheck.groundLayer) == 0)
            return;

        foreach (ContactPoint2D contact in collision.contacts)
        {
            if (contact.normal.y <= 0.5f)
                continue;

            var slope = collision.collider.GetComponent<SlopeOneWayPlatform>();
            if (slope == null)
                continue;

            Vector2 feetPos = new Vector2(capsuleCollider.bounds.center.x, capsuleCollider.bounds.min.y);
            if (!slope.IsFeetAboveSurface(feetPos))
                continue;

            MaintainSlopeContact(contact.normal);
            return;
        }
    }

    void MaintainSlopeContact(Vector2 groundNormal)
    {
        if (IsSlopeDetached || !physicsCheck.isGround)
            return;

        if (platformDropThrough != null && platformDropThrough.IsDroppingThrough)
            return;

        // 上升中不贴合，避免吃掉起跳速度
        if (rb.linearVelocity.y > 0.05f)
            return;

        rb.gravityScale = 0f;

        if (playerAnim.IsTurning || playerAnim.IsCharging || playerAnim.IsHeavySpinFiring
            || (playerAnim.IsCrouching && playerAnim.IsDispatching))
        {
            rb.linearVelocity = Vector2.zero;
            return;
        }

        float moveX = Mathf.Abs(moveInput.x) > inputThreshold ? Mathf.Sign(moveInput.x) : 0f;
        float speed = playerAnim.IsCrouching ? crouchMoveSpeed : runSpeed;
        Vector2 tangent = new Vector2(-groundNormal.y, groundNormal.x).normalized;

        if (Mathf.Approximately(moveX, 0f))
        {
            rb.linearVelocity = Vector2.zero;
            return;
        }

        if (Mathf.Sign(tangent.x) != Mathf.Sign(moveX))
            tangent = -tangent;

        rb.linearVelocity = tangent * speed;
    }

    void BeginSlopeDetach(float duration = 0.2f)
    {
        slopeDetachTimer = Mathf.Max(slopeDetachTimer, duration);
        rb.gravityScale = normalGravityScale;
    }

    void ReadInput()
    {
        moveInput = actions.Player.Move.ReadValue<Vector2>();
        jumpPressed = actions.Player.Jump.WasPressedThisFrame();

        dbgKPressedThisUpdate = jumpPressed;
        if (jumpPressed)
        {
            jumpBufferCounter = jumpBufferTime;
            lastKPressFrame = Time.frameCount;
            dbgLastKPressFrame = lastKPressFrame;
        }
    }

    void HandleCrouch() // 仅地面响应下方向进入/退出蹲姿
    {
        if (!physicsCheck.isGround)
            return;

        // 连击终结期间禁止站起/换蹲
        if (playerAnim.IsPlayingMachinistComboShoot)
        {
            jumpBufferCounter = 0f;
            return;
        }

        bool wantCrouch = moveInput.y < -inputThreshold;

        // 蓄力中：蹲下/站起只切换蓄力姿态，不中断蓄力
        if (playerAnim.IsCharging)
        {
            bool wantLookUp = moveInput.y > inputThreshold;
            playerAnim.SyncChargeAimFromInput(wantLookUp, wantLookDown: false, wantCrouch);
            if (wantCrouch)
                jumpBufferCounter = 0f;
            return;
        }

        // 召唤动画期间锁定蹲/站姿态，避免 intro/loop 中途换层
        if (playerAnim.IsDispatching)
        {
            if (wantCrouch)
                jumpBufferCounter = 0f;
            return;
        }

        if (wantCrouch && !playerAnim.IsCrouching)
        {
            // Land 期间 airPhase 仍可能是 Fall/LeapAir；按住 S 应立刻打断落地动画进蹲
            if (playerAnim.IsPlayingLand)
            {
                jumpBufferCounter = 0f;
                playerAnim.PlayCrouchAnim();
                return;
            }

            // 起跳后 coyote 期间 isGround 仍为 true；按住 S 不得重新蹲下，否则会清掉 Jump/Leap
            if (playerAnim.CurrentAirPhase != PlayerAnimBase.AirPhaseType.Ground)
                return;
            if (rb.linearVelocity.y > 0.05f)
                return;

            jumpBufferCounter = 0f; // 进入蹲姿时清跳跃缓冲，避免蹲跳后立刻再次蹲下
            playerAnim.PlayCrouchAnim();
        }
        else if (!wantCrouch && playerAnim.IsCrouching)
            playerAnim.PlayStandAnim();
    }

    void HandleLook() // W 向上看（地面/空中）；空中 S 向下看
    {
        bool wantLookUp = moveInput.y > inputThreshold;
        bool wantLookDown = !physicsCheck.isGround && moveInput.y < -inputThreshold;

        // 蓄力中：上下输入切蓄力方向，不走普通 Look
        if (playerAnim.IsCharging)
        {
            // 地面蹲下由 HandleCrouch 处理；此处只同步上下瞄准（地面按下不算 LookDown）
            bool wantCrouch = physicsCheck.isGround && moveInput.y < -inputThreshold;
            if (!wantCrouch)
                playerAnim.SyncChargeAimFromInput(wantLookUp, wantLookDown, wantCrouch: false);
            return;
        }

        if (playerAnim.IsDispatching)
            return;

        playerAnim.SetLookUp(wantLookUp);
        playerAnim.SetLookDown(wantLookDown);
    }

    void TryTurn() // 地面改变朝向时播站立/蹲伏转身
    {
        if (!physicsCheck.isGround || playerAnim.IsTurning)
            return;

        float moveX = Mathf.Abs(moveInput.x) > inputThreshold ? Mathf.Sign(moveInput.x) : 0f;
        if (moveX == 0f || moveX == faceDir)
            return;

        // 蓄力中：左右输入忽略（不翻面、不转身、不移动）
        if (playerAnim.IsCharging || playerAnim.IsHeavySpinFiring)
            return;

        if (playerAnim.IsPlayingMachinistComboShoot)
            return;

        // 蹲姿召唤期间不转身、不移动
        if (playerAnim.IsCrouching && playerAnim.IsDispatching)
            return;

        bool started = playerAnim.IsCrouching
            ? playerAnim.PlayCrouchTurnAnim()
            : playerAnim.PlayTurnAnim();

        if (!started)
            return;

        faceDir = moveX;
        ApplyFacing();
        rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
    }

    bool TryJump() // 地面起跳；有水平输入为 Leap，初速度 v=sqrt(2gh)
    {
        bool wantsJump = jumpBufferCounter > 0f;

        dbgLastTryJumpFrame = Time.frameCount;
        dbgJumpBuffered = wantsJump;
        dbgJumpBufferRemaining = Mathf.Max(0f, jumpBufferCounter);
        dbgIsGroundInFixed = physicsCheck.isGround;
        dbgDidJump = false;

        if (playerAnim.IsPlayingMachinistComboShoot || playerAnim.IsHeavySpinFiring)
        {
            jumpBufferCounter = 0f;
            dbgResult = playerAnim.IsHeavySpinFiring ? "机枪蓄力中禁止跳跃" : "连击终结中禁止跳跃";
            return false;
        }

        if (!wantsJump)
        {
            dbgResult = lastKPressFrame >= 0
                ? $"缓冲已过期（按 K 后已过 {Time.frameCount - lastKPressFrame} 帧）"
                : "无跳跃输入";
            return false;
        }

        if (!physicsCheck.isGround)
        {
            dbgResult = $"不在地面（缓冲剩余 {dbgJumpBufferRemaining:F2}s）";
            return false;
        }

        if (moveInput.y < -inputThreshold
            && platformDropThrough != null
            && platformDropThrough.TryBeginDropThrough(moveInput, inputThreshold))
        {
            ClearAirHang(restoreGravity: false);
            BeginSlopeDetach(0.35f);
            rb.linearVelocity = new Vector2(rb.linearVelocity.x, Mathf.Min(rb.linearVelocity.y, -2f));
            jumpBufferCounter = 0f;
            dbgResult = "单向平台下穿";
            lastKPressFrame = -1;
            return false;
        }

        bool hasHorizontalInput = Mathf.Abs(moveInput.x) > inputThreshold;
        // 蓄力中左右无效：起跳不改朝向、不带水平速度
        if (playerAnim.IsCharging || playerAnim.IsHeavySpinFiring
            || (playerAnim.IsCrouching && playerAnim.IsDispatching))
            hasHorizontalInput = false;
        else if (hasHorizontalInput)
            faceDir = moveInput.x > 0f ? 1f : -1f;

        // 斜坡站立时 gravityScale 可能为 0，必须用正常重力反算初速度
        float gravity = Mathf.Abs(Physics2D.gravity.y * normalGravityScale);
        float jumpVelocity = Mathf.Sqrt(2f * gravity * jumpHeight);

        float horizontalVelocity = hasHorizontalInput ? faceDir * runSpeed : 0f;

        if (physicsCheck.isOnSlope)
        {
            BeginSlopeDetach(0.25f);
            // 沿法线微抬，减少起跳帧仍卡在坡面里
            Vector2 n = physicsCheck.groundNormal;
            rb.position += n * 0.06f;
        }
        else
        {
            rb.gravityScale = normalGravityScale;
        }

        rb.linearVelocity = new Vector2(horizontalVelocity, jumpVelocity);

        playerAnim.PlayJumpAnim(hasHorizontalInput);
        ApplyFacing();

        jumpBufferCounter = 0f;
        dbgDidJump = true;
        dbgResult = "起跳成功";
        lastKPressFrame = -1;
        return true;
    }

    /// <summary>
    /// 站在机器人顶部单向平台时叠加平台速度。
    /// 玩家无摩擦材质，无法靠物理摩擦跟随，必须在速度层携带。
    /// </summary>
    void ApplyRobotTopPlatformCarry(bool applyVertical)
    {
        if (platformDropThrough != null && platformDropThrough.IsDroppingThrough)
            return;

        RobotTopPlatform platform = FindRobotTopUnderFeet();
        if (platform == null)
            return;

        Vector2 platformVelocity = platform.PlatformVelocity;
        Vector2 velocity = rb.linearVelocity;
        velocity.x += platformVelocity.x;
        if (applyVertical && physicsCheck.isSolidGround)
            velocity.y = platformVelocity.y;
        rb.linearVelocity = velocity;
    }

    RobotTopPlatform FindRobotTopUnderFeet()
    {
        float facing = Mathf.Sign(transform.localScale.x);
        if (Mathf.Approximately(facing, 0f))
            facing = 1f;

        Vector2 origin = (Vector2)transform.position
            + new Vector2(physicsCheck.bottomOffset.x * facing, physicsCheck.bottomOffset.y);
        float castDistance = physicsCheck.checkRaduis + 0.12f;

        RaycastHit2D hit = Physics2D.CircleCast(
            origin, 0.08f, Vector2.down, castDistance, physicsCheck.groundLayer);

        if (hit.collider == null || hit.normal.y <= 0.5f)
            return null;

        if (platformDropThrough != null && !platformDropThrough.ShouldCollideWith(hit.collider))
            return null;

        return hit.collider.GetComponent<RobotTopPlatform>();
    }

    void ApplyHorizontalMovement()
    {
        if (playerAnim.IsPlayingMachinistComboShoot)
        {
            rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
            return;
        }

        if (physicsCheck.isGround && playerAnim.IsCrouching
            && (playerAnim.IsShooting || playerAnim.IsThrowing || playerAnim.IsMelee || playerAnim.IsDispatching))
        {
            rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
            return;
        }

        if (playerAnim.IsCharging || playerAnim.IsHeavySpinFiring)
        {
            rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
            return;
        }

        float moveX = Mathf.Abs(moveInput.x) > inputThreshold ? Mathf.Sign(moveInput.x) : 0f;
        Vector2 entryTangent = Vector2.zero;
        bool slopeEntry = platformDropThrough != null
            && platformDropThrough.TryGetBottomSlopeEntry(out _, out entryTangent);
        if (!physicsCheck.isOnSlope && !slopeEntry && physicsCheck.IsBlockedHorizontally(moveX))
            moveX = 0f;

        if (physicsCheck.isGround)
        {
            // 斜坡起跳后 coyote 仍可能判接地，不能覆盖上升速度
            if (IsSlopeDetached)
            {
                if (moveX != 0f)
                {
                    rb.linearVelocity = new Vector2(moveX * runSpeed, rb.linearVelocity.y);
                    ApplyFacing();
                }
                return;
            }

            if (playerAnim.IsTurning)
            {
                rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
                return;
            }

            float speed = playerAnim.IsCrouching ? crouchMoveSpeed : runSpeed;

            if (physicsCheck.isOnSlope)
            {
                Vector2 normal = physicsCheck.groundNormal;
                Vector2 tangent = new Vector2(-normal.y, normal.x).normalized;

                if (Mathf.Approximately(moveX, 0f))
                {
                    rb.linearVelocity = Vector2.zero;
                }
                else
                {
                    if (Mathf.Sign(tangent.x) != Mathf.Sign(moveX))
                        tangent = -tangent;
                    rb.linearVelocity = tangent * speed;
                }
            }
            else if (slopeEntry)
            {
                // 坡脚过渡：沿坡面切向抬升，避免纯水平撞厚盒端面
                rb.linearVelocity = entryTangent * speed;
            }
            else
            {
                rb.linearVelocity = new Vector2(moveX * speed, rb.linearVelocity.y);
            }

            if (moveX != 0f || slopeEntry)
                ApplyFacing();
            return;
        }

        // 空中：有输入才改水平速度，无输入保留惯性
        if (moveX != 0f)
        {
            rb.linearVelocity = new Vector2(moveX * runSpeed, rb.linearVelocity.y);

            if ((playerAnim.IsShooting || playerAnim.IsThrowing || playerAnim.IsMelee) && moveX != faceDir)
            {
                faceDir = moveX;
                ApplyFacing();
            }
        }
        else if (!physicsCheck.isGround && (physicsCheck.touchLeftWall || physicsCheck.touchRightWall))
        {
            // 贴障碍物且无输入时仍清除朝墙的水平速度，避免物理求解把 Y 速度抵消
            rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
        }
    }

    /// <summary>
    /// 清除朝障碍物方向的速度分量（含起跳惯性），防止贴墙/卡台阶角悬空。
    /// </summary>
    void CancelVelocityIntoObstacle()
    {
        if (physicsCheck.isOnSlope)
            return;

        // 坡脚过渡中允许沿切向顶入，不被侧墙速度清除打断
        if (platformDropThrough != null
            && platformDropThrough.TryGetBottomSlopeEntry(out _, out _))
            return;

        if (physicsCheck.IsBlockedHorizontally(-1f) && rb.linearVelocity.x < 0f)
            rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
        else if (physicsCheck.IsBlockedHorizontally(1f) && rb.linearVelocity.x > 0f)
            rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
    }

    /// <summary>
    /// 站在斜坡上时移除法向速度分量，防止物理求解与重力导致滑落。
    /// </summary>
    void CancelVelocityIntoSlope()
    {
        if (IsSlopeDetached)
            return;

        if (!physicsCheck.isGround || !physicsCheck.isOnSlope)
            return;

        if (rb.linearVelocity.y > 0.05f)
            return;

        Vector2 normal = physicsCheck.groundNormal;
        Vector2 velocity = rb.linearVelocity;
        float normalSpeed = Vector2.Dot(velocity, normal);
        rb.linearVelocity = velocity - normal * normalSpeed;
    }

    void UpdateSlopeGravity()
    {
        // 滞空期间由 UpdateAirHang 管重力，避免斜坡逻辑覆写
        if (IsAirHanging)
            return;

        if (IsSlopeDetached || (platformDropThrough != null && platformDropThrough.IsDroppingThrough))
        {
            rb.gravityScale = normalGravityScale;
            return;
        }

        if (physicsCheck.isGround && physicsCheck.isOnSlope)
            rb.gravityScale = 0f;
        else
            rb.gravityScale = normalGravityScale;
    }

    void ApplyFacing() // 翻转 localScale.x，保留绝对缩放
    {
        Vector3 scale = transform.localScale;
        scale.x = Mathf.Abs(scale.x) * faceDir;
        transform.localScale = scale;
    }

    public void FaceTowardWorldX(float worldX)
    {
        float newDir = worldX >= transform.position.x ? 1f : -1f;
        if (Mathf.Approximately(newDir, faceDir))
            return;

        faceDir = newDir;
        ApplyFacing();
    }

    void SyncAnimation() // 推进空中阶段；地面按输入切换 Idle/Run
    {
        playerAnim.UpdateAirState(physicsCheck.isGround, rb.linearVelocity.y);

        if (!physicsCheck.isGround || playerAnim.IsTurning)
            return;

        if (playerAnim.IsCharging || playerAnim.IsHeavySpinFiring)
        {
            playerAnim.PlayIdleAnim();
            return;
        }

        if (playerAnim.IsCrouching && playerAnim.IsDispatching)
            return;

        if (playerAnim.IsCrouching && (playerAnim.IsShooting || playerAnim.IsThrowing || playerAnim.IsMelee))
            return;

        if (Mathf.Abs(moveInput.x) > inputThreshold)
            playerAnim.PlayRunAnim();
        else if (!playerAnim.TryPlayRunStopLand())
            playerAnim.PlayIdleAnim();
    }

    void OnNewGame() => ResetMovementState();

    void OnSceneLoaded() => ResetMovementState();

    public DataDefination GetDataID() => GetComponent<DataDefination>();

    public void GetSaveData(Data data)
    {
        var dataId = GetDataID();
        if (dataId == null || string.IsNullOrEmpty(dataId.ID))
            return;
        if (!data.characterPosDict.ContainsKey(dataId.ID))
            return;

        string key = dataId.ID + FacingKeySuffix;
        if (data.floatSavedData.ContainsKey(key))
            data.floatSavedData[key] = faceDir;
        else
            data.floatSavedData.Add(key, faceDir);
    }

    public void LoadSaveData(Data data)
    {
        var dataId = GetDataID();
        if (dataId == null || string.IsNullOrEmpty(dataId.ID))
            return;
        if (!data.characterPosDict.ContainsKey(dataId.ID))
            return;

        string key = dataId.ID + FacingKeySuffix;
        if (data.floatSavedData.TryGetValue(key, out float savedFacing))
            faceDir = savedFacing >= 0f ? 1f : -1f;

        ResetMovementState();
    }

    void ResetMovementState()
    {
        if (IsActionLocked)
            EndExternalControl();

        moveInput = Vector2.zero;
        jumpPressed = false;
        jumpBufferCounter = 0f;
        lastKPressFrame = -1;
        slopeDetachTimer = 0f;
        airHangTimer = 0f;

        rb.linearVelocity = Vector2.zero;
        rb.position = transform.position;
        rb.gravityScale = normalGravityScale;

        if (playerAnim.IsCrouching)
            playerAnim.PlayStandAnim();
        playerAnim.SetLookUp(false);
        playerAnim.SetLookDown(false);
        playerAnim.PlayIdleAnim();

        physicsCheck.Check();
        playerAnim.UpdateAirState(physicsCheck.isGround, 0f);
        ApplyFacing();
    }

    public void BeginExternalControl()
    {
        if (IsActionLocked)
            return;

        ClearAirHang(restoreGravity: false);
        savedGravityScale = normalGravityScale;
        savedColliderEnabled = capsuleCollider != null && capsuleCollider.enabled;

        rb.gravityScale = 0f;
        rb.linearVelocity = Vector2.zero;
        if (capsuleCollider != null)
            capsuleCollider.enabled = false;

        IsActionLocked = true;
    }

    public void EndExternalControl()
    {
        if (!IsActionLocked)
            return;

        rb.gravityScale = savedGravityScale;
        if (capsuleCollider != null)
            capsuleCollider.enabled = savedColliderEnabled;

        IsActionLocked = false;
    }

    public void StepExternalMove(Vector2 target, float speed)
    {
        if (!IsActionLocked)
            return;

        Vector2 next = Vector2.MoveTowards(rb.position, target, speed * Time.fixedDeltaTime);
        rb.MovePosition(next);
    }

    void ApplyCrouchCollider(bool crouching)
    {
        if (capsuleCollider == null || lastCrouchColliderState == crouching)
            return;

        lastCrouchColliderState = crouching;

        if (crouching)
        {
            float bottom = standingColliderOffset.y - standingColliderSize.y * 0.5f;
            capsuleCollider.size = crouchColliderSize;
            capsuleCollider.offset = new Vector2(
                standingColliderOffset.x,
                bottom + crouchColliderSize.y * 0.5f);
        }
        else
        {
            capsuleCollider.size = standingColliderSize;
            capsuleCollider.offset = standingColliderOffset;
        }

        physicsCheck.RefreshOffsets();
    }
}
