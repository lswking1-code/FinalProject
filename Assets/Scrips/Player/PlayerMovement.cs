using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(PhysicsCheck))]
[RequireComponent(typeof(PlayerAnim))]
[RequireComponent(typeof(DataDefination))]
public class PlayerMovement : MonoBehaviour, ISaveable // 玩家移动：输入/动画在 Update，物理在 FixedUpdate
{
    const string FacingKeySuffix = "facing";

    [Header("移动")]
    public float runSpeed = 4f;
    public float crouchMoveSpeed = 2f;
    public float jumpHeight = 2.5f;      // 起跳目标高度，用于反算初速度
    public float inputThreshold = 0.5f;  // 摇杆死区，低于此值视为无输入
    public float jumpBufferTime = 0.15f; // 跳跃输入缓冲（秒），弥补 Update 与 FixedUpdate 不同步

    Rigidbody2D rb;
    PhysicsCheck physicsCheck;
    PlayerAnim playerAnim;
    InputSystem_Actions actions;
    CapsuleCollider2D capsuleCollider;

    float savedGravityScale;
    bool savedColliderEnabled;

    public bool IsActionLocked { get; private set; }

    Vector2 moveInput;
    bool jumpPressed;
    float jumpBufferCounter; // >0 表示近期按过跳跃键，在 FixedUpdate 中消费
    float faceDir = 1f; // 面朝：1 右，-1 左，通过 localScale.x 翻转
    public float FaceDirection => faceDir;
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
        playerAnim = GetComponent<PlayerAnim>();
        capsuleCollider = GetComponent<CapsuleCollider2D>();
        actions = new InputSystem_Actions();
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
        if (IsActionLocked)
            return;

        ReadInput();
        HandleCrouch();
        HandleLook();
        TryTurn();
        SyncAnimation(); // 每帧同步动画与空中阶段
    }

    void FixedUpdate()
    {
        if (IsActionLocked)
            return;

        physicsCheck.Check();

        if (actions.Player.Jump.WasPressedThisFrame()) // Fixed 里也读一次，覆盖同帧时序差
            jumpBufferCounter = jumpBufferTime;
        jumpBufferCounter -= Time.fixedDeltaTime;

        if (TryJump()) // 起跳覆盖本帧速度，跳过后不再水平移动
        {
            HandleLook(); // 蹲跳等：离开地面后同帧补判空中向下看
            return;
        }

        TryTurn(); // 与 Update 双调用无害；保证 FixedUpdate 先于 Update 时也能先转身
        ApplyHorizontalMovement();
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

        bool wantCrouch = moveInput.y < -inputThreshold;

        if (wantCrouch && !playerAnim.IsCrouching)
        {
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

        bool hasHorizontalInput = Mathf.Abs(moveInput.x) > inputThreshold;
        if (hasHorizontalInput)
            faceDir = moveInput.x > 0f ? 1f : -1f;

        float gravity = Mathf.Abs(Physics2D.gravity.y * rb.gravityScale);
        float jumpVelocity = Mathf.Sqrt(2f * gravity * jumpHeight);

        float horizontalVelocity = hasHorizontalInput ? faceDir * runSpeed : 0f;
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
        if (physicsCheck.isGround && playerAnim.IsCrouching && (playerAnim.IsShooting || playerAnim.IsThrowing))
        {
            rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
            return;
        }

        float moveX = Mathf.Abs(moveInput.x) > inputThreshold ? Mathf.Sign(moveInput.x) : 0f;

        if (physicsCheck.isGround)
        {
            if (playerAnim.IsTurning)
            {
                rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
                return;
            }

            float speed = playerAnim.IsCrouching ? crouchMoveSpeed : runSpeed;
            rb.linearVelocity = new Vector2(moveX * speed, rb.linearVelocity.y);

            if (moveX != 0f)
                ApplyFacing();
            return;
        }

        // 空中：有输入才改水平速度，无输入保留惯性
        if (moveX != 0f)
        {
            rb.linearVelocity = new Vector2(moveX * runSpeed, rb.linearVelocity.y);

            if ((playerAnim.IsShooting || playerAnim.IsThrowing) && moveX != faceDir)
            {
                faceDir = moveX;
                ApplyFacing();
            }
        }
    }

    void ApplyFacing() // 翻转 localScale.x，保留绝对缩放
    {
        Vector3 scale = transform.localScale;
        scale.x = Mathf.Abs(scale.x) * faceDir;
        transform.localScale = scale;
    }

    void SyncAnimation() // 推进空中阶段；地面按输入切换 Idle/Run
    {
        playerAnim.UpdateAirState(physicsCheck.isGround, rb.linearVelocity.y);

        if (!physicsCheck.isGround || playerAnim.IsTurning)
            return;

        if (playerAnim.IsCrouching && (playerAnim.IsShooting || playerAnim.IsThrowing))
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

        rb.linearVelocity = Vector2.zero;
        rb.position = transform.position;

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
}
