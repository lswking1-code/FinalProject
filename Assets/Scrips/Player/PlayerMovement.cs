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

    public bool IsActionLocked { get; private set; }
    public bool IsSlopeDetached => slopeDetachTimer > 0f;

    Vector2 moveInput;
    bool jumpPressed;
    float jumpBufferCounter; // >0 表示近期按过跳跃键，在 FixedUpdate 中消费
    bool comboShootInputSnapshotActive;
    float comboStartMoveX;
    bool comboStartWantCrouch;
    bool comboStartWasCrouching;
    bool comboStartHadJumpBuffer;
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
        playerAnim = GetComponent<PlayerAnimBase>();
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
    }

    void OnEnable()
    {
        actions.Player.Enable();
        if (newGameEvent != null)
            newGameEvent.OnEventRaised += OnNewGame;
        if (afterSceneLoadedEvent != null)
            afterSceneLoadedEvent.OnEventRaised += OnSceneLoaded;
        ((ISaveable)this).RegisterSaveData();
    }

    void OnDisable()
    {
        if (IsActionLocked)
            EndExternalControl();

        if (newGameEvent != null)
            newGameEvent.OnEventRaised -= OnNewGame;
        if (afterSceneLoadedEvent != null)
            afterSceneLoadedEvent.OnEventRaised -= OnSceneLoaded;
        ((ISaveable)this).UnregisterSaveData();
        actions.Player.Disable();
    }

    void OnDestroy()
    {
        actions?.Dispose();//释放玩家输入
    }

    void Update()
    {
        if (!IsActionLocked)
        {
            ReadInput();
            TryInterruptMachinistComboShoot();
            HandleLook();
            TryTurn();
            SyncAnimation(); // 先推进空中/落地，再处理蹲姿，才能同帧打断 Land
            HandleCrouch();
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

        if (actions.Player.Jump.WasPressedThisFrame()) // Fixed 里也读一次，覆盖同帧时序差
            jumpBufferCounter = jumpBufferTime;
        jumpBufferCounter -= Time.fixedDeltaTime;

        if (TryJump()) // 起跳覆盖本帧速度，跳过后不再水平移动
        {
            HandleLook(); // 蹲跳等：离开地面后同帧补判空中向下看
            return;
        }

        TryInterruptMachinistComboShoot();
        TryTurn(); // 与 Update 双调用无害；保证 FixedUpdate 先于 Update 时也能先转身
        ApplyHorizontalMovement();
        CancelVelocityIntoObstacle();
        CancelVelocityIntoSlope();
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

        if (playerAnim.IsTurning || playerAnim.IsCharging
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

    void TryInterruptMachinistComboShoot()
    {
        if (!playerAnim.IsPlayingMachinistComboShoot)
        {
            comboShootInputSnapshotActive = false;
            return;
        }

        if (!comboShootInputSnapshotActive)
        {
            CaptureComboShootInputSnapshot();
            comboShootInputSnapshotActive = true;
            return;
        }

        if (!HasMachinistComboShootInterruptInput())
            return;

        playerAnim.InterruptMachinistComboShootFromInput();
    }

    void CaptureComboShootInputSnapshot()
    {
        comboStartMoveX = Mathf.Abs(moveInput.x) > inputThreshold ? Mathf.Sign(moveInput.x) : 0f;
        comboStartWantCrouch = physicsCheck.isGround && moveInput.y < -inputThreshold;
        comboStartWasCrouching = playerAnim.IsCrouching;
        comboStartHadJumpBuffer = jumpBufferCounter > 0f;
    }

    bool HasMachinistComboShootInterruptInput()
    {
        if (jumpPressed)
            return true;

        if (jumpBufferCounter > 0f && !comboStartHadJumpBuffer)
            return true;

        float moveX = Mathf.Abs(moveInput.x) > inputThreshold ? Mathf.Sign(moveInput.x) : 0f;
        if (moveX != comboStartMoveX)
            return true;

        bool wantCrouch = physicsCheck.isGround && moveInput.y < -inputThreshold;
        if (wantCrouch != comboStartWantCrouch)
            return true;

        if (comboStartWasCrouching && playerAnim.IsCrouching && !wantCrouch)
            return true;

        return false;
    }

    void HandleCrouch() // 仅地面响应下方向进入/退出蹲姿
    {
        if (!physicsCheck.isGround)
            return;

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
        if (playerAnim.IsCharging)
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
            BeginSlopeDetach(0.35f);
            rb.linearVelocity = new Vector2(rb.linearVelocity.x, Mathf.Min(rb.linearVelocity.y, -2f));
            jumpBufferCounter = 0f;
            dbgResult = "单向平台下穿";
            lastKPressFrame = -1;
            return false;
        }

        bool hasHorizontalInput = Mathf.Abs(moveInput.x) > inputThreshold;
        // 蓄力中左右无效：起跳不改朝向、不带水平速度
        if (playerAnim.IsCharging || (playerAnim.IsCrouching && playerAnim.IsDispatching))
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

    void ApplyHorizontalMovement()
    {
        if (physicsCheck.isGround && playerAnim.IsCrouching
            && (playerAnim.IsShooting || playerAnim.IsThrowing || playerAnim.IsMelee || playerAnim.IsDispatching))
        {
            rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
            return;
        }

        if (playerAnim.IsCharging)
        {
            rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
            return;
        }

        float moveX = Mathf.Abs(moveInput.x) > inputThreshold ? Mathf.Sign(moveInput.x) : 0f;
        if (!physicsCheck.isOnSlope && physicsCheck.IsBlockedHorizontally(moveX))
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
            else
            {
                rb.linearVelocity = new Vector2(moveX * speed, rb.linearVelocity.y);
            }

            if (moveX != 0f)
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

        if (playerAnim.IsCharging)
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

        savedGravityScale = rb.gravityScale;
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
