using UnityEngine;

public enum MachinistShootKind
{
    Normal,
    Combo,
}

public enum MachinistChargeAim
{
    Forward,
    Up,
    Down,
    Crouch,
}

[RequireComponent(typeof(Rigidbody2D))]
public class PlayerAnim : MonoBehaviour // 玩家动画：下半身 AirPhase 参数驱动；上半身 locomotion 由 Play 驱动，蹲姿/着陆/转身走 FullBody 层
{
    public enum AirPhaseType // 空中阶段，同步到上下半身 Animator 的 AirPhase 参数
    {
        Ground = 0,
        Jump = 1,    // 原地起跳上升
        Fall = 2,    // 下落（含走出平台）
        Leap = 3,    // 带水平速度起跳上升
        LeapAir = 4, // 带水平速度起跳后的下落
    }

    enum AirTrack // 起跳类型，区分 Jump / Leap 轨道
    {
        None,
        Jump,
        Leap,
    }

    enum BodyDisplayMode // Split=上下半身，FullBody=全身层
    {
        Split,
        FullBody,
    }

    const string LandStateName = "Land";
    const string TurnStateName = "Turn";
    const string CrouchTurnStateName = "CrouchTurn";
    const string CrouchStateName = "Crouch";
    const string CrouchStartStateName = "CrouchStart";
    const string LookUpStartStateName = "LookUpStart";
    const string LookUpStateName = "LookUp";
    const string LookUpEndStateName = "LookUpEnd";
    const string LookDownStartStateName = "LookDownStart";
    const string LookDownStateName = "LookDown";
    const string LookDownEndStateName = "LookDownEnd";
    const string ShootStateName = "Shoot";
    const string LookUpShootStateName = "LookUpShoot";
    const string LookDownShootStateName = "LookDownShoot";
    const string CrouchShootStateName = "CrouchShoot";
    const string ComboShootStateName = "ComboShoot";
    const string LookUpComboShootStateName = "LookUpComboShoot";
    const string LookDownComboShootStateName = "LookDownComboShoot";
    const string CrouchComboShootStateName = "CrouchComboShoot";
    const string ChargeStartStateName = "ChargeStart";
    const string ChargeLoopStateName = "ChargeLoop";
    const string ChargeShootStateName = "ChargeShoot";
    const string LookUpChargeStartStateName = "LookUpChargeStart";
    const string LookUpChargeLoopStateName = "LookUpChargeLoop";
    const string LookUpChargeShootStateName = "LookUpChargeShoot";
    const string LookDownChargeStartStateName = "LookDownChargeStart";
    const string LookDownChargeLoopStateName = "LookDownChargeLoop";
    const string LookDownChargeShootStateName = "LookDownChargeShoot";
    const string ThrowStateName = "Throw";
    const string AirThrowStateName = "AirThrow";
    const string CrouchThrowStateName = "CrouchThrow";
    const string MeleeStateName = "Melee";
    const string AirMeleeStateName = "AirMelee";
    const string CrouchMeleeStateName = "CrouchMelee";
    const string DieStateName = "Die";
    const int UpperLookAirPhaseBlock = 5; // 无 AnyState 映射，Look 期间阻止 Ground→Idle 抢状态
    const string IsLookUpParam = "IsLookUp";
    const string IsLookDownParam = "IsLookDown";
    const string ShootTriggerParam = "Shoot";
    const string IsChargingParam = "IsCharging";

    [Header("Split 动画机")]
    public Animator upperAnimator;
    public Animator lowerAnimator;

    [Header("FullBody 动画机")]
    public Animator crouchAnimator;

    [Header("显示层")]
    public GameObject upBody;
    public GameObject downBody;
    [Tooltip("全身层：蹲姿、着陆、转身等")]
    public GameObject crouchBody;

    [Header("空中")]
    [Tooltip("竖直速度低于等于该值时视为开始下落")]
    public float descendVelocityThreshold = 0f;

    Rigidbody2D rb;
    PlayerMovement playerMovement;
    PhysicsCheck physicsCheck;
    BodyDisplayMode displayMode = BodyDisplayMode.Split;
    AirPhaseType airPhase = AirPhaseType.Ground;
    AirTrack airTrack = AirTrack.None;

    string activeFullBodyState;
    bool fullBodyAutoExit; // 全身动作播完后是否自动切回 Split

    bool isCrouching;
    bool isRunning;
    bool isShooting;
    bool isCharging;
    bool isThrowing;
    bool isMelee;
    bool isDead;
    bool isLookingUp;
    bool isLookingDown;
    bool isEndingLookUp;
    bool isEndingLookDown;
    bool jumpInvokedThisFrame; // 本帧是否起跳，防止同帧误判为走出平台
    bool wasGrounded;          // 上一帧是否在地面
    bool airStateInitialized;  // 是否完成首次地面检测，避免开局误判 Fall
    int lastUpperSyncedPhase = -1;   // 代码侧缓存，避免同帧重复 SetInteger 触发 AnyState
    string lastUpperLocomotionState; // 上半身 locomotion 由 Play 驱动
    bool lastUpperSyncedLookUp;
    bool lastUpperSyncedLookDown;
    bool lastUpperSyncedRun;
    string activeShootStateName;
    Animator activeShootAnimator;
    bool upperShootUsesAnimatorParam;
    Animator activeChargeAnimator;
    string activeChargeStateName;
    MachinistChargeAim activeChargeAim;
    string activeThrowStateName;
    Animator activeThrowAnimator;
    string activeMeleeStateName;
    Animator activeMeleeAnimator;
    float comboShootPinnedNormalized;
    bool comboShootInputInterrupted;
    bool pendingLookUpReleaseAfterCombo;
    bool pendingLookDownReleaseAfterCombo;

    public bool IsCrouching => isCrouching;
    public bool IsShooting => isShooting;
    public bool IsCharging => isCharging;
    public MachinistChargeAim ActiveChargeAim => activeChargeAim;
    public bool IsPlayingMachinistComboShoot =>
        isShooting && IsMachinistComboShootState(activeShootStateName);
    public bool IsThrowing => isThrowing;
    public bool IsMelee => isMelee;
    public bool IsDead => isDead;
    public bool IsLookingUp => isLookingUp || isEndingLookUp;
    public bool IsLookingDown => isLookingDown || isEndingLookDown;
    public AirPhaseType CurrentAirPhase => airPhase;
    public bool IsInFullBody => displayMode == BodyDisplayMode.FullBody;
    public string CurrentFullBodyState => activeFullBodyState;
    public bool IsPlayingLand =>
        displayMode == BodyDisplayMode.FullBody && activeFullBodyState == LandStateName;
    public bool IsTurning =>
        activeFullBodyState == TurnStateName || activeFullBodyState == CrouchTurnStateName;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        playerMovement = GetComponent<PlayerMovement>();
        physicsCheck = GetComponent<PhysicsCheck>();
    }

    void Start()
    {
        SetSplitDisplay();
        SyncSplitAnimators();

        var physicsCheck = GetComponent<PhysicsCheck>(); // 首帧检测地面，初始化 wasGrounded
        if (physicsCheck != null)
        {
            physicsCheck.Check();
            wasGrounded = physicsCheck.isGround;
            airStateInitialized = true;
        }
    }

    void LateUpdate() // 每帧末尾清零 jumpInvokedThisFrame
    {
        jumpInvokedThisFrame = false;
    }

    public void UpdateAirState(bool grounded) // PlayerMovement 传入地面检测结果
    {
        float velocityY = rb != null ? rb.linearVelocity.y : 0f;
        UpdateAirState(grounded, velocityY);
    }

    public void UpdateAirState(bool grounded, float velocityY) // 推进空中阶段并同步 Animator；蹲姿/全身层期间暂停；grounded 地面检测结果，velocityY 竖直速度
    {
        if (isDead)
            return;

        if (isCrouching && !grounded)
            ExitCrouchForAir(velocityY);

        if (isCrouching)
        {
            TryAutoExitCrouchTurn();
            MaintainShootCompletion();
            MaintainChargeCompletion();
            MaintainThrowCompletion();
            MaintainMeleeCompletion();
            wasGrounded = grounded;
            return;
        }

        if (displayMode == BodyDisplayMode.FullBody)
        {
            TryAutoExitFullBody(); // normalizedTime 兜底退出，配合 Animation Event
            MaintainChargeCompletion();
            wasGrounded = grounded;
            return;
        }

        AdvanceAirPhase(grounded, velocityY);
        SyncSplitAnimators();
        MaintainShootCompletion();
        MaintainChargeCompletion();
        MaintainThrowCompletion();
        MaintainMeleeCompletion();
        wasGrounded = grounded;
        airStateInitialized = true;
    }

    public void PlayJumpAnim(bool hasHorizontalInput) // 有水平输入走 Leap，否则 Jump；蹲姿起跳先退出 FullBody
    {
        InterruptLand();
        InterruptTurn();

        isCrouching = false;
        ResetFullBodyParams();
        SetSplitDisplay(); // 只切显示层，起跳前不同步 Ground，避免 AnyState 先切 Idle 再切 Jump

        jumpInvokedThisFrame = true;

        if (hasHorizontalInput)
        {
            airTrack = AirTrack.Leap;
            airPhase = AirPhaseType.Leap;
        }
        else
        {
            airTrack = AirTrack.Jump;
            airPhase = AirPhaseType.Jump;
        }

        InvalidateUpperLocomotionCache();
        SyncSplitAnimators();
    }

    public bool PlayTurnAnim() // 地面站立转身，进入全身 Turn 状态
    {
        if (isCrouching || displayMode == BodyDisplayMode.FullBody)
            return false;
        if (airPhase != AirPhaseType.Ground)
            return false;

        EnterFullBody(TurnStateName, autoExitOnComplete: true);
        return true;
    }

    public bool PlayCrouchTurnAnim() // 蹲伏转身，保持全身层
    {
        if (!isCrouching || crouchAnimator == null)
            return false;
        if (activeFullBodyState == CrouchTurnStateName)
            return false;

        ResetFullBodyParams();
        activeFullBodyState = CrouchTurnStateName;
        fullBodyAutoExit = true;
        crouchAnimator.Play(CrouchTurnStateName, 0, 0f);
        return true;
    }

    public bool TryPlayRunStopLand() // 站立地面跑动急停：松键边沿播全身 Land
    {
        if (!isRunning || isCrouching || IsTurning || IsUpperLookActive() || IsPlayingLand)
            return false;
        if (displayMode != BodyDisplayMode.Split || airPhase != AirPhaseType.Ground)
            return false;

        isRunning = false;
        EnterFullBodyLand();
        return true;
    }

    public void PlayIdleAnim() // 停止移动；地面 Split 层清除射击状态
    {
        isRunning = false;

        if (isCrouching)
        {
            crouchAnimator.SetBool("IsRun", false);
            return;
        }

        if (displayMode == BodyDisplayMode.Split && airPhase == AirPhaseType.Ground && !isShooting && !isCharging && !isThrowing && !isMelee)
            SyncSplitAnimators();
    }

    public void PlayRunAnim() // 跑步；蹲姿时只驱动全身层 IsRun
    {
        // 蓄力中禁止跑动动画（站立下半身 / 蹲姿全身）
        if (isCharging)
            return;

        if (isCrouching && (isShooting || isThrowing || isMelee))
            return;

        if (IsPlayingMachinistComboShoot)
            return;

        isRunning = true;

        if (InterruptLand())
            return;

        if (InterruptTurn())
            return;

        if (isCrouching)
        {
            crouchAnimator.SetBool("IsRun", true);
            return;
        }

        if (displayMode == BodyDisplayMode.Split && airPhase == AirPhaseType.Ground)
            SyncSplitAnimators();
    }

    public void PlayCrouchAnim() // 进入蹲姿，播 CrouchStart，需手动站起退出
    {
        if (isCrouching)
            return;

        InterruptLand();

        isCrouching = true;
        airPhase = AirPhaseType.Ground;
        airTrack = AirTrack.None;
        ClearLookState();
        ResetFullBodyParams();
        EnterFullBody(CrouchStartStateName, autoExitOnComplete: false);
    }

    public void PlayStandAnim() // 站起，恢复 Split 层
    {
        if (!isCrouching)
            return;

        isCrouching = false;
        ResetFullBodyParams();

        // 蹲姿射击/投掷/近战绑在 crouchAnimator；起身后 crouchBody 会关掉。
        // 若不先结束，isShooting 等标志会卡住，SyncSplitAnimators 永久跳过上半身同步。
        if (isShooting && activeShootAnimator == crouchAnimator)
            CompleteShoot();
        else if (isThrowing && activeThrowAnimator == crouchAnimator)
            CompleteThrow();
        else if (isMelee && activeMeleeAnimator == crouchAnimator)
            CompleteMelee();

        ExitFullBody();
        RestoreUpperLocomotion();
    }

    public bool TryPlayShootAnim() // 射击中再次按 J 会从头重播；可打断转身/着陆/蹲伏起步/仰视俯视起步
    {
        if (isMelee)
            CompleteMelee();

        if (isThrowing)
            CompleteThrow();

        if (IsPlayingLand)
            InterruptLand();
        else if (activeFullBodyState == TurnStateName)
            InterruptTurn();
        else if (activeFullBodyState == CrouchTurnStateName)
        {
            activeFullBodyState = null;
            fullBodyAutoExit = false;
            ResetFullBodyParams();
        }

        string stateName;
        Animator animator;

        if (isCrouching)
        {
            upperShootUsesAnimatorParam = false;
            stateName = CrouchShootStateName;
            animator = crouchAnimator;
        }
        else
        {
            if (upperAnimator == null)
                return false;

            animator = upperAnimator;
            bool shootLookUp = playerMovement != null && playerMovement.GetShootLookUp();
            bool shootLookDown = playerMovement != null && playerMovement.GetShootLookDown();

            if (shootLookUp)
            {
                stateName = LookUpShootStateName;
                upperShootUsesAnimatorParam = true;
                BeginLookShoot(true, false);
            }
            else if (shootLookDown)
            {
                stateName = LookDownShootStateName;
                upperShootUsesAnimatorParam = true;
                BeginLookShoot(false, true);
            }
            else
            {
                stateName = ShootStateName;
                upperShootUsesAnimatorParam = false;
                ClearLookStateForHorizontalShoot();
                BlockUpperAirPhaseForHorizontalShoot();
            }
        }

        if (animator == null)
            return false;

        isShooting = true;
        activeShootStateName = stateName;
        activeShootAnimator = animator;

        if (isCrouching)
            animator.Play(stateName, 0, 0f);
        else
        {
            animator.Play(stateName, 0, 0f); // 仰视/俯视也强制 Play，避免 Trigger 与 MaintainShootCompletion 竞态
            ResetUpperShootTrigger();
        }

        return true;
    }

    public bool TryPlayMachinistShootAnim(MachinistShootKind kind)
    {
        if (IsPlayingMachinistComboShoot)
            return false;

        if (isCharging)
            CancelMachinistCharge();

        if (isMelee)
            CompleteMelee();

        if (isThrowing)
            CompleteThrow();

        if (IsPlayingLand)
            InterruptLand();
        else if (activeFullBodyState == TurnStateName)
            InterruptTurn();
        else if (activeFullBodyState == CrouchTurnStateName)
        {
            activeFullBodyState = null;
            fullBodyAutoExit = false;
            ResetFullBodyParams();
        }

        string stateName;
        Animator animator;

        if (isCrouching)
        {
            upperShootUsesAnimatorParam = false;
            stateName = kind == MachinistShootKind.Combo
                ? CrouchComboShootStateName
                : CrouchShootStateName;
            animator = crouchAnimator;
        }
        else
        {
            if (upperAnimator == null)
                return false;

            animator = upperAnimator;
            bool shootLookUp = playerMovement != null && playerMovement.GetShootLookUp();
            bool shootLookDown = playerMovement != null && playerMovement.GetShootLookDown();

            if (shootLookUp)
            {
                stateName = kind == MachinistShootKind.Combo
                    ? LookUpComboShootStateName
                    : LookUpShootStateName;
                upperShootUsesAnimatorParam = true;
                BeginLookShoot(true, false);
            }
            else if (shootLookDown)
            {
                stateName = kind == MachinistShootKind.Combo
                    ? LookDownComboShootStateName
                    : LookDownShootStateName;
                upperShootUsesAnimatorParam = true;
                BeginLookShoot(false, true);
            }
            else
            {
                stateName = kind == MachinistShootKind.Combo
                    ? ComboShootStateName
                    : ShootStateName;
                upperShootUsesAnimatorParam = false;
                ClearLookStateForHorizontalShoot();
                BlockUpperAirPhaseForHorizontalShoot();
            }
        }

        if (animator == null)
            return false;

        isShooting = true;
        activeShootStateName = stateName;
        activeShootAnimator = animator;

        if (kind == MachinistShootKind.Combo)
        {
            comboShootPinnedNormalized = 0f;
            comboShootInputInterrupted = false;
            pendingLookUpReleaseAfterCombo = false;
            pendingLookDownReleaseAfterCombo = false;
        }

        if (isCrouching)
            animator.Play(stateName, 0, 0f);
        else
        {
            animator.Play(stateName, 0, 0f);
            ResetUpperShootTrigger();
        }

        return true;
    }

    public void InterruptMachinistComboShootFromInput()
    {
        if (!IsPlayingMachinistComboShoot)
            return;

        comboShootInputInterrupted = true;
        CompleteShoot();
    }

    static bool IsMachinistComboShootState(string stateName) =>
        stateName == ComboShootStateName
        || stateName == LookUpComboShootStateName
        || stateName == LookDownComboShootStateName
        || stateName == CrouchComboShootStateName;

    static bool IsLookMachinistComboShootState(string stateName) =>
        stateName == LookUpComboShootStateName
        || stateName == LookDownComboShootStateName;

    static bool IsUpperBodyLookShootState(string stateName) =>
        stateName == LookUpShootStateName
        || stateName == LookDownShootStateName
        || stateName == LookUpComboShootStateName
        || stateName == LookDownComboShootStateName;

    static bool IsNaturalShootExitState(AnimatorStateInfo info, string shootStateName)
    {
        if (shootStateName == LookUpShootStateName || shootStateName == LookUpComboShootStateName)
        {
            return info.IsName(LookUpStateName)
                || info.IsName(LookUpEndStateName)
                || info.IsName(LookUpStartStateName);
        }

        if (shootStateName == LookDownShootStateName || shootStateName == LookDownComboShootStateName)
        {
            return info.IsName(LookDownStateName)
                || info.IsName(LookDownEndStateName)
                || info.IsName(LookDownStartStateName);
        }

        if (shootStateName == ShootStateName || shootStateName == ComboShootStateName)
        {
            return info.IsName("Idle")
                || info.IsName("Run")
                || info.IsName("Jump")
                || info.IsName("Fall")
                || info.IsName("Leap")
                || info.IsName("LeapAir");
        }

        return false;
    }

    bool IsPlayingLookMachinistComboShoot() =>
        isShooting && IsLookMachinistComboShootState(activeShootStateName);

    public bool BeginMachinistCharge()
    {
        if (isCharging)
            return true;

        if (isMelee)
            CompleteMelee();

        if (isThrowing)
            CompleteThrow();

        if (isShooting)
            CompleteShoot();

        if (IsPlayingLand)
            InterruptLand();
        else if (activeFullBodyState == TurnStateName)
            InterruptTurn();
        else if (activeFullBodyState == CrouchTurnStateName)
        {
            activeFullBodyState = null;
            fullBodyAutoExit = false;
            ResetFullBodyParams();
        }

        // 清普通 Look，改由蓄力瞄准态接管上半身
        if (IsUpperLookActive())
            ClearLookState();

        isRunning = false;
        if (lowerAnimator != null)
            lowerAnimator.SetBool("IsRun", false);
        if (isCrouching && crouchAnimator != null)
            crouchAnimator.SetBool("IsRun", false);

        isCharging = true;
        var aim = ResolveInitialChargeAim();
        ApplyChargeAim(aim, playStart: true);
        if (activeChargeAnimator == null)
        {
            isCharging = false;
            activeChargeAim = MachinistChargeAim.Forward;
            return false;
        }

        return true;
    }

    public void SetChargeAim(MachinistChargeAim aim)
    {
        if (!isCharging)
            return;

        if (aim == activeChargeAim)
            return;

        ApplyChargeAim(aim, playStart: true);
    }

    public void SyncChargeAimFromInput(bool wantLookUp, bool wantLookDown, bool wantCrouch)
    {
        if (!isCharging)
            return;

        MachinistChargeAim aim;
        if (wantCrouch)
            aim = MachinistChargeAim.Crouch;
        else if (wantLookUp)
            aim = MachinistChargeAim.Up;
        else if (wantLookDown)
            aim = MachinistChargeAim.Down;
        else
            aim = MachinistChargeAim.Forward;

        SetChargeAim(aim);
    }

    MachinistChargeAim ResolveInitialChargeAim()
    {
        if (isCrouching)
            return MachinistChargeAim.Crouch;

        if (playerMovement != null)
        {
            if (playerMovement.GetShootLookUp())
                return MachinistChargeAim.Up;
            if (playerMovement.GetShootLookDown())
                return MachinistChargeAim.Down;
        }

        return MachinistChargeAim.Forward;
    }

    void ApplyChargeAim(MachinistChargeAim aim, bool playStart)
    {
        bool wantCrouch = aim == MachinistChargeAim.Crouch;

        if (wantCrouch && !isCrouching)
            EnterCrouchChargeDisplay();
        else if (!wantCrouch && isCrouching)
            ExitCrouchChargeDisplay();

        activeChargeAim = aim;

        Animator animator = wantCrouch ? crouchAnimator : upperAnimator;
        if (animator == null)
        {
            activeChargeAnimator = null;
            activeChargeStateName = null;
            return;
        }

        if (activeChargeAnimator != null && activeChargeAnimator != animator)
            activeChargeAnimator.SetBool(IsChargingParam, false);

        string startName = GetChargeStartStateName(aim);
        string loopName = GetChargeLoopStateName(aim);
        string stateName = playStart ? startName : loopName;

        activeChargeAnimator = animator;
        activeChargeStateName = stateName;
        animator.SetBool(IsChargingParam, !playStart);
        animator.Play(stateName, 0, 0f);
    }

    static string GetChargeStartStateName(MachinistChargeAim aim) => aim switch
    {
        MachinistChargeAim.Up => LookUpChargeStartStateName,
        MachinistChargeAim.Down => LookDownChargeStartStateName,
        _ => ChargeStartStateName,
    };

    static string GetChargeLoopStateName(MachinistChargeAim aim) => aim switch
    {
        MachinistChargeAim.Up => LookUpChargeLoopStateName,
        MachinistChargeAim.Down => LookDownChargeLoopStateName,
        _ => ChargeLoopStateName,
    };

    static string GetChargeShootStateName(MachinistChargeAim aim) => aim switch
    {
        MachinistChargeAim.Up => LookUpChargeShootStateName,
        MachinistChargeAim.Down => LookDownChargeShootStateName,
        _ => ChargeShootStateName,
    };

    void EnterCrouchChargeDisplay()
    {
        InterruptLand();
        ClearLookState();

        isCrouching = true;
        airPhase = AirPhaseType.Ground;
        airTrack = AirTrack.None;
        displayMode = BodyDisplayMode.FullBody;
        activeFullBodyState = ChargeLoopStateName;
        fullBodyAutoExit = false;
        ResetFullBodyParams();

        if (upBody != null)
            upBody.SetActive(false);
        if (downBody != null)
            downBody.SetActive(false);
        if (crouchBody != null)
            crouchBody.SetActive(true);
    }

    void ExitCrouchChargeDisplay()
    {
        isCrouching = false;
        ResetFullBodyParams();
        SetSplitDisplay();
        InvalidateUpperLocomotionCache();
    }

    public bool ReleaseMachinistCharge()
    {
        if (!isCharging || activeChargeAnimator == null)
            return false;

        var animator = activeChargeAnimator;
        var aim = activeChargeAim;
        string shootState = GetChargeShootStateName(aim);

        isCharging = false;
        activeChargeAnimator = null;
        activeChargeStateName = null;
        activeChargeAim = MachinistChargeAim.Forward;
        animator.SetBool(IsChargingParam, false);

        isShooting = true;
        activeShootStateName = shootState;
        activeShootAnimator = animator;
        upperShootUsesAnimatorParam = false;
        if (aim == MachinistChargeAim.Forward && !isCrouching)
            BlockUpperAirPhaseForHorizontalShoot();
        animator.Play(shootState, 0, 0f);
        return true;
    }

    void CancelMachinistCharge()
    {
        if (!isCharging)
            return;

        if (activeChargeAnimator != null)
            activeChargeAnimator.SetBool(IsChargingParam, false);

        isCharging = false;
        activeChargeAnimator = null;
        activeChargeStateName = null;
        activeChargeAim = MachinistChargeAim.Forward;
    }

    public bool TryPlayThrowAnim() // 投掷中再次按 U 会从头重播；可打断转身/着陆/蹲伏起步
    {
        if (isMelee)
            CompleteMelee();

        if (isShooting)
            CompleteShoot();

        if (isCharging)
            CancelMachinistCharge();

        if (IsPlayingLand)
            InterruptLand();
        else if (activeFullBodyState == TurnStateName)
            InterruptTurn();
        else if (activeFullBodyState == CrouchTurnStateName)
        {
            activeFullBodyState = null;
            fullBodyAutoExit = false;
            ResetFullBodyParams();
        }

        string stateName;
        Animator animator;

        if (isCrouching)
        {
            stateName = CrouchThrowStateName;
            animator = crouchAnimator;
        }
        else
        {
            if (upperAnimator == null)
                return false;

            animator = upperAnimator;
            if (IsUpperLookActive())
                StopLook();

            stateName = airPhase == AirPhaseType.Ground ? ThrowStateName : AirThrowStateName;
        }

        if (animator == null)
            return false;

        isThrowing = true;
        activeThrowStateName = stateName;
        activeThrowAnimator = animator;
        animator.Play(stateName, 0, 0f);
        return true;
    }

    public bool TryPlayMeleeAnim() // 近战可打断射击/投掷；站立/空中/蹲伏对应不同动画
    {
        if (isShooting)
            CompleteShoot();
        if (isThrowing)
            CompleteThrow();
        if (isCharging)
            CancelMachinistCharge();

        if (IsPlayingLand)
            InterruptLand();
        else if (activeFullBodyState == TurnStateName)
            InterruptTurn();
        else if (activeFullBodyState == CrouchTurnStateName)
        {
            activeFullBodyState = null;
            fullBodyAutoExit = false;
            ResetFullBodyParams();
        }

        string stateName;
        Animator animator;

        if (isCrouching)
        {
            stateName = CrouchMeleeStateName;
            animator = crouchAnimator;
            if (crouchAnimator != null)
                crouchAnimator.SetBool("IsRun", false);
        }
        else
        {
            if (upperAnimator == null)
                return false;

            animator = upperAnimator;
            if (IsUpperLookActive())
                StopLook();

            stateName = airPhase == AirPhaseType.Ground ? MeleeStateName : AirMeleeStateName;
        }

        if (animator == null)
            return false;

        isMelee = true;
        activeMeleeStateName = stateName;
        activeMeleeAnimator = animator;
        animator.Play(stateName, 0, 0f);
        return true;
    }

    public bool TryGetMeleeAnimProgress(out float normalizedTime)
    {
        normalizedTime = 0f;
        if (!isMelee || activeMeleeAnimator == null || string.IsNullOrEmpty(activeMeleeStateName))
            return false;

        var info = activeMeleeAnimator.GetCurrentAnimatorStateInfo(0);
        if (!info.IsName(activeMeleeStateName))
            return false;

        normalizedTime = info.normalizedTime;
        return true;
    }

    public void PlayDieAnim()
    {
        isDead = true;
        isCrouching = false;
        isRunning = false;
        isShooting = false;
        isThrowing = false;
        isMelee = false;
        ClearLookState();
        ResetFullBodyParams();
        EnterFullBody(DieStateName, autoExitOnComplete: false);
    }

    public bool TryGetDieAnimProgress(out float normalizedTime)
    {
        normalizedTime = 0f;
        if (!isDead || crouchAnimator == null)
            return false;

        var info = crouchAnimator.GetCurrentAnimatorStateInfo(0);
        if (!info.IsName(DieStateName))
            return false;

        normalizedTime = info.normalizedTime;
        return true;
    }

    public void ResetFromDeath()
    {
        isDead = false;
        activeFullBodyState = null;
        fullBodyAutoExit = false;
        ExitFullBody();
        RestoreUpperLocomotion();
    }

    public void SetLookUp(bool active)
    {
        if (isCharging)
            return;

        if (isCrouching || displayMode == BodyDisplayMode.FullBody)
        {
            if (IsUpperLookActive())
                StopLook();
            return;
        }

        if (active)
        {
            if (isLookingDown || isEndingLookDown)
                StopLook();

            pendingLookUpReleaseAfterCombo = false;
            isLookingUp = true;
            isEndingLookUp = false;
            ApplyUpperLookParams(lookUp: true, lookDown: false);
            TrySwitchHorizontalShootToLookShoot(LookUpShootStateName);
            EnsureUpperLookLoopWhenNotShooting(LookUpStateName, LookUpStartStateName, LookUpShootStateName, LookUpComboShootStateName);
        }
        else if (isLookingUp)
        {
            if (IsPlayingLookMachinistComboShoot())
            {
                pendingLookUpReleaseAfterCombo = true;
                return;
            }

            isLookingUp = false;
            isEndingLookUp = true;
            SetUpperLookBool(IsLookUpParam, false);
            if (isShooting && IsUpperBodyLookShootState(activeShootStateName))
                InterruptLookShootForLookEnd(LookUpEndStateName);
            else if (upperAnimator != null && !isShooting)
            {
                var info = upperAnimator.GetCurrentAnimatorStateInfo(0);
                if (info.IsName(LookUpComboShootStateName) || info.IsName(LookUpShootStateName))
                    upperAnimator.Play(LookUpEndStateName, 0, 0f);
            }
        }
    }

    public void SetLookDown(bool active)
    {
        if (isCharging)
            return;

        if (isCrouching || displayMode == BodyDisplayMode.FullBody)
        {
            if (IsUpperLookActive())
                StopLook();
            return;
        }

        if (active)
        {
            if (isLookingUp || isEndingLookUp)
                StopLook();

            pendingLookDownReleaseAfterCombo = false;
            isLookingDown = true;
            isEndingLookDown = false;
            ApplyUpperLookParams(lookUp: false, lookDown: true);
            TrySwitchHorizontalShootToLookShoot(LookDownShootStateName);
            EnsureUpperLookLoopWhenNotShooting(LookDownStateName, LookDownStartStateName, LookDownShootStateName, LookDownComboShootStateName);
        }
        else if (isLookingDown)
        {
            if (IsPlayingLookMachinistComboShoot())
            {
                pendingLookDownReleaseAfterCombo = true;
                return;
            }

            isLookingDown = false;
            isEndingLookDown = true;
            SetUpperLookBool(IsLookDownParam, false);
            if (isShooting && IsUpperBodyLookShootState(activeShootStateName))
                InterruptLookShootForLookEnd(LookDownEndStateName);
            else if (upperAnimator != null && !isShooting)
            {
                var info = upperAnimator.GetCurrentAnimatorStateInfo(0);
                if (info.IsName(LookDownComboShootStateName) || info.IsName(LookDownShootStateName))
                    upperAnimator.Play(LookDownEndStateName, 0, 0f);
            }
        }
    }

    void ClearLookStateForHorizontalShoot()
    {
        pendingLookUpReleaseAfterCombo = false;
        pendingLookDownReleaseAfterCombo = false;
        isLookingUp = false;
        isLookingDown = false;
        isEndingLookUp = false;
        isEndingLookDown = false;
        ResetUpperLookParams();
        ResetUpperShootTrigger();
    }

    void InterruptLookShootForLookEnd(string lookEndStateName)
    {
        isShooting = false;
        activeShootStateName = null;
        activeShootAnimator = null;
        upperShootUsesAnimatorParam = false;
        comboShootPinnedNormalized = 0f;
        comboShootInputInterrupted = false;

        ResetUpperShootTrigger();
        if (upperAnimator != null)
            upperAnimator.Play(lookEndStateName, 0, 0f);
    }

    void BeginLookShoot(bool lookUp, bool lookDown)
    {
        if (upperAnimator == null)
            return;

        if (lookUp)
        {
            if (isLookingDown || isEndingLookDown)
                StopLook();

            isLookingUp = true;
            isEndingLookUp = false;
            ApplyUpperLookParams(lookUp: true, lookDown: false);
        }
        else if (lookDown)
        {
            if (isLookingUp || isEndingLookUp)
                StopLook();

            isLookingDown = true;
            isEndingLookDown = false;
            ApplyUpperLookParams(lookUp: false, lookDown: true);
        }

        // 由调用方 Play 射击态；不再 SetTrigger，避免 Play 强切后 Trigger 残留，落地回 LookUpStart 时误进 LookUpShoot
        ResetUpperShootTrigger();
    }

    void TrySwitchHorizontalShootToLookShoot(string lookShootStateName)
    {
        if (!isShooting || upperAnimator == null)
            return;

        if (activeShootStateName != ShootStateName)
            return;

        upperShootUsesAnimatorParam = true;
        activeShootStateName = lookShootStateName;
        upperAnimator.Play(lookShootStateName, 0, 0f);
        ResetUpperShootTrigger();
    }

    void ResetUpperShootTrigger()
    {
        if (upperAnimator != null)
            upperAnimator.ResetTrigger(ShootTriggerParam);
    }

    void EnsureUpperLookLoopWhenNotShooting(
        string lookLoopState,
        string lookStartState,
        string lookShootState,
        string lookComboShootState)
    {
        if (isShooting || upperAnimator == null)
            return;

        ResetUpperShootTrigger();
        var info = upperAnimator.GetCurrentAnimatorStateInfo(0);
        if (info.IsName(lookLoopState) || info.IsName(lookStartState))
            return;

        // 未在射击却停在 Look*Shoot：强制回 Look 循环，避免落地后继续播完射击片段
        if (info.IsName(lookShootState) || info.IsName(lookComboShootState))
        {
            upperAnimator.Play(lookLoopState, 0, 0f);
            return;
        }

        upperAnimator.Play(lookStartState, 0, 0f);
    }

    void ExitCrouchForAir(float velocityY)
    {
        bool keepCharge = isCharging;
        MachinistChargeAim chargeAim = activeChargeAim;

        isCrouching = false;
        ResetFullBodyParams();
        SetSplitDisplay();

        bool hasHorizontal =
            isRunning || (rb != null && Mathf.Abs(rb.linearVelocity.x) > 0.1f);

        if (velocityY > descendVelocityThreshold)
        {
            if (hasHorizontal)
            {
                airTrack = AirTrack.Leap;
                airPhase = AirPhaseType.Leap;
            }
            else
            {
                airTrack = AirTrack.Jump;
                airPhase = AirPhaseType.Jump;
            }
        }
        else if (airTrack == AirTrack.Leap || hasHorizontal)
        {
            airTrack = AirTrack.Leap;
            airPhase = AirPhaseType.LeapAir;
        }
        else
        {
            airTrack = AirTrack.Jump;
            airPhase = AirPhaseType.Fall;
        }

        InvalidateUpperLocomotionCache();

        if (keepCharge)
        {
            // 蹲姿蓄力离地：保持蓄力，切到站立方向蓄力（Crouch → Forward，或按输入 Up/Down）
            if (chargeAim == MachinistChargeAim.Crouch)
                chargeAim = ResolveInitialChargeAim();
            if (chargeAim == MachinistChargeAim.Crouch)
                chargeAim = MachinistChargeAim.Forward;

            activeChargeAim = chargeAim; // 避免 Apply 再 ExitCrouch
            ApplyChargeAim(chargeAim, playStart: true);
            return;
        }

        SyncSplitAnimators();
    }

    public void EnterFullBody(string stateName, bool autoExitOnComplete) // 切全身层并从头播放指定状态
    {
        CancelUpperShootForFullBody();
        ClearLookState();
        displayMode = BodyDisplayMode.FullBody;
        activeFullBodyState = stateName;
        fullBodyAutoExit = autoExitOnComplete;

        if (upBody != null)
            upBody.SetActive(false);
        if (downBody != null)
            downBody.SetActive(false);
        if (crouchBody != null)
            crouchBody.SetActive(true);

        // 默认状态为 Crouch，与 CrouchStart/Land/Turn 不同，Play 不会与入场重复
        if (crouchAnimator != null)
            crouchAnimator.Play(stateName, 0, 0f);
    }

    public void ExitFullBody() // 恢复 Split 层并同步参数
    {
        displayMode = BodyDisplayMode.Split;
        activeFullBodyState = null;
        fullBodyAutoExit = false;

        if (crouchBody != null)
            crouchBody.SetActive(false);
        if (upBody != null)
            upBody.SetActive(true);
        if (downBody != null)
            downBody.SetActive(true);

        ResetUpperShootTrigger();
        InvalidateUpperLocomotionCache();

        // 落地后若仍按住上/下，直接回 Look 循环，避免冻结的 LookUpShoot 继续播，也避开 LookUpStart 上残留 Shoot 过渡
        if (TryRestoreLookAfterFullBody())
        {
            if (lowerAnimator != null)
            {
                lowerAnimator.SetInteger("AirPhase", (int)airPhase);
                lowerAnimator.SetBool("IsRun", isRunning);
            }
            return;
        }

        SyncSplitAnimators();
    }

    void CancelUpperShootForFullBody()
    {
        if (!isShooting && !isCharging)
        {
            ResetUpperShootTrigger();
            return;
        }

        isShooting = false;
        activeShootStateName = null;
        activeShootAnimator = null;
        upperShootUsesAnimatorParam = false;
        comboShootPinnedNormalized = 0f;
        comboShootInputInterrupted = false;
        pendingLookUpReleaseAfterCombo = false;
        pendingLookDownReleaseAfterCombo = false;

        if (isCharging)
            CancelMachinistCharge();

        ResetUpperShootTrigger();
    }

    bool TryRestoreLookAfterFullBody()
    {
        if (upperAnimator == null || isCrouching)
            return false;

        bool wantLookUp = playerMovement != null && playerMovement.GetShootLookUp();
        bool wantLookDown = playerMovement != null && playerMovement.GetShootLookDown();

        if (wantLookUp)
        {
            isLookingUp = true;
            isLookingDown = false;
            isEndingLookUp = false;
            isEndingLookDown = false;
            ApplyUpperLookParams(lookUp: true, lookDown: false);
            ResetUpperShootTrigger();
            upperAnimator.Play(LookUpStateName, 0, 0f);
            return true;
        }

        if (wantLookDown)
        {
            isLookingDown = true;
            isLookingUp = false;
            isEndingLookDown = false;
            isEndingLookUp = false;
            ApplyUpperLookParams(lookUp: false, lookDown: true);
            ResetUpperShootTrigger();
            upperAnimator.Play(LookDownStateName, 0, 0f);
            return true;
        }

        return false;
    }

    public void OnFullBodyAnimationFinished() // Animation Event：全身动作结束，触发 autoExit
    {
        if (!fullBodyAutoExit || string.IsNullOrEmpty(activeFullBodyState))
            return;

        if (activeFullBodyState == CrouchTurnStateName && isCrouching)
        {
            CompleteCrouchTurnExit();
            return;
        }

        if (displayMode != BodyDisplayMode.FullBody)
            return;

        CompleteAutoFullBodyExit();
    }

    public void OnLandAnimationFinished() => OnFullBodyAnimationFinished(); // 兼容旧事件名

    void EnterFullBodyLand() // 空中落地或地面急停播 Land，结束后回地面 Split
    {
        // 空中最终连击未播完就落地：直接结束，避免回到 Split 后被 pin 逻辑在地面重播
        if (IsPlayingMachinistComboShoot)
            CompleteShoot();

        EnterFullBody(LandStateName, autoExitOnComplete: true);
    }

    public bool InterruptLand() // 下半身有输入时立刻退出 Land，返回是否打断了 Land
    {
        if (!IsPlayingLand)
            return false;

        CompleteAutoFullBodyExit();
        return true;
    }

    public bool InterruptTurn() // 起跳/移动打断转身
    {
        if (!IsTurning)
            return false;

        if (activeFullBodyState == CrouchTurnStateName)
            CompleteCrouchTurnExit();
        else
            CompleteAutoFullBodyExit();

        return true;
    }

    void TryAutoExitCrouchTurn() // 蹲伏转身结束，回 Crouch 循环
    {
        if (!fullBodyAutoExit || activeFullBodyState != CrouchTurnStateName)
            return;
        if (!IsFullBodyStateDone(CrouchTurnStateName))
            return;

        CompleteCrouchTurnExit();
    }

    void CompleteCrouchTurnExit()
    {
        activeFullBodyState = null;
        fullBodyAutoExit = false;
        ResetFullBodyParams();

        if (crouchAnimator == null)
            return;

        if (isShooting)
            crouchAnimator.Play(CrouchShootStateName, 0, 0f);
        else if (isThrowing)
            crouchAnimator.Play(CrouchThrowStateName, 0, 0f);
        else if (isMelee)
            crouchAnimator.Play(CrouchMeleeStateName, 0, 0f);
        else
            crouchAnimator.Play(CrouchStateName, 0, 0f);
    }

    void TryAutoExitFullBody() // 轮询 normalizedTime，Animation Event 未触发时兜底
    {
        if (!fullBodyAutoExit || string.IsNullOrEmpty(activeFullBodyState))
            return;
        if (!IsFullBodyStateDone(activeFullBodyState))
            return;

        CompleteAutoFullBodyExit();
    }

    void CompleteAutoFullBodyExit()
    {
        if (activeFullBodyState == LandStateName)
        {
            airPhase = AirPhaseType.Ground;
            airTrack = AirTrack.None;
        }

        ExitFullBody();
    }

    void AdvanceAirPhase(bool grounded, float velocityY) // 空中阶段状态机
    {
        switch (airPhase)
        {
            case AirPhaseType.Ground:
                if (airStateInitialized && wasGrounded && !grounded && !jumpInvokedThisFrame)
                {
                    // 上坡踏上平台时竖直速度仍向上，不应误判为下落
                    if (velocityY > descendVelocityThreshold)
                        break;

                    // 刚离开斜坡踏上/走下平台时，沿坡速度不等于坠落
                    if (physicsCheck != null && physicsCheck.WasOnSlopeRecently && velocityY > -8f)
                        break;

                    airTrack = AirTrack.Jump;
                    airPhase = AirPhaseType.Fall;
                }
                break;

            case AirPhaseType.Jump:
                if (velocityY <= descendVelocityThreshold)
                    airPhase = AirPhaseType.Fall;
                break;

            case AirPhaseType.Leap:
                if (velocityY <= descendVelocityThreshold)
                    airPhase = AirPhaseType.LeapAir;
                break;

            case AirPhaseType.Fall:
            case AirPhaseType.LeapAir:
                if (grounded)
                    EnterFullBodyLand();
                break;
        }
    }

    void SyncSplitAnimators() // 下半身始终同步；Look 期间上半身由代码独占
    {
        if (lowerAnimator == null)
            return;

        // 蓄力锁移动：下半身保持 Idle，避免输入仍驱动奔跑
        if (isCharging)
            isRunning = false;

        int phase = (int)airPhase;
        lowerAnimator.SetInteger("AirPhase", phase);
        lowerAnimator.SetBool("IsRun", isRunning);

        if (IsUpperLookActive())
        {
            SyncUpperLookParams();
            MaintainUpperLookEndCompletion();
            return;
        }

        if (isShooting || isCharging || isThrowing || isMelee)
            return;

        if (upperAnimator == null)
            return;

        SyncUpperLocomotionViaPlay();
    }

    void SyncUpperLocomotionViaPlay()
    {
        if (upperAnimator == null)
            return;

        int phase = (int)airPhase;
        string stateName = GetUpperLocomotionStateName();
        bool phaseChanged = phase != lastUpperSyncedPhase;
        bool stateChanged = stateName != lastUpperLocomotionState;
        bool runChanged = airPhase == AirPhaseType.Ground && isRunning != lastUpperSyncedRun;

        if (!phaseChanged && !stateChanged && !runChanged)
            return;

        // 先 Play 定态，再写参数：参数仅用于阻止 AnyState Ground→Idle，已在目标态时不会重入 Jump/Leap
        if (stateChanged)
        {
            lastUpperLocomotionState = stateName;
            upperAnimator.Play(stateName, 0, 0f);
        }

        if (phaseChanged)
        {
            lastUpperSyncedPhase = phase;
            upperAnimator.SetInteger("AirPhase", phase);
        }

        if (airPhase == AirPhaseType.Ground && runChanged)
        {
            lastUpperSyncedRun = isRunning;
            upperAnimator.SetBool("IsRun", isRunning);
        }
    }

    void InvalidateUpperLocomotionCache()
    {
        lastUpperSyncedPhase = -1;
        lastUpperLocomotionState = null;
        lastUpperSyncedRun = !isRunning;
    }

    void BlockUpperAirPhaseForHorizontalShoot()
    {
        if (upperAnimator == null)
            return;

        if (lastUpperSyncedPhase == UpperLookAirPhaseBlock)
            return;

        lastUpperSyncedPhase = UpperLookAirPhaseBlock;
        upperAnimator.SetInteger("AirPhase", UpperLookAirPhaseBlock);
    }

    void ApplyUpperLookParams(bool lookUp, bool lookDown)
    {
        if (upperAnimator == null)
            return;

        lastUpperSyncedPhase = UpperLookAirPhaseBlock;
        lastUpperSyncedRun = isRunning;
        upperAnimator.SetInteger("AirPhase", UpperLookAirPhaseBlock);
        // 先写 Look，再清 IsRun，避免 Run 态先匹配 Run→Idle 而进不了 LookUpStart
        SetUpperLookBool(IsLookUpParam, lookUp);
        SetUpperLookBool(IsLookDownParam, lookDown);
        upperAnimator.SetBool("IsRun", false);
        lastUpperSyncedRun = false;
    }

    void SyncUpperLookParams()
    {
        if (upperAnimator == null)
            return;

        if (lastUpperSyncedPhase != UpperLookAirPhaseBlock)
        {
            lastUpperSyncedPhase = UpperLookAirPhaseBlock;
            upperAnimator.SetInteger("AirPhase", UpperLookAirPhaseBlock);
        }

        if (upperAnimator.GetBool("IsRun"))
            upperAnimator.SetBool("IsRun", false);

        bool wantLookUp = isLookingUp;
        bool wantLookDown = isLookingDown;
        SetUpperLookBool(IsLookUpParam, wantLookUp);
        SetUpperLookBool(IsLookDownParam, wantLookDown);
    }

    void SetUpperLookBool(string paramName, bool value)
    {
        if (upperAnimator == null)
            return;

        if (paramName == IsLookUpParam)
        {
            if (lastUpperSyncedLookUp == value)
                return;

            lastUpperSyncedLookUp = value;
        }
        else if (paramName == IsLookDownParam)
        {
            if (lastUpperSyncedLookDown == value)
                return;

            lastUpperSyncedLookDown = value;
        }

        upperAnimator.SetBool(paramName, value);
    }

    void MaintainUpperLookEndCompletion()
    {
        if (upperAnimator == null)
            return;

        var info = upperAnimator.GetCurrentAnimatorStateInfo(0);

        if (isEndingLookUp)
        {
            if (info.IsName(LookUpEndStateName) && info.normalizedTime >= 1f)
                CompleteUpperLookEnd();
            else if (!isShooting && !info.IsName(LookUpEndStateName))
                upperAnimator.Play(LookUpEndStateName, 0, 0f);
            return;
        }

        if (isEndingLookDown)
        {
            if (info.IsName(LookDownEndStateName) && info.normalizedTime >= 1f)
                CompleteUpperLookEnd();
            else if (!isShooting && !info.IsName(LookDownEndStateName))
                upperAnimator.Play(LookDownEndStateName, 0, 0f);
        }
    }

    void CompleteUpperLookEnd()
    {
        isEndingLookUp = false;
        isEndingLookDown = false;
        ResetUpperLookParams();
        RestoreUpperLocomotion();
    }

    void ExitUpperLookShootState()
    {
        if (upperAnimator == null)
        {
            pendingLookUpReleaseAfterCombo = false;
            pendingLookDownReleaseAfterCombo = false;
            RestoreUpperLocomotion();
            return;
        }

        if (pendingLookUpReleaseAfterCombo)
        {
            pendingLookUpReleaseAfterCombo = false;
            isLookingUp = false;
            isEndingLookUp = true;
            SetUpperLookBool(IsLookUpParam, false);
            upperAnimator.Play(LookUpEndStateName, 0, 0f);
            return;
        }

        if (pendingLookDownReleaseAfterCombo)
        {
            pendingLookDownReleaseAfterCombo = false;
            isLookingDown = false;
            isEndingLookDown = true;
            SetUpperLookBool(IsLookDownParam, false);
            upperAnimator.Play(LookDownEndStateName, 0, 0f);
            return;
        }

        if (isEndingLookUp)
        {
            upperAnimator.Play(LookUpEndStateName, 0, 0f);
            return;
        }

        if (isEndingLookDown)
        {
            upperAnimator.Play(LookDownEndStateName, 0, 0f);
            return;
        }

        bool wantLookUp = playerMovement != null && playerMovement.GetShootLookUp();
        bool wantLookDown = playerMovement != null && playerMovement.GetShootLookDown();

        ResetUpperShootTrigger();

        if (wantLookUp)
        {
            isLookingUp = true;
            isEndingLookUp = false;
            ApplyUpperLookParams(lookUp: true, lookDown: false);
            upperAnimator.Play(LookUpStateName, 0, 0f);
            return;
        }

        if (wantLookDown)
        {
            isLookingDown = true;
            isEndingLookDown = false;
            ApplyUpperLookParams(lookUp: false, lookDown: true);
            upperAnimator.Play(LookDownStateName, 0, 0f);
            return;
        }

        if (isLookingUp)
        {
            isLookingUp = false;
            isEndingLookUp = true;
            SetUpperLookBool(IsLookUpParam, false);
            upperAnimator.Play(LookUpEndStateName, 0, 0f);
            return;
        }

        if (isLookingDown)
        {
            isLookingDown = false;
            isEndingLookDown = true;
            SetUpperLookBool(IsLookDownParam, false);
            upperAnimator.Play(LookDownEndStateName, 0, 0f);
            return;
        }

        RestoreUpperLocomotion();
    }

    bool IsUpperLookActive() => isLookingUp || isLookingDown || isEndingLookUp || isEndingLookDown;

    void StopLook()
    {
        isLookingUp = false;
        isLookingDown = false;
        isEndingLookUp = false;
        isEndingLookDown = false;
        pendingLookUpReleaseAfterCombo = false;
        pendingLookDownReleaseAfterCombo = false;
        ResetUpperLookParams();
        ResetUpperShootTrigger();
        RestoreUpperLocomotion();
    }

    void ResetUpperLookParams()
    {
        lastUpperSyncedLookUp = true;
        lastUpperSyncedLookDown = true;
        SetUpperLookBool(IsLookUpParam, false);
        SetUpperLookBool(IsLookDownParam, false);
    }

    void RestoreUpperLocomotion()
    {
        if (upperAnimator == null)
            return;

        InvalidateUpperLocomotionCache();
        SyncUpperLocomotionViaPlay();
    }

    string GetUpperLocomotionStateName()
    {
        if (airPhase == AirPhaseType.Ground)
            return isRunning ? "Run" : "Idle";

        switch (airPhase)
        {
            case AirPhaseType.Jump: return "Jump";
            case AirPhaseType.Fall: return "Fall";
            case AirPhaseType.Leap: return "Leap";
            case AirPhaseType.LeapAir: return "LeapAir";
            default: return "Idle";
        }
    }

    void ClearLookState()
    {
        isLookingUp = false;
        isLookingDown = false;
        isEndingLookUp = false;
        isEndingLookDown = false;
        pendingLookUpReleaseAfterCombo = false;
        pendingLookDownReleaseAfterCombo = false;
        ResetUpperLookParams();
        ResetUpperShootTrigger();
    }

    void SetSplitDisplay() // 强制切回 Split 显示，不自动 Sync
    {
        displayMode = BodyDisplayMode.Split;
        activeFullBodyState = null;
        fullBodyAutoExit = false;

        if (crouchBody != null)
            crouchBody.SetActive(false);
        if (upBody != null)
            upBody.SetActive(true);
        if (downBody != null)
            downBody.SetActive(true);
    }

    void MaintainShootCompletion()
    {
        if (!isShooting || activeShootAnimator == null || string.IsNullOrEmpty(activeShootStateName))
            return;

        // crouchBody 被隐藏后 Animator 停更，必须兜底结束，否则上半身永久卡 Idle
        if (!activeShootAnimator.isActiveAndEnabled)
        {
            CompleteShoot();
            return;
        }

        if (!upperShootUsesAnimatorParam && activeShootAnimator == upperAnimator)
            BlockUpperAirPhaseForHorizontalShoot();

        var info = activeShootAnimator.GetCurrentAnimatorStateInfo(0);

        if (IsMachinistComboShootState(activeShootStateName))
        {
            if (comboShootInputInterrupted)
                return;

            if (info.IsName(activeShootStateName))
            {
                comboShootPinnedNormalized = info.normalizedTime;
                if (info.normalizedTime < 1f)
                    return;

                CompleteShoot();
                return;
            }

            activeShootAnimator.Play(activeShootStateName, 0, comboShootPinnedNormalized);
            return;
        }

        if (!info.IsName(activeShootStateName))
        {
            if (IsNaturalShootExitState(info, activeShootStateName))
            {
                CompleteShoot();
                return;
            }

            activeShootAnimator.Play(activeShootStateName, 0, 0f);
            return;
        }

        if (info.normalizedTime < 1f)
            return;

        CompleteShoot();
    }

    void MaintainChargeCompletion()
    {
        if (!isCharging || activeChargeAnimator == null || string.IsNullOrEmpty(activeChargeStateName))
            return;

        var info = activeChargeAnimator.GetCurrentAnimatorStateInfo(0);
        if (!info.IsName(activeChargeStateName))
            return;

        string startName = GetChargeStartStateName(activeChargeAim);
        string loopName = GetChargeLoopStateName(activeChargeAim);
        if (activeChargeStateName != startName)
            return;

        // 空占位（无 motion / 长度≈0）立即进 Loop；有剪辑则等播完
        if (info.length > 0.001f && info.normalizedTime < 1f)
            return;

        activeChargeStateName = loopName;
        activeChargeAnimator.SetBool(IsChargingParam, true);
        activeChargeAnimator.Play(loopName, 0, 0f);
    }

    void MaintainThrowCompletion()
    {
        if (!isThrowing || activeThrowAnimator == null || string.IsNullOrEmpty(activeThrowStateName))
            return;

        if (!activeThrowAnimator.isActiveAndEnabled)
        {
            CompleteThrow();
            return;
        }

        var info = activeThrowAnimator.GetCurrentAnimatorStateInfo(0);
        if (!info.IsName(activeThrowStateName))
        {
            CompleteThrow();
            return;
        }

        if (info.normalizedTime < 1f)
            return;

        CompleteThrow();
    }

    void MaintainMeleeCompletion()
    {
        if (!isMelee || activeMeleeAnimator == null || string.IsNullOrEmpty(activeMeleeStateName))
            return;

        if (!activeMeleeAnimator.isActiveAndEnabled)
        {
            CompleteMelee();
            return;
        }

        var info = activeMeleeAnimator.GetCurrentAnimatorStateInfo(0);
        if (!info.IsName(activeMeleeStateName))
        {
            if (isCrouching
                && activeMeleeStateName == CrouchMeleeStateName
                && (info.IsName(CrouchStateName) || info.IsName("CrouchMove")))
                return;

            CompleteMelee();
            return;
        }

        if (info.normalizedTime < 1f)
            return;

        CompleteMelee();
    }

    void CompleteMelee()
    {
        isMelee = false;
        activeMeleeStateName = null;
        activeMeleeAnimator = null;

        if (isCrouching)
        {
            if (crouchAnimator == null)
                return;

            if (isRunning)
            {
                crouchAnimator.SetBool("IsRun", true);
                crouchAnimator.Play(CrouchStateName, 0, 0f);
            }
            else
            {
                crouchAnimator.SetBool("IsRun", false);
                crouchAnimator.Play(CrouchStateName, 0, 0f);
            }

            return;
        }

        RestoreUpperLocomotion();
    }

    void CompleteThrow()
    {
        isThrowing = false;
        activeThrowStateName = null;
        activeThrowAnimator = null;

        if (isCrouching)
        {
            if (crouchAnimator == null)
                return;

            if (isRunning)
            {
                crouchAnimator.SetBool("IsRun", true);
                crouchAnimator.Play(CrouchStateName, 0, 0f);
            }
            else
            {
                crouchAnimator.SetBool("IsRun", false);
                crouchAnimator.Play(CrouchStateName, 0, 0f);
            }

            return;
        }

        RestoreUpperLocomotion();
    }

    void CompleteShoot()
    {
        isShooting = false;
        activeShootStateName = null;
        activeShootAnimator = null;
        comboShootPinnedNormalized = 0f;
        comboShootInputInterrupted = false;
        ResetUpperShootTrigger();

        if (isCrouching)
        {
            if (crouchAnimator == null)
                return;

            if (isRunning)
            {
                crouchAnimator.SetBool("IsRun", true);
                crouchAnimator.Play(CrouchStateName, 0, 0f);
            }
            else
            {
                crouchAnimator.SetBool("IsRun", false);
                crouchAnimator.Play(CrouchStateName, 0, 0f);
            }

            return;
        }

        if (upperShootUsesAnimatorParam)
        {
            upperShootUsesAnimatorParam = false;
            ExitUpperLookShootState();
            return;
        }

        if (isLookingUp)
            ApplyUpperLookParams(lookUp: true, lookDown: false);
        else if (isLookingDown)
            ApplyUpperLookParams(lookUp: false, lookDown: true);
        else
            RestoreUpperLocomotion();
    }

    void ResetFullBodyParams()
    {
        if (crouchAnimator == null)
            return;

        crouchAnimator.SetBool("IsRun", false);
    }

    bool IsFullBodyStateDone(string stateName, int layer = 0)
    {
        if (crouchAnimator == null)
            return true;

        var info = crouchAnimator.GetCurrentAnimatorStateInfo(layer);
        return info.IsName(stateName) && info.normalizedTime >= 1f;
    }
}
