using UnityEngine;

public enum MachinistShootKind
{
    Normal,
    Combo1,
    Combo2,
    Combo,
    Blast,
    Electric,
    Machine,
}

public enum MachinistChargeAim
{
    Forward,
    Up,
    Down,
    Crouch,
}

[RequireComponent(typeof(Rigidbody2D))]
public class PlayerAnim : PlayerAnimBase // 玩家动画：下半身 AirPhase 参数驱动；上半身 locomotion 由 Play 驱动，蹲姿/着陆/转身走 FullBody 层
{
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
    const string Combo1ShootStateName = "combo1_shoot";
    const string Combo2ShootStateName = "combo2_shoot";
    const string LookUpComboShootStateName = "LookUpComboShoot";
    const string LookDownComboShootStateName = "LookDownComboShoot";
    const string CrouchComboShootStateName = "CrouchComboShoot";
    const string CrouchCombo1ShootStateName = "CrouchCombo1Shoot";
    const string CrouchCombo2ShootStateName = "CrouchCombo2Shoot";
    const string AirComboShootStateName = "EXcombo_shoot_air";
    const string LookUpBlastShootStateName = "LookUpBlastShoot";
    const string LookDownBlastShootStateName = "LookDownBlastShoot";
    const string CrouchBlastShootStateName = "CrouchBlastShoot";
    const string AirBlastShootStateName = "AirBlastShoot";
    const string LookUpElectricShootStateName = "LookUpElectricShoot";
    const string LookDownElectricShootStateName = "LookDownElectricShoot";
    const string CrouchElectricShootStateName = "CrouchElectricShoot";
    const string AirElectricShootStateName = "AirElectricShoot";
    const string LookUpMachineShootStateName = "LookUpMachineShoot";
    const string LookDownMachineShootStateName = "LookDownMachineShoot";
    const string CrouchMachineShootStateName = "CrouchMachineShoot";
    const string AirMachineShootStateName = "AirMachineShoot";
    const string LoadBulletStateName = "LoadBullet";
    const string MeleeLoadStateName = "M_Melee_Load";
    const string MeleeIdleUpStateName = "M_Melee_Idle_up";
    const string MeleeRunUpStateName = "M_Melee_Run_up";
    const string MeleeJumpUpStateName = "M_Melee_AirUp_Up";
    const string MeleeFallUpStateName = "M_Melee_AirDown_Up";
    const string MeleeLandFullStateName = "M_Melee_Land_Full";
    const string ChargeStartStateName = "ChargeStart";
    const string ChargeLoopStateName = "ChargeLoop";
    const string ChargeShootStateName = "ChargeShoot";
    const string LookUpChargeStartStateName = "LookUpChargeStart";
    const string LookUpChargeLoopStateName = "LookUpChargeLoop";
    const string LookUpChargeShootStateName = "LookUpChargeShoot";
    const string LookDownChargeStartStateName = "LookDownChargeStart";
    const string LookDownChargeLoopStateName = "LookDownChargeLoop";
    const string LookDownChargeShootStateName = "LookDownChargeShoot";
    const string DispatchStateName = "M_Dispatch";
    const string DispatchLoopStateName = "M_Dispatch_loop";
    const string CrouchDispatchStateName = "M_CrouchDispatch";
    const string CrouchDispatchLoopStateName = "M_CrouchDispatch_loop";
    const string ThrowStateName = "Throw";
    const string AirThrowStateName = "AirThrow";
    const string CrouchThrowStateName = "CrouchThrow";
    const string MeleeStateName = "Melee";
    const string AirMeleeStateName = "AirMelee";
    const string CrouchMeleeStateName = "CrouchMelee";
    const string DieStateName = "Die";
    const string RollStateName = "Roll";
    const string WeaponSwitchStateName = "WeaponSwitch";
    const string CrouchWeaponSwitchStateName = "CrouchWeaponSwitch";
    const string RecallStateName = "M_Back";
    const int UpperLookAirPhaseBlock = 5; // 无 AnyState 映射，Look 期间阻止 Ground→Idle 抢状态
    const string IsLookUpParam = "IsLookUp";
    const string IsLookDownParam = "IsLookDown";
    const string ShootTriggerParam = "Shoot";
    const string IsChargingParam = "IsCharging";
    const string WeaponIdParam = "WeaponID";

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

    [Header("机械师近战（特殊弹 L）")]
    [Tooltip("仅当未传入状态名时使用。出刀顺序请在 MachinistShooting 的近战连段里配。")]
    [HideInInspector]
    [SerializeField] string[] machinistMeleeAttackUpStates =
    {
        "M_Melee_Attack1_up",
        "M_Melee_Attack2_up",
        "M_Melee_Attack3_up",
    };
    [HideInInspector]
    [SerializeField] string[] machinistMeleeAttackDownStates =
    {
        "M_Melee_Attack1_down",
        "M_Melee_Attack2_down",
        "M_Melee_Attack3_down",
    };

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
    bool sustainShoot;
    bool isCharging;
    bool isHeavySpinFiring;
    bool isDispatching;
    bool dispatchHoldForLoop;
    bool dispatchAutoEndOnIntroComplete;
    bool dispatchInLoop;
    bool dispatchIsCrouch;
    bool isThrowing;
    bool isMelee;
    bool isSwitchingWeapon;
    bool isRecalling;
    bool isDead;
    bool isRolling;
    bool rollPoseActive;
    Vector3 fullBodyRestLocalPos;
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
    Animator activeDispatchAnimator;
    string activeDispatchStateName;
    string activeThrowStateName;
    Animator activeThrowAnimator;
    string activeMeleeStateName;
    Animator activeMeleeAnimator;
    string activeWeaponSwitchStateName;
    Animator activeWeaponSwitchAnimator;
    string activeRecallStateName;
    Animator activeRecallAnimator;
    AnimatorOverrideController upperOverrideController;
    AnimatorOverrideController crouchOverrideController;
    RuntimeAnimatorController upperBaseController;
    RuntimeAnimatorController crouchBaseController;
    float comboShootPinnedNormalized;
    bool comboShootInputInterrupted;
    float loadBulletPinnedNormalized;
    bool pendingLookUpReleaseAfterCombo;
    bool pendingLookDownReleaseAfterCombo;
    bool forcedCrouchComboActive;
    bool forcedCrouchComboWasAlreadyCrouching;
    bool forcedAirComboActive;
    bool machinistMeleeStance;
    bool isMachinistMeleeAttacking;
    bool isMachinistAirMelee;
    bool lowerMeleePlayed;
    string activeLowerMeleeStateName;

    public override bool IsCrouching => isCrouching;
    public override bool IsShooting => isShooting;
    public override bool IsCharging => isCharging;
    public override bool LocksMovementWhileCharging => isCharging && isCrouching;
    public override bool IsHeavySpinFiring => isHeavySpinFiring;
    public override bool IsDispatching => isDispatching;
    public override MachinistChargeAim ActiveChargeAim => activeChargeAim;
    public override bool IsPlayingMachinistComboShoot =>
        isShooting && IsMachinistComboShootState(activeShootStateName);
    public override bool IsPlayingMachinistChargeShoot =>
        isShooting && IsMachinistChargeShootState(activeShootStateName);
    public override bool IsPlayingMachineShoot =>
        isShooting && IsMachineShootState(activeShootStateName);
    public override bool IsForcedCrouchCombo =>
        forcedCrouchComboActive && (IsPlayingMachinistComboShoot || IsPlayingMachineShoot);
    public override bool IsForcedAirCombo =>
        forcedAirComboActive && (IsPlayingMachinistComboShoot || IsPlayingMachineShoot);
    public override bool IsSustainingAirHang =>
        IsForcedAirCombo
        || IsPlayingLookDownSpecialShoot()
        || IsPlayingMachinistChargeShoot
        || isMachinistAirMelee;
    public override bool IsPlayingLoadBullet =>
        isShooting && IsLoadBulletState(activeShootStateName);
    public override bool IsThrowing => isThrowing;
    public override bool IsMelee => isMelee;
    public override bool IsMachinistMeleeStance => machinistMeleeStance;
    public override bool IsMachinistMeleeAttacking => isMachinistMeleeAttacking;
    public int CurrentMachinistMeleeStep { get; private set; } = -1;
    public override bool IsSwitchingWeapon => isSwitchingWeapon;
    public override bool IsRecalling => isRecalling;
    public override bool IsDead => isDead;
    public override bool IsLookingUp => isLookingUp || isEndingLookUp;
    public override bool IsLookingDown => isLookingDown || isEndingLookDown;
    public override AirPhaseType CurrentAirPhase => airPhase;
    public override bool IsInFullBody => displayMode == BodyDisplayMode.FullBody;
    public override string CurrentFullBodyState => activeFullBodyState;
    public override bool IsPlayingLand =>
        displayMode == BodyDisplayMode.FullBody && IsLandFullBodyState;

    bool IsLandFullBodyState =>
        activeFullBodyState == LandStateName || activeFullBodyState == MeleeLandFullStateName;

    public override bool IsTurning =>
        activeFullBodyState == TurnStateName || activeFullBodyState == CrouchTurnStateName;
    public override bool IsRolling =>
        isRolling || (displayMode == BodyDisplayMode.FullBody && activeFullBodyState == RollStateName);

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        playerMovement = GetComponent<PlayerMovement>();
        physicsCheck = GetComponent<PhysicsCheck>();
        EnsureOverrideControllers();
    }

    void Start()
    {
        EnsureOverrideControllers();
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

    public override void UpdateAirState(bool grounded) // PlayerMovement 传入地面检测结果
    {
        float velocityY = rb != null ? rb.linearVelocity.y : 0f;
        UpdateAirState(grounded, velocityY);
    }

    public override void UpdateAirState(bool grounded, float velocityY) // 推进空中阶段并同步 Animator；蹲姿/全身层期间暂停；grounded 地面检测结果，velocityY 竖直速度
    {
        if (isDead)
            return;

        if (isCrouching && !grounded && !forcedCrouchComboActive)
            ExitCrouchForAir(velocityY);

        if (isCrouching)
        {
            TryAutoExitCrouchTurn();
            MaintainShootCompletion();
            MaintainChargeCompletion();
            MaintainDispatchCompletion();
            MaintainThrowCompletion();
            MaintainMeleeCompletion();
            MaintainWeaponSwitchCompletion();
            MaintainRecallCompletion();
            wasGrounded = grounded;
            return;
        }

        if (displayMode == BodyDisplayMode.FullBody)
        {
            // 空中全身终结：不走 autoExit，由 MaintainShootCompletion 结束
            if (forcedAirComboActive)
            {
                MaintainShootCompletion();
                wasGrounded = grounded;
                return;
            }

            TryAutoExitFullBody(); // normalizedTime 兜底退出，配合 Animation Event
            MaintainChargeCompletion();
            MaintainDispatchCompletion();
            MaintainWeaponSwitchCompletion();
            MaintainRecallCompletion();
            wasGrounded = grounded;
            return;
        }

        AdvanceAirPhase(grounded, velocityY);
        SyncSplitAnimators();
        MaintainShootCompletion();
        MaintainChargeCompletion();
        MaintainDispatchCompletion();
        MaintainThrowCompletion();
        MaintainMeleeCompletion();
        MaintainWeaponSwitchCompletion();
        MaintainRecallCompletion();
        wasGrounded = grounded;
        airStateInitialized = true;
    }

    public override void PlayJumpAnim(bool hasHorizontalInput) // 有水平输入走 Leap，否则 Jump；蹲姿起跳先退出 FullBody
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

    public override bool PlayTurnAnim() // 地面站立转身，进入全身 Turn 状态
    {
        if (IsPlayingLand)
            InterruptLand();

        if (isCrouching || displayMode == BodyDisplayMode.FullBody || isDispatching || isRecalling)
            return false;
        if (airPhase != AirPhaseType.Ground)
            return false;
        // Turn 会 CancelUpperShootForFullBody，走路换向不得掐装弹
        if (IsPlayingLoadBullet)
            return false;

        EnterFullBody(TurnStateName, autoExitOnComplete: true);
        return true;
    }

    public override bool PlayCrouchTurnAnim() // 蹲伏转身，保持全身层
    {
        if (!isCrouching || crouchAnimator == null || isDispatching || isRecalling)
            return false;
        if (activeFullBodyState == CrouchTurnStateName)
            return false;
        if (IsPlayingLoadBullet)
            return false;

        ResetFullBodyParams();
        activeFullBodyState = CrouchTurnStateName;
        fullBodyAutoExit = true;
        crouchAnimator.Play(CrouchTurnStateName, 0, 0f);
        return true;
    }

    public override bool TryPlayRunStopLand() // 站立地面跑动急停：松键边沿播全身 Land
    {
        if (!isRunning || isCrouching || IsTurning || IsPlayingLand)
            return false;
        if (displayMode != BodyDisplayMode.Split || airPhase != AirPhaseType.Ground)
            return false;
        // 急停不得打断射击/投掷/近战等；这些动作进行中只回 Idle
        if (IsOccupiedByAction())
            return false;

        isRunning = false;
        EnterFullBodyLand();
        return true;
    }

    bool IsOccupiedByAction() =>
        isShooting || isCharging || isDispatching || isThrowing || isMelee
        || isSwitchingWeapon || isRecalling || isRolling || IsUpperLookActive();

    public override void PlayIdleAnim() // 停止移动；地面 Split 层清除射击状态
    {
        isRunning = false;

        if (isCrouching)
        {
            crouchAnimator.SetBool("IsRun", false);
            return;
        }

        if (displayMode == BodyDisplayMode.Split && airPhase == AirPhaseType.Ground
            && !isShooting && !isCharging && !isDispatching && !isThrowing && !isMelee && !isSwitchingWeapon && !isRecalling)
            SyncSplitAnimators();
    }

    public override void PlayRunAnim() // 跑步；蹲姿时只驱动全身层 IsRun
    {
        if (isDispatching && isCrouching)
            return;

        if (isCrouching && (isShooting || isCharging || isThrowing || isMelee || isSwitchingWeapon || isRecalling))
            return;

        if (IsPlayingMachinistComboShoot || IsPlayingMachinistChargeShoot)
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

    public override void PlayCrouchAnim() // 进入蹲姿，播 CrouchStart，需手动站起退出
    {
        if (isCrouching)
            return;

        if (isDispatching)
            EndDispatch();

        if (isSwitchingWeapon)
            CompleteWeaponSwitch();

        if (isRecalling)
            CompleteRecall();

        InterruptLand();

        isCrouching = true;
        airPhase = AirPhaseType.Ground;
        airTrack = AirTrack.None;
        ClearLookState();
        ResetFullBodyParams();
        EnterFullBody(CrouchStartStateName, autoExitOnComplete: false);
    }

    public override void PlayStandAnim() // 站起，恢复 Split 层
    {
        if (!isCrouching)
            return;

        if (forcedCrouchComboActive)
            return;

        if (isDispatching)
            EndDispatch();

        if (isSwitchingWeapon)
            CompleteWeaponSwitch();

        if (isRecalling)
            CompleteRecall();

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

    public override bool TryPlayShootAnim() // 射击中再次按 J 会从头重播；可打断转身/着陆/蹲伏起步/仰视俯视起步
    {
        if (isSwitchingWeapon || isRecalling || isRolling)
            return false;

        if (IsPlayingMachinistComboShoot || IsPlayingLoadBullet || isMachinistMeleeAttacking)
            return false;

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

    public override bool TryPlayMachinistShootAnim(MachinistShootKind kind)
    {
        if (isSwitchingWeapon || isRecalling)
            return false;

        if (isDispatching)
            return false;

        if (IsPlayingMachinistComboShoot)
            return false;

        if (IsPlayingLoadBullet)
            return false;

        if (isMachinistMeleeAttacking)
            return false;

        // MachineShoot 仅允许被下一次 MachineShoot 打断，不被普通/Combo/Blast/Electric 打断
        if (IsPlayingMachineShoot && kind != MachinistShootKind.Machine)
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

        // 特殊弹 S：仰视/俯视走 Look Machine；空中水平走 Air Machine；地面/蹲姿走 Crouch Machine
        bool shootLookUp = !isCrouching && playerMovement != null && playerMovement.GetShootLookUp();
        bool shootLookDown = !isCrouching && playerMovement != null && playerMovement.GetShootLookDown();
        bool isHorizontalForward = !shootLookUp && !shootLookDown;

        if (kind == MachinistShootKind.Machine && isHorizontalForward)
        {
            bool grounded = physicsCheck != null && physicsCheck.isGround;
            if (!grounded && !isCrouching)
                return PlayForcedAirMachineShoot();
            return PlayForcedCrouchMachineShoot();
        }

        // 前方终结连击 / Blast / Electric：地面/已蹲 → 蹲姿全身；空中未蹲 → 空中全身
        if ((kind == MachinistShootKind.Combo
                || kind == MachinistShootKind.Blast
                || kind == MachinistShootKind.Electric)
            && isHorizontalForward)
        {
            bool grounded = physicsCheck != null && physicsCheck.isGround;
            if (!grounded && !isCrouching)
            {
                if (kind == MachinistShootKind.Blast)
                    return PlayForcedAirBlastShoot();
                if (kind == MachinistShootKind.Electric)
                    return PlayForcedAirElectricShoot();
                return PlayForcedAirComboShoot();
            }

            if (kind == MachinistShootKind.Blast)
                return PlayForcedCrouchBlastShoot();
            if (kind == MachinistShootKind.Electric)
                return PlayForcedCrouchElectricShoot();
            return PlayForcedCrouchComboShoot();
        }

        string stateName;
        Animator animator;

        if (isCrouching)
        {
            upperShootUsesAnimatorParam = false;
            stateName = ResolveCrouchHorizontalShootState(kind);
            animator = crouchAnimator;
        }
        else
        {
            if (upperAnimator == null)
                return false;

            animator = upperAnimator;

            if (shootLookUp)
            {
                stateName = kind switch
                {
                    MachinistShootKind.Combo => LookUpComboShootStateName,
                    MachinistShootKind.Blast => LookUpBlastShootStateName,
                    MachinistShootKind.Electric => LookUpElectricShootStateName,
                    MachinistShootKind.Machine => LookUpMachineShootStateName,
                    _ => LookUpShootStateName,
                };
                upperShootUsesAnimatorParam = true;
                if (kind == MachinistShootKind.Machine)
                {
                    if (forcedCrouchComboActive)
                        ExitForcedCrouchComboDisplay();
                    if (forcedAirComboActive)
                        ExitForcedAirComboDisplay(clearHang: false);
                }
                BeginLookShoot(true, false);
            }
            else if (shootLookDown)
            {
                stateName = kind switch
                {
                    MachinistShootKind.Combo => LookDownComboShootStateName,
                    MachinistShootKind.Blast => LookDownBlastShootStateName,
                    MachinistShootKind.Electric => LookDownElectricShootStateName,
                    MachinistShootKind.Machine => LookDownMachineShootStateName,
                    _ => LookDownShootStateName,
                };
                upperShootUsesAnimatorParam = true;
                if (kind == MachinistShootKind.Machine)
                {
                    if (forcedCrouchComboActive)
                        ExitForcedCrouchComboDisplay();
                    if (forcedAirComboActive)
                        ExitForcedAirComboDisplay(clearHang: false);
                }
                BeginLookShoot(false, true);
            }
            else
            {
                stateName = ResolveUpperHorizontalShootState(kind);
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

        if (kind == MachinistShootKind.Combo
            || kind == MachinistShootKind.Blast
            || kind == MachinistShootKind.Electric
            || kind == MachinistShootKind.Machine)
        {
            if (kind != MachinistShootKind.Machine)
            {
                comboShootPinnedNormalized = 0f;
                comboShootInputInterrupted = false;
            }
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

    static string ResolveCrouchHorizontalShootState(MachinistShootKind kind) => kind switch
    {
        MachinistShootKind.Combo => CrouchComboShootStateName,
        MachinistShootKind.Blast => CrouchBlastShootStateName,
        MachinistShootKind.Electric => CrouchElectricShootStateName,
        MachinistShootKind.Combo1 => CrouchCombo1ShootStateName,
        MachinistShootKind.Combo2 => CrouchCombo2ShootStateName,
        _ => CrouchShootStateName,
    };

    static string ResolveUpperHorizontalShootState(MachinistShootKind kind) => kind switch
    {
        MachinistShootKind.Combo => ComboShootStateName,
        MachinistShootKind.Combo1 => Combo1ShootStateName,
        MachinistShootKind.Combo2 => Combo2ShootStateName,
        _ => ShootStateName,
    };

    bool PlayForcedCrouchComboShoot()
    {
        if (crouchAnimator == null)
            return false;

        if (forcedAirComboActive)
            ExitForcedAirComboDisplay(clearHang: false);

        bool alreadyCrouching = isCrouching;

        if (!alreadyCrouching)
            EnterForcedCrouchComboDisplay(CrouchComboShootStateName);

        forcedCrouchComboActive = true;
        forcedCrouchComboWasAlreadyCrouching = alreadyCrouching;

        upperShootUsesAnimatorParam = false;
        isShooting = true;
        activeShootStateName = CrouchComboShootStateName;
        activeShootAnimator = crouchAnimator;
        comboShootPinnedNormalized = 0f;
        comboShootInputInterrupted = false;
        pendingLookUpReleaseAfterCombo = false;
        pendingLookDownReleaseAfterCombo = false;

        crouchAnimator.Play(CrouchComboShootStateName, 0, 0f);
        return true;
    }

    bool PlayForcedCrouchMachineShoot()
    {
        if (crouchAnimator == null)
            return false;

        if (forcedAirComboActive)
            ExitForcedAirComboDisplay(clearHang: false);

        bool alreadyCrouching = isCrouching;
        bool wasMachine = IsPlayingMachineShoot;

        if (!alreadyCrouching)
            EnterForcedCrouchComboDisplay(CrouchMachineShootStateName);
        else
            activeFullBodyState = CrouchMachineShootStateName;

        forcedCrouchComboActive = true;
        // Machine 打断 Machine：保留进入前的蹲姿标记；其它情况按当前是否已蹲记录
        if (!wasMachine)
            forcedCrouchComboWasAlreadyCrouching = alreadyCrouching;

        upperShootUsesAnimatorParam = false;
        isShooting = true;
        activeShootStateName = CrouchMachineShootStateName;
        activeShootAnimator = crouchAnimator;

        crouchAnimator.Play(CrouchMachineShootStateName, 0, 0f);
        return true;
    }

    bool PlayForcedAirMachineShoot()
    {
        if (crouchAnimator == null)
            return false;

        if (forcedCrouchComboActive)
            ExitForcedCrouchComboDisplay();

        EnterForcedAirComboDisplay(AirMachineShootStateName);

        forcedAirComboActive = true;
        upperShootUsesAnimatorParam = false;
        isShooting = true;
        activeShootStateName = AirMachineShootStateName;
        activeShootAnimator = crouchAnimator;
        pendingLookUpReleaseAfterCombo = false;
        pendingLookDownReleaseAfterCombo = false;

        crouchAnimator.Play(AirMachineShootStateName, 0, 0f);
        return true;
    }

    bool PlayForcedAirComboShoot()
    {
        if (crouchAnimator == null)
            return false;

        if (forcedCrouchComboActive)
            ExitForcedCrouchComboDisplay();

        EnterForcedAirComboDisplay(AirComboShootStateName);

        forcedAirComboActive = true;
        upperShootUsesAnimatorParam = false;
        isShooting = true;
        activeShootStateName = AirComboShootStateName;
        activeShootAnimator = crouchAnimator;
        comboShootPinnedNormalized = 0f;
        comboShootInputInterrupted = false;
        pendingLookUpReleaseAfterCombo = false;
        pendingLookDownReleaseAfterCombo = false;

        crouchAnimator.Play(AirComboShootStateName, 0, 0f);
        return true;
    }

    bool PlayForcedCrouchBlastShoot()
    {
        if (crouchAnimator == null)
            return false;

        if (forcedAirComboActive)
            ExitForcedAirComboDisplay(clearHang: false);

        bool alreadyCrouching = isCrouching;

        if (!alreadyCrouching)
            EnterForcedCrouchComboDisplay(CrouchBlastShootStateName);

        forcedCrouchComboActive = true;
        forcedCrouchComboWasAlreadyCrouching = alreadyCrouching;

        upperShootUsesAnimatorParam = false;
        isShooting = true;
        activeShootStateName = CrouchBlastShootStateName;
        activeShootAnimator = crouchAnimator;
        comboShootPinnedNormalized = 0f;
        comboShootInputInterrupted = false;
        pendingLookUpReleaseAfterCombo = false;
        pendingLookDownReleaseAfterCombo = false;

        crouchAnimator.Play(CrouchBlastShootStateName, 0, 0f);
        return true;
    }

    bool PlayForcedAirBlastShoot()
    {
        if (crouchAnimator == null)
            return false;

        if (forcedCrouchComboActive)
            ExitForcedCrouchComboDisplay();

        EnterForcedAirComboDisplay(AirBlastShootStateName);

        forcedAirComboActive = true;
        upperShootUsesAnimatorParam = false;
        isShooting = true;
        activeShootStateName = AirBlastShootStateName;
        activeShootAnimator = crouchAnimator;
        comboShootPinnedNormalized = 0f;
        comboShootInputInterrupted = false;
        pendingLookUpReleaseAfterCombo = false;
        pendingLookDownReleaseAfterCombo = false;

        crouchAnimator.Play(AirBlastShootStateName, 0, 0f);
        return true;
    }

    bool PlayForcedCrouchElectricShoot()
    {
        if (crouchAnimator == null)
            return false;

        if (forcedAirComboActive)
            ExitForcedAirComboDisplay(clearHang: false);

        bool alreadyCrouching = isCrouching;

        if (!alreadyCrouching)
            EnterForcedCrouchComboDisplay(CrouchElectricShootStateName);

        forcedCrouchComboActive = true;
        forcedCrouchComboWasAlreadyCrouching = alreadyCrouching;

        upperShootUsesAnimatorParam = false;
        isShooting = true;
        activeShootStateName = CrouchElectricShootStateName;
        activeShootAnimator = crouchAnimator;
        comboShootPinnedNormalized = 0f;
        comboShootInputInterrupted = false;
        pendingLookUpReleaseAfterCombo = false;
        pendingLookDownReleaseAfterCombo = false;

        crouchAnimator.Play(CrouchElectricShootStateName, 0, 0f);
        return true;
    }

    bool PlayForcedAirElectricShoot()
    {
        if (crouchAnimator == null)
            return false;

        if (forcedCrouchComboActive)
            ExitForcedCrouchComboDisplay();

        EnterForcedAirComboDisplay(AirElectricShootStateName);

        forcedAirComboActive = true;
        upperShootUsesAnimatorParam = false;
        isShooting = true;
        activeShootStateName = AirElectricShootStateName;
        activeShootAnimator = crouchAnimator;
        comboShootPinnedNormalized = 0f;
        comboShootInputInterrupted = false;
        pendingLookUpReleaseAfterCombo = false;
        pendingLookDownReleaseAfterCombo = false;

        crouchAnimator.Play(AirElectricShootStateName, 0, 0f);
        return true;
    }

    void EnterForcedCrouchComboDisplay(string fullBodyStateName)
    {
        InterruptLand();
        ClearLookState();

        isCrouching = true;
        displayMode = BodyDisplayMode.FullBody;
        activeFullBodyState = fullBodyStateName;
        fullBodyAutoExit = false;
        ResetFullBodyParams();

        if (upBody != null)
            upBody.SetActive(false);
        if (downBody != null)
            downBody.SetActive(false);
        if (crouchBody != null)
            crouchBody.SetActive(true);
    }

    void EnterForcedAirComboDisplay(string fullBodyStateName)
    {
        InterruptLand();
        ClearLookState();

        // 不设 isCrouching，避免空中缩碰撞体
        displayMode = BodyDisplayMode.FullBody;
        activeFullBodyState = fullBodyStateName;
        fullBodyAutoExit = false;
        ResetFullBodyParams();

        if (upBody != null)
            upBody.SetActive(false);
        if (downBody != null)
            downBody.SetActive(false);
        if (crouchBody != null)
            crouchBody.SetActive(true);
    }

    void ExitForcedCrouchComboDisplay()
    {
        bool stayCrouching = forcedCrouchComboWasAlreadyCrouching;
        forcedCrouchComboActive = false;
        forcedCrouchComboWasAlreadyCrouching = false;

        if (stayCrouching)
        {
            if (crouchAnimator == null)
                return;

            activeFullBodyState = CrouchStateName;
            fullBodyAutoExit = false;
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

        isCrouching = false;
        ResetFullBodyParams();
        SetSplitDisplay();
        InvalidateUpperLocomotionCache();

        bool grounded = physicsCheck != null && physicsCheck.isGround;
        if (!grounded)
        {
            float velocityY = rb != null ? rb.linearVelocity.y : 0f;
            RestoreAirPhaseAfterForcedCrouch(velocityY);
            SyncSplitAnimators();
            return;
        }

        airPhase = AirPhaseType.Ground;
        airTrack = AirTrack.None;
        RestoreUpperLocomotion();
    }

    void ExitForcedAirComboDisplay(bool clearHang = true)
    {
        forcedAirComboActive = false;
        ResetFullBodyParams();
        SetSplitDisplay();
        InvalidateUpperLocomotionCache();

        if (clearHang && playerMovement != null)
            playerMovement.ClearAirHang();

        bool grounded = physicsCheck != null && physicsCheck.isGround;
        if (!grounded)
        {
            float velocityY = rb != null ? rb.linearVelocity.y : 0f;
            RestoreAirPhaseAfterForcedCrouch(velocityY);
            SyncSplitAnimators();
            return;
        }

        airPhase = AirPhaseType.Ground;
        airTrack = AirTrack.None;
        RestoreUpperLocomotion();
    }

    void RestoreAirPhaseAfterForcedCrouch(float velocityY)
    {
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
    }

    public override void InterruptMachinistComboShootFromInput()
    {
        if (!IsPlayingMachinistComboShoot)
            return;

        comboShootInputInterrupted = true;
        CompleteShoot();
    }

    public override void CancelMachinistShootAnim()
    {
        if (!isShooting)
            return;

        if (IsPlayingMachinistComboShoot)
            comboShootInputInterrupted = true;

        CompleteShoot();
    }

    /// <summary>
    /// 上半身装弹动画（蹲姿时走全身层同名状态）。播完前不可被射击等打断，逻辑同连击 pin。
    /// </summary>
    public override bool TryPlayLoadBulletAnim() => TryPlayLoadAnim(LoadBulletStateName);

    public override bool TryPlayMeleeLoadAnim() => TryPlayLoadAnim(MeleeLoadStateName);

    bool TryPlayLoadAnim(string stateName)
    {
        if (isSwitchingWeapon || isRecalling || isDispatching || isDead)
            return false;

        if (IsPlayingMachinistComboShoot || IsPlayingMachineShoot || IsPlayingLoadBullet)
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

        bool meleeLoad = stateName == MeleeLoadStateName;
        if (meleeLoad && isCrouching)
            PlayStandAnim();

        Animator animator;
        if (isCrouching && !meleeLoad)
        {
            if (crouchAnimator == null)
                return false;
            upperShootUsesAnimatorParam = false;
            animator = crouchAnimator;
        }
        else
        {
            if (upperAnimator == null)
                return false;
            upperShootUsesAnimatorParam = false;
            ClearLookStateForHorizontalShoot();
            BlockUpperAirPhaseForHorizontalShoot();
            animator = upperAnimator;
        }

        isShooting = true;
        activeShootStateName = stateName;
        activeShootAnimator = animator;
        loadBulletPinnedNormalized = 0f;

        animator.Play(stateName, 0, 0f);
        if (animator == upperAnimator)
            ResetUpperShootTrigger();

        return true;
    }

    public override void SetMachinistMeleeStance(bool active)
    {
        if (machinistMeleeStance == active)
            return;

        machinistMeleeStance = active;
        if (active && IsUpperLookActive())
            StopLook();

        InvalidateUpperLocomotionCache();
        if (!isShooting && !isMelee && !isCharging && !isDispatching && !isThrowing
            && !isSwitchingWeapon && !isRecalling
            && displayMode == BodyDisplayMode.Split)
            SyncUpperLocomotionViaPlay();
    }

    public override bool TryPlayMachinistMeleeAttackAnim(int step)
        => TryPlayMachinistMeleeAttackAnim(step, null, null);

    public override bool TryPlayMachinistMeleeAttackAnim(int step, string upperState, string lowerState)
    {
        if (isSwitchingWeapon || isRecalling || isRolling || isDispatching || isDead)
            return false;

        if (IsPlayingLoadBullet || IsPlayingMachinistComboShoot || IsPlayingMachineShoot)
            return false;

        if (isMelee)
            return false;

        if (isShooting)
            CompleteShoot();
        if (isThrowing)
            CompleteThrow();
        if (isCharging)
            CancelMachinistCharge();

        if (isCrouching)
            PlayStandAnim();

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

        if (upperAnimator == null)
            return false;

        if (IsUpperLookActive())
            StopLook();

        if (string.IsNullOrEmpty(upperState))
            upperState = ResolveMachinistMeleeAttackState(
                machinistMeleeAttackUpStates, step, step >= 2 ? "M_Melee_Attack3_up" : "M_Melee_Attack2_up");
        if (string.IsNullOrEmpty(upperState))
            return false;

        bool groundedMelee = airPhase == AirPhaseType.Ground;

        isMelee = true;
        isMachinistMeleeAttacking = true;
        CurrentMachinistMeleeStep = step;
        isMachinistAirMelee = !groundedMelee;
        activeMeleeStateName = upperState;
        activeMeleeAnimator = upperAnimator;
        lowerMeleePlayed = false;
        activeLowerMeleeStateName = null;

        upperAnimator.Play(upperState, 0, 0f);
        BlockUpperAirPhaseForHorizontalShoot();

        if (groundedMelee && lowerAnimator != null)
        {
            if (string.IsNullOrEmpty(lowerState))
                lowerState = ResolveMachinistMeleeAttackState(
                    machinistMeleeAttackDownStates, step, step >= 2 ? "M_Melee_Attack3_down" : "M_Melee_Attack2_down");

            if (!string.IsNullOrEmpty(lowerState))
            {
                // 先挡 AnyState Ground→Idle，再 Play；否则同帧会被拉回 Idle
                BlockLowerAirPhaseForMelee();
                lowerAnimator.Play(lowerState, 0, 0f);
                lowerMeleePlayed = true;
                activeLowerMeleeStateName = lowerState;
            }
        }

        return true;
    }

    public void CancelMachinistMeleeAttack()
    {
        if (isMachinistMeleeAttacking)
            CompleteMelee();
    }

    static string ResolveMachinistMeleeAttackState(string[] states, int step, string fallback)
    {
        if (states != null && step >= 0 && step < states.Length && !string.IsNullOrEmpty(states[step]))
            return states[step];
        return fallback;
    }

    static bool IsLoadBulletState(string stateName) =>
        stateName == LoadBulletStateName || stateName == MeleeLoadStateName;

    static bool IsMachinistComboShootState(string stateName) =>
        stateName == ComboShootStateName
        || stateName == LookUpComboShootStateName
        || stateName == LookDownComboShootStateName
        || stateName == CrouchComboShootStateName
        || stateName == AirComboShootStateName
        || stateName == LookUpBlastShootStateName
        || stateName == LookDownBlastShootStateName
        || stateName == CrouchBlastShootStateName
        || stateName == AirBlastShootStateName
        || stateName == LookUpElectricShootStateName
        || stateName == LookDownElectricShootStateName
        || stateName == CrouchElectricShootStateName
        || stateName == AirElectricShootStateName;

    static bool IsMachinistChargeShootState(string stateName) =>
        stateName == ChargeShootStateName
        || stateName == LookUpChargeShootStateName
        || stateName == LookDownChargeShootStateName;

    static bool IsMachineShootState(string stateName) =>
        stateName == CrouchMachineShootStateName
        || stateName == AirMachineShootStateName
        || stateName == LookUpMachineShootStateName
        || stateName == LookDownMachineShootStateName;

    static bool IsLookMachinistComboShootState(string stateName) =>
        stateName == LookUpComboShootStateName
        || stateName == LookDownComboShootStateName
        || stateName == LookUpBlastShootStateName
        || stateName == LookDownBlastShootStateName
        || stateName == LookUpElectricShootStateName
        || stateName == LookDownElectricShootStateName
        || stateName == LookUpMachineShootStateName
        || stateName == LookDownMachineShootStateName;

    static bool IsUpperBodyLookShootState(string stateName) =>
        stateName == LookUpShootStateName
        || stateName == LookDownShootStateName
        || stateName == LookUpComboShootStateName
        || stateName == LookDownComboShootStateName
        || stateName == LookUpBlastShootStateName
        || stateName == LookDownBlastShootStateName
        || stateName == LookUpElectricShootStateName
        || stateName == LookDownElectricShootStateName
        || stateName == LookUpMachineShootStateName
        || stateName == LookDownMachineShootStateName;

    static bool IsNaturalShootExitState(AnimatorStateInfo info, string shootStateName)
    {
        if (shootStateName == LookUpShootStateName
            || shootStateName == LookUpComboShootStateName
            || shootStateName == LookUpBlastShootStateName
            || shootStateName == LookUpElectricShootStateName
            || shootStateName == LookUpMachineShootStateName)
        {
            return info.IsName(LookUpStateName)
                || info.IsName(LookUpEndStateName)
                || info.IsName(LookUpStartStateName);
        }

        if (shootStateName == LookDownShootStateName
            || shootStateName == LookDownComboShootStateName
            || shootStateName == LookDownBlastShootStateName
            || shootStateName == LookDownElectricShootStateName
            || shootStateName == LookDownMachineShootStateName)
        {
            return info.IsName(LookDownStateName)
                || info.IsName(LookDownEndStateName)
                || info.IsName(LookDownStartStateName);
        }

        if (shootStateName == ShootStateName
            || shootStateName == ComboShootStateName
            || shootStateName == Combo1ShootStateName
            || shootStateName == Combo2ShootStateName)
        {
            return info.IsName("Idle")
                || info.IsName("Run")
                || info.IsName("Jump")
                || info.IsName("Fall")
                || info.IsName("Leap")
                || info.IsName("LeapAir");
        }

        if (shootStateName == CrouchMachineShootStateName)
            return info.IsName(CrouchStateName);

        return false;
    }

    static bool IsLookDownSpecialShootState(string stateName) =>
        stateName == LookDownBlastShootStateName
        || stateName == LookDownElectricShootStateName
        || stateName == LookDownMachineShootStateName;

    bool IsPlayingLookDownSpecialShoot() =>
        isShooting && IsLookDownSpecialShootState(activeShootStateName);

    bool IsPlayingLookMachinistComboShoot() =>
        isShooting && IsLookMachinistComboShootState(activeShootStateName);

    public override bool BeginMachinistCharge()
    {
        if (isSwitchingWeapon || isRecalling)
            return false;

        if (isDispatching)
            return false;

        if (IsPlayingLoadBullet || IsPlayingMachinistComboShoot || IsPlayingMachineShoot)
            return false;

        if (isMachinistMeleeAttacking)
            return false;

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

        // 清普通 Look，改由蓄力瞄准态接管上半身；下半身保持当前跑/Idle
        if (IsUpperLookActive())
            ClearLookState();

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

    public override void SetChargeAim(MachinistChargeAim aim)
    {
        if (!isCharging)
            return;

        if (aim == activeChargeAim)
            return;

        ApplyChargeAim(aim, playStart: true);
    }

    public override void SyncChargeAimFromInput(bool wantLookUp, bool wantLookDown, bool wantCrouch)
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

    public override bool ReleaseMachinistCharge()
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

    public override void CancelCharge() => CancelMachinistCharge();

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

    public override bool BeginDispatch()
    {
        if (isDead || isSwitchingWeapon || isRecalling)
            return false;

        if (IsPlayingLoadBullet || IsPlayingMachinistComboShoot || IsPlayingMachineShoot)
            return false;

        if (isDispatching)
            return true;

        if (isMelee)
            CompleteMelee();

        if (isThrowing)
            CompleteThrow();

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

        if (IsUpperLookActive())
            ClearLookState();

        dispatchIsCrouch = isCrouching;
        dispatchHoldForLoop = false;
        dispatchAutoEndOnIntroComplete = false;
        dispatchInLoop = false;

        if (dispatchIsCrouch)
        {
            if (crouchAnimator == null)
                return false;

            isRunning = false;
            crouchAnimator.SetBool("IsRun", false);

            isDispatching = true;
            activeDispatchAnimator = crouchAnimator;
            activeDispatchStateName = CrouchDispatchStateName;
            activeFullBodyState = CrouchDispatchStateName;
            fullBodyAutoExit = false;
            crouchAnimator.Play(CrouchDispatchStateName, 0, 0f);
            return true;
        }

        if (upperAnimator == null)
            return false;

        isDispatching = true;
        activeDispatchAnimator = upperAnimator;
        activeDispatchStateName = DispatchStateName;
        BlockUpperAirPhaseForHorizontalShoot();
        upperAnimator.Play(DispatchStateName, 0, 0f);
        return true;
    }

    public override void SetDispatchHold(bool hold)
    {
        if (!isDispatching)
            return;

        dispatchHoldForLoop = hold;
        if (!hold)
            return;

        // 若 intro 已结束仍按住，立刻切 loop
        if (!dispatchInLoop)
            TryEnterDispatchLoop();
    }

    public override void SetDispatchAutoEnd(bool autoEnd)
    {
        if (!isDispatching)
            return;

        dispatchAutoEndOnIntroComplete = autoEnd;
        if (!autoEnd || dispatchInLoop || dispatchHoldForLoop)
            return;

        if (activeDispatchAnimator == null)
        {
            EndDispatch();
            return;
        }

        string introName = dispatchIsCrouch ? CrouchDispatchStateName : DispatchStateName;
        var info = activeDispatchAnimator.GetCurrentAnimatorStateInfo(0);
        if (!info.IsName(introName) || info.normalizedTime >= 1f)
            EndDispatch();
    }

    public override void EndDispatch()
    {
        if (!isDispatching)
            return;

        bool wasCrouch = dispatchIsCrouch;
        isDispatching = false;
        dispatchHoldForLoop = false;
        dispatchAutoEndOnIntroComplete = false;
        dispatchInLoop = false;
        activeDispatchAnimator = null;
        activeDispatchStateName = null;
        dispatchIsCrouch = false;

        if (wasCrouch)
        {
            if (isCrouching && crouchAnimator != null)
            {
                activeFullBodyState = CrouchStateName;
                fullBodyAutoExit = false;
                crouchAnimator.Play(CrouchStateName, 0, 0f);
            }
            return;
        }

        RestoreUpperLocomotion();
    }

    void TryEnterDispatchLoop()
    {
        if (!isDispatching || dispatchInLoop || activeDispatchAnimator == null)
            return;

        string loopName = dispatchIsCrouch ? CrouchDispatchLoopStateName : DispatchLoopStateName;
        dispatchInLoop = true;
        activeDispatchStateName = loopName;
        if (dispatchIsCrouch)
            activeFullBodyState = loopName;
        activeDispatchAnimator.Play(loopName, 0, 0f);
    }

    void MaintainDispatchCompletion()
    {
        if (!isDispatching || activeDispatchAnimator == null || string.IsNullOrEmpty(activeDispatchStateName))
            return;

        if (!activeDispatchAnimator.isActiveAndEnabled)
        {
            EndDispatch();
            return;
        }

        if (dispatchInLoop)
            return;

        var info = activeDispatchAnimator.GetCurrentAnimatorStateInfo(0);
        string introName = dispatchIsCrouch ? CrouchDispatchStateName : DispatchStateName;
        if (!info.IsName(introName))
            return;

        if (info.length > 0.001f && info.normalizedTime < 1f)
            return;

        if (dispatchHoldForLoop)
            TryEnterDispatchLoop();
        else if (dispatchAutoEndOnIntroComplete)
            EndDispatch();
        // Pressing 阶段 intro 已结束：定格末帧，等短按松开 AutoEnd 或长按 Hold→loop
    }

    public override bool TryPlayThrowAnim() // 投掷中再次按 U 会从头重播；可打断转身/着陆/蹲伏起步
    {
        if (isSwitchingWeapon || isRecalling || isRolling)
            return false;

        if (isDispatching)
            return false;

        if (IsPlayingLoadBullet || IsPlayingMachinistComboShoot || IsPlayingMachineShoot)
            return false;

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

    public override bool TryPlayMeleeAnim() // 近战可打断射击/投掷；站立/空中/蹲伏对应不同动画
    {
        if (isSwitchingWeapon || isRecalling || isRolling)
            return false;

        if (isDispatching)
            return false;

        if (IsPlayingLoadBullet || IsPlayingMachinistComboShoot || IsPlayingMachineShoot)
            return false;

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
        // 与射击/上看相同：挡住 AnyState Ground→Idle，避免出刀被 0.25s 混合掐掉
        if (animator == upperAnimator)
            BlockUpperAirPhaseForHorizontalShoot();
        return true;
    }

    public override void ApplyWeaponDefinition(WeaponDefinition def)
    {
        if (def == null)
            return;

        EnsureOverrideControllers();
        ApplyOverridesToController(upperOverrideController, def);
        ApplyOverridesToController(crouchOverrideController, def);

        if (upperAnimator != null && HasAnimatorParam(upperAnimator, WeaponIdParam))
            upperAnimator.SetInteger(WeaponIdParam, def.weaponId);
        if (crouchAnimator != null && HasAnimatorParam(crouchAnimator, WeaponIdParam))
            crouchAnimator.SetInteger(WeaponIdParam, def.weaponId);

        InvalidateUpperLocomotionCache();
    }

    public override bool TryPlayWeaponSwitchAnim(WeaponDefinition def) // 先全量换姿，再播切枪；可打断射击/投掷/近战/转身/着陆
    {
        if (def == null || isDead)
            return false;

        // 无法打断的动画中：只换姿，不播切枪动画
        if (isRolling || isDispatching || IsPlayingLoadBullet || IsPlayingMachinistComboShoot)
        {
            ApplyWeaponDefinition(def);
            return true;
        }

        if (isRecalling)
            CompleteRecall();

        if (isSwitchingWeapon)
            CompleteWeaponSwitch();

        if (isShooting)
            CompleteShoot();
        if (isThrowing)
            CompleteThrow();
        if (isMelee)
            CompleteMelee();
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

        ApplyWeaponDefinition(def);

        string stateName;
        Animator animator;

        if (isCrouching)
        {
            stateName = CrouchWeaponSwitchStateName;
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

            stateName = WeaponSwitchStateName;
        }

        if (animator == null)
            return false;

        isSwitchingWeapon = true;
        activeWeaponSwitchStateName = stateName;
        activeWeaponSwitchAnimator = animator;
        animator.Play(stateName, 0, 0f);
        return true;
    }

    public override bool TryPlayWeaponSwitchAnim(WeaponDefinition fromDef, WeaponDefinition toDef)
        => TryPlayWeaponSwitchAnim(toDef);

    public override bool TryPlayRecallAnim()
    {
        if (isDead || isCrouching)
            return false;

        if (isRolling || isDispatching || IsPlayingLoadBullet || IsPlayingMachinistComboShoot)
            return false;

        if (isRecalling)
            CompleteRecall();

        if (isSwitchingWeapon)
            CompleteWeaponSwitch();

        if (isShooting)
            CompleteShoot();
        if (isThrowing)
            CompleteThrow();
        if (isMelee)
            CompleteMelee();
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

        if (upperAnimator == null)
            return false;

        if (IsUpperLookActive())
            StopLook();

        isRecalling = true;
        activeRecallStateName = RecallStateName;
        activeRecallAnimator = upperAnimator;
        upperAnimator.Play(RecallStateName, 0, 0f);
        return true;
    }

    public override bool TryGetMeleeAnimProgress(out float normalizedTime)
        => TryGetMeleeAnimProgress(out normalizedTime, out _);

    public override bool TryGetMeleeAnimProgress(out float normalizedTime, out float length)
    {
        normalizedTime = 0f;
        length = 0f;
        if (!isMelee || activeMeleeAnimator == null || string.IsNullOrEmpty(activeMeleeStateName))
            return false;

        var info = activeMeleeAnimator.GetCurrentAnimatorStateInfo(0);
        if (info.IsName(activeMeleeStateName))
        {
            normalizedTime = info.normalizedTime;
            length = info.length;
            return true;
        }

        if (activeMeleeAnimator.IsInTransition(0))
        {
            var next = activeMeleeAnimator.GetNextAnimatorStateInfo(0);
            if (next.IsName(activeMeleeStateName))
            {
                normalizedTime = next.normalizedTime;
                length = next.length;
                return true;
            }
        }

        return false;
    }

    public override void PlayDieAnim()
    {
        isDead = true;
        isRolling = false;
        ResetRollRotation();
        isCrouching = false;
        isRunning = false;
        isShooting = false;
        isCharging = false;
        isDispatching = false;
        dispatchHoldForLoop = false;
        dispatchAutoEndOnIntroComplete = false;
        dispatchInLoop = false;
        dispatchIsCrouch = false;
        activeDispatchAnimator = null;
        activeDispatchStateName = null;
        isThrowing = false;
        isMelee = false;
        isMachinistMeleeAttacking = false;
        CurrentMachinistMeleeStep = -1;
        isMachinistAirMelee = false;
        lowerMeleePlayed = false;
        activeLowerMeleeStateName = null;
        isSwitchingWeapon = false;
        isRecalling = false;
        activeRecallStateName = null;
        activeRecallAnimator = null;
        ClearLookState();
        ResetFullBodyParams();
        EnterFullBody(DieStateName, autoExitOnComplete: false);
    }

    public override void PlayHurtAnim()
    {
        TrySetHurtTrigger(upperAnimator);
        TrySetHurtTrigger(lowerAnimator);
        TrySetHurtTrigger(crouchAnimator);
    }

    static void TrySetHurtTrigger(Animator animator)
    {
        if (animator == null || !animator.isActiveAndEnabled)
            return;

        foreach (var p in animator.parameters)
        {
            if (p.type == AnimatorControllerParameterType.Trigger && p.name == "hurt")
            {
                animator.SetTrigger("hurt");
                return;
            }
        }
    }

    public override bool TryGetDieAnimProgress(out float normalizedTime)
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

    public override void ResetFromDeath()
    {
        isDead = false;
        isRolling = false;
        ResetRollRotation();
        activeFullBodyState = null;
        fullBodyAutoExit = false;
        ExitFullBody();
        RestoreUpperLocomotion();
    }

    public override void SetLookUp(bool active)
    {
        if (isCharging || machinistMeleeStance || isMachinistMeleeAttacking)
        {
            if (machinistMeleeStance && IsUpperLookActive())
                StopLook();
            return;
        }

        if (active && IsPlayingLand)
            InterruptLand();

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
            EnsureUpperLookLoopWhenNotShooting(
                LookUpStateName, LookUpStartStateName, LookUpShootStateName,
                LookUpComboShootStateName, LookUpBlastShootStateName, LookUpElectricShootStateName,
                LookUpMachineShootStateName);
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
                if (info.IsName(LookUpComboShootStateName)
                    || info.IsName(LookUpBlastShootStateName)
                    || info.IsName(LookUpElectricShootStateName)
                    || info.IsName(LookUpMachineShootStateName)
                    || info.IsName(LookUpShootStateName))
                    upperAnimator.Play(LookUpEndStateName, 0, 0f);
            }
        }
    }

    public override void SetLookDown(bool active)
    {
        if (isCharging || machinistMeleeStance || isMachinistMeleeAttacking)
        {
            if (machinistMeleeStance && IsUpperLookActive())
                StopLook();
            return;
        }

        if (active && IsPlayingLand)
            InterruptLand();

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
            EnsureUpperLookLoopWhenNotShooting(
                LookDownStateName, LookDownStartStateName, LookDownShootStateName,
                LookDownComboShootStateName, LookDownBlastShootStateName, LookDownElectricShootStateName,
                LookDownMachineShootStateName);
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
                if (info.IsName(LookDownComboShootStateName)
                    || info.IsName(LookDownBlastShootStateName)
                    || info.IsName(LookDownElectricShootStateName)
                    || info.IsName(LookDownMachineShootStateName)
                    || info.IsName(LookDownShootStateName))
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
        bool clearHang = IsLookDownSpecialShootState(activeShootStateName);

        isShooting = false;
        activeShootStateName = null;
        activeShootAnimator = null;
        upperShootUsesAnimatorParam = false;
        comboShootPinnedNormalized = 0f;
        comboShootInputInterrupted = false;

        ResetUpperShootTrigger();
        if (clearHang)
            playerMovement?.ClearAirHang();
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
        string lookComboShootState,
        string lookBlastShootState = null,
        string lookElectricShootState = null,
        string lookMachineShootState = null)
    {
        if (isShooting || upperAnimator == null)
            return;

        ResetUpperShootTrigger();
        var info = upperAnimator.GetCurrentAnimatorStateInfo(0);
        if (info.IsName(lookLoopState) || info.IsName(lookStartState))
            return;

        // 未在射击却停在 Look*Shoot：强制回 Look 循环，避免落地后继续播完射击片段
        if (info.IsName(lookShootState)
            || info.IsName(lookComboShootState)
            || (!string.IsNullOrEmpty(lookBlastShootState) && info.IsName(lookBlastShootState))
            || (!string.IsNullOrEmpty(lookElectricShootState) && info.IsName(lookElectricShootState))
            || (!string.IsNullOrEmpty(lookMachineShootState) && info.IsName(lookMachineShootState)))
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

        if (isSwitchingWeapon)
            CompleteWeaponSwitch();

        if (isRecalling)
            CompleteRecall();

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

    public override void EnterFullBody(string stateName, bool autoExitOnComplete) // 切全身层并从头播放指定状态
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

    public override void ExitFullBody() // 恢复 Split 层并同步参数
    {
        displayMode = BodyDisplayMode.Split;
        activeFullBodyState = null;
        fullBodyAutoExit = false;
        isRolling = false;
        ResetRollRotation();

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

        bool clearHang = IsLookDownSpecialShootState(activeShootStateName);

        isShooting = false;
        activeShootStateName = null;
        activeShootAnimator = null;
        upperShootUsesAnimatorParam = false;
        comboShootPinnedNormalized = 0f;
        comboShootInputInterrupted = false;
        loadBulletPinnedNormalized = 0f;
        pendingLookUpReleaseAfterCombo = false;
        pendingLookDownReleaseAfterCombo = false;

        if (clearHang)
            playerMovement?.ClearAirHang();

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

    public override void OnFullBodyAnimationFinished() // Animation Event：全身动作结束，触发 autoExit
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

    public override void OnLandAnimationFinished() => OnFullBodyAnimationFinished(); // 兼容旧事件名

    void EnterFullBodyLand() // 空中落地或地面急停播 Land，结束后回地面 Split
    {
        // 强制蹲姿/空中连击/MachineShoot 播完前不被落地打断
        if ((forcedCrouchComboActive || forcedAirComboActive)
            && (IsPlayingMachinistComboShoot || IsPlayingMachineShoot))
            return;

        // 连击终结 / 蓄力中 / 蓄力射击 / 近战出刀 / 装弹：落地不打断，只同步空中相位
        if (IsPlayingMachinistComboShoot
            || isCharging
            || isMachinistMeleeAttacking
            || IsPlayingLoadBullet
            || (isShooting && IsMachinistChargeShootState(activeShootStateName)))
        {
            airPhase = AirPhaseType.Ground;
            airTrack = AirTrack.None;
            isMachinistAirMelee = false;
            return;
        }

        EnterFullBody(GetLandStateName(), autoExitOnComplete: true);
    }

    string GetLandStateName() =>
        machinistMeleeStance ? MeleeLandFullStateName : LandStateName;

    public override bool InterruptLand() // 下半身有输入时立刻退出 Land，返回是否打断了 Land
    {
        if (!IsPlayingLand)
            return false;

        CompleteAutoFullBodyExit();
        return true;
    }

    public override bool InterruptTurn() // 起跳/移动打断转身
    {
        if (!IsTurning)
            return false;

        if (activeFullBodyState == CrouchTurnStateName)
            CompleteCrouchTurnExit();
        else
            CompleteAutoFullBodyExit();

        return true;
    }

    public override bool TryPlayRollAnim()
    {
        if (isDead || isRolling)
            return false;

        if (isDispatching || isSwitchingWeapon || isRecalling)
            return false;

        if (isMelee)
            CompleteMelee();
        if (isThrowing)
            CompleteThrow();
        if (isShooting)
            CompleteShoot();
        if (isCharging)
            CancelMachinistCharge();

        if (IsPlayingLand)
            InterruptLand();
        else if (IsTurning)
            InterruptTurn();

        if (isCrouching)
            PlayStandAnim();

        ClearLookState();
        EnterFullBody(RollStateName, autoExitOnComplete: false);
        if (crouchAnimator != null)
            crouchAnimator.Update(0f); // 立刻切到 roll 精灵，再按真实尺寸抬升旋转轴
        BeginRollPose();
        isRolling = true;
        return true;
    }

    public override void EndRollAnim()
    {
        if (!isRolling && activeFullBodyState != RollStateName)
            return;

        isRolling = false;
        ResetRollRotation();

        if (displayMode == BodyDisplayMode.FullBody && activeFullBodyState == RollStateName)
            ExitFullBody();
    }

    public override void SetRollRotation(float degreesZ)
    {
        if (crouchBody == null)
            return;

        crouchBody.transform.localRotation = Quaternion.Euler(0f, 0f, degreesZ);
    }

    public override void ResetRollRotation()
    {
        if (crouchBody == null)
        {
            rollPoseActive = false;
            return;
        }

        crouchBody.transform.localRotation = Quaternion.identity;
        if (rollPoseActive)
        {
            crouchBody.transform.localPosition = fullBodyRestLocalPos;
            rollPoseActive = false;
        }
    }

    /// <summary>
    /// roll 切片 pivot 在中心，直接绕 FullBody 原点转会有一半扎进地。
    /// 开始翻滚时把旋转中心抬到约半身高度，使翻滚圆贴地。
    /// </summary>
    void BeginRollPose()
    {
        if (crouchBody == null)
            return;

        if (!rollPoseActive)
        {
            fullBodyRestLocalPos = crouchBody.transform.localPosition;
            rollPoseActive = true;
        }

        float lift = EstimateRollPivotLift();
        Vector3 pos = fullBodyRestLocalPos;
        pos.y += lift;
        crouchBody.transform.localPosition = pos;
        crouchBody.transform.localRotation = Quaternion.identity;
    }

    float EstimateRollPivotLift()
    {
        var sr = crouchBody != null ? crouchBody.GetComponent<SpriteRenderer>() : null;
        if (sr == null || sr.sprite == null)
            return 0.6f;

        // sprite.bounds 为本地尺寸；再乘 FullBody 本地缩放
        Vector3 extents = sr.sprite.bounds.extents;
        float scaleY = Mathf.Abs(crouchBody.transform.localScale.y);
        float scaleX = Mathf.Abs(crouchBody.transform.localScale.x);
        // 用外接圆半径，避免 45° 时边角穿地
        float radius = Mathf.Sqrt(
            extents.x * extents.x * scaleX * scaleX +
            extents.y * extents.y * scaleY * scaleY);
        return radius;
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
        else if (isDispatching)
            crouchAnimator.Play(
                dispatchInLoop ? CrouchDispatchLoopStateName : CrouchDispatchStateName, 0, 0f);
        else if (isThrowing)
            crouchAnimator.Play(CrouchThrowStateName, 0, 0f);
        else if (isMelee)
            crouchAnimator.Play(CrouchMeleeStateName, 0, 0f);
        else if (isSwitchingWeapon)
            crouchAnimator.Play(CrouchWeaponSwitchStateName, 0, 0f);
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
        if (IsLandFullBodyState)
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
                if (ShouldLandFromAir(grounded, velocityY))
                {
                    EnterFullBodyLand();
                    break;
                }
                if (velocityY <= descendVelocityThreshold)
                    airPhase = AirPhaseType.Fall;
                break;

            case AirPhaseType.Leap:
                if (ShouldLandFromAir(grounded, velocityY))
                {
                    EnterFullBodyLand();
                    break;
                }
                if (velocityY <= descendVelocityThreshold)
                    airPhase = AirPhaseType.LeapAir;
                break;

            case AirPhaseType.Fall:
            case AirPhaseType.LeapAir:
                if (IsSolidlyGrounded(grounded))
                    EnterFullBodyLand();
                break;
        }
    }

    bool IsSolidlyGrounded(bool grounded) =>
        physicsCheck != null ? physicsCheck.isSolidGround : grounded;

    bool ShouldLandFromAir(bool grounded, float velocityY)
    {
        if (!IsSolidlyGrounded(grounded))
            return false;
        if (velocityY <= descendVelocityThreshold)
            return true;
        return playerMovement != null && playerMovement.CanLandOnSlopeWhileAscending;
    }

    void SyncSplitAnimators() // 下半身始终同步；Look 期间上半身由代码独占
    {
        if (lowerAnimator == null)
            return;

        if (isMelee && lowerMeleePlayed)
        {
            BlockLowerAirPhaseForMelee();
            PinLowerMeleeState();
        }
        else
        {
            int phase = (int)airPhase;
            lowerAnimator.SetInteger("AirPhase", phase);
            lowerAnimator.SetBool("IsRun", isRunning);
        }

        if (IsUpperLookActive())
        {
            SyncUpperLookParams();
            MaintainUpperLookEndCompletion();
            return;
        }

        if (isShooting || isCharging || isDispatching || isThrowing || isMelee || isSwitchingWeapon || isRecalling)
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
        // 近战空中写 5：upM AnyState 会把 AirPhase 1–4 抢回普通 Jump/Fall/Leap
        bool blockAirPhase = machinistMeleeStance && airPhase != AirPhaseType.Ground;
        int syncedPhase = blockAirPhase ? UpperLookAirPhaseBlock : phase;
        string stateName = GetUpperLocomotionStateName();
        bool phaseChanged = syncedPhase != lastUpperSyncedPhase;
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
            lastUpperSyncedPhase = syncedPhase;
            upperAnimator.SetInteger("AirPhase", syncedPhase);
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

    /// <summary>
    /// downM 有 AnyState：AirPhase==Ground 且 !IsRun → Idle。
    /// 出刀期间写成无映射值，避免刚 Play 的 Attack_down 被抢回 Idle。
    /// </summary>
    void BlockLowerAirPhaseForMelee()
    {
        if (lowerAnimator == null)
            return;

        if (lowerAnimator.GetInteger("AirPhase") != UpperLookAirPhaseBlock)
            lowerAnimator.SetInteger("AirPhase", UpperLookAirPhaseBlock);
        if (lowerAnimator.GetBool("IsRun"))
            lowerAnimator.SetBool("IsRun", false);
    }

    void PinLowerMeleeState()
    {
        if (lowerAnimator == null || string.IsNullOrEmpty(activeLowerMeleeStateName))
            return;

        var info = lowerAnimator.GetCurrentAnimatorStateInfo(0);
        if (info.IsName(activeLowerMeleeStateName))
            return;

        if (lowerAnimator.IsInTransition(0)
            && lowerAnimator.GetNextAnimatorStateInfo(0).IsName(activeLowerMeleeStateName))
            return;

        lowerAnimator.Play(activeLowerMeleeStateName, 0, 0f);
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
        {
            if (machinistMeleeStance)
                return isRunning ? MeleeRunUpStateName : MeleeIdleUpStateName;
            return isRunning ? "Run" : "Idle";
        }

        if (machinistMeleeStance)
        {
            return airPhase switch
            {
                AirPhaseType.Jump or AirPhaseType.Leap => MeleeJumpUpStateName,
                AirPhaseType.Fall or AirPhaseType.LeapAir => MeleeFallUpStateName,
                _ => MeleeIdleUpStateName,
            };
        }

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
        isRolling = false;
        ResetRollRotation();

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
                // 空/零长度 clip（无关键帧）无法靠 normalizedTime 推进，立即结束以免锁移动
                if (info.length > 0.001f && info.normalizedTime < 1f)
                    return;

                CompleteShoot();
                return;
            }

            activeShootAnimator.Play(activeShootStateName, 0, comboShootPinnedNormalized);
            return;
        }

        if (IsLoadBulletState(activeShootStateName))
        {
            if (info.IsName(activeShootStateName))
            {
                loadBulletPinnedNormalized = info.normalizedTime;
                if (info.normalizedTime < 1f)
                    return;

                CompleteShoot();
                return;
            }

            activeShootAnimator.Play(activeShootStateName, 0, loadBulletPinnedNormalized);
            return;
        }

        if (!info.IsName(activeShootStateName))
        {
            if (IsNaturalShootExitState(info, activeShootStateName))
            {
                if (sustainShoot)
                {
                    activeShootAnimator.Play(activeShootStateName, 0, 0.999f);
                    return;
                }

                CompleteShoot();
                return;
            }

            activeShootAnimator.Play(activeShootStateName, 0, 0f);
            return;
        }

        if (info.normalizedTime < 1f)
            return;

        if (sustainShoot)
        {
            activeShootAnimator.Play(activeShootStateName, 0, 0.999f);
            return;
        }

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

            if (activeMeleeAnimator.IsInTransition(0)
                && activeMeleeAnimator.GetNextAnimatorStateInfo(0).IsName(activeMeleeStateName))
                return;

            // 刚 Play 的一帧仍可能报旧状态；机械师出刀先钉回，避免第三刀被立刻 Complete
            if (isMachinistMeleeAttacking)
            {
                BlockUpperAirPhaseForHorizontalShoot();
                activeMeleeAnimator.Play(activeMeleeStateName, 0, 0f);
                return;
            }

            CompleteMelee();
            return;
        }

        if (info.normalizedTime < 1f)
            return;

        CompleteMelee();
    }

    void MaintainWeaponSwitchCompletion()
    {
        if (!isSwitchingWeapon || activeWeaponSwitchAnimator == null || string.IsNullOrEmpty(activeWeaponSwitchStateName))
            return;

        var info = activeWeaponSwitchAnimator.GetCurrentAnimatorStateInfo(0);
        if (!info.IsName(activeWeaponSwitchStateName))
        {
            CompleteWeaponSwitch();
            return;
        }

        if (info.normalizedTime < 1f)
            return;

        CompleteWeaponSwitch();
    }

    void CompleteWeaponSwitch()
    {
        isSwitchingWeapon = false;
        activeWeaponSwitchStateName = null;
        activeWeaponSwitchAnimator = null;

        if (isCrouching)
        {
            if (crouchAnimator != null)
                crouchAnimator.Play(CrouchStateName, 0, 0f);
            return;
        }

        InvalidateUpperLocomotionCache();
        if (displayMode == BodyDisplayMode.Split)
            SyncUpperLocomotionViaPlay();
    }

    void MaintainRecallCompletion()
    {
        if (!isRecalling || activeRecallAnimator == null || string.IsNullOrEmpty(activeRecallStateName))
            return;

        var info = activeRecallAnimator.GetCurrentAnimatorStateInfo(0);
        if (!info.IsName(activeRecallStateName))
        {
            CompleteRecall();
            return;
        }

        if (info.normalizedTime < 1f)
            return;

        CompleteRecall();
    }

    void CompleteRecall()
    {
        isRecalling = false;
        activeRecallStateName = null;
        activeRecallAnimator = null;

        if (isCrouching)
        {
            if (crouchAnimator != null)
                crouchAnimator.Play(CrouchStateName, 0, 0f);
            return;
        }

        InvalidateUpperLocomotionCache();
        if (displayMode == BodyDisplayMode.Split)
            SyncUpperLocomotionViaPlay();
    }

    void CompleteMelee()
    {
        bool restoreLower = lowerMeleePlayed;
        isMelee = false;
        isMachinistMeleeAttacking = false;
        CurrentMachinistMeleeStep = -1;
        isMachinistAirMelee = false;
        activeMeleeStateName = null;
        activeMeleeAnimator = null;
        lowerMeleePlayed = false;
        activeLowerMeleeStateName = null;

        if (restoreLower)
            RestoreLowerLocomotionAfterMelee();

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

    void RestoreLowerLocomotionAfterMelee()
    {
        if (lowerAnimator == null)
            return;

        int phase = (int)airPhase;
        lowerAnimator.SetInteger("AirPhase", phase);
        lowerAnimator.SetBool("IsRun", isRunning);

        string stateName;
        if (airPhase == AirPhaseType.Ground)
            stateName = isRunning ? "Run" : "Idle";
        else
            stateName = airPhase switch
            {
                AirPhaseType.Jump => "Jump",
                AirPhaseType.Fall => "Fall",
                AirPhaseType.Leap => "Leap",
                AirPhaseType.LeapAir => "LeapAir",
                _ => "Idle",
            };

        lowerAnimator.Play(stateName, 0, 0f);
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

    public override void SetSustainShoot(bool sustain)
    {
        sustainShoot = sustain;
    }

    public override void SetHeavySpinFiring(bool value)
    {
        isHeavySpinFiring = value;
    }

    void CompleteShoot()
    {
        bool clearLookDownSpecialHang = IsLookDownSpecialShootState(activeShootStateName);

        isShooting = false;
        sustainShoot = false;
        activeShootStateName = null;
        activeShootAnimator = null;
        comboShootPinnedNormalized = 0f;
        comboShootInputInterrupted = false;
        loadBulletPinnedNormalized = 0f;
        ResetUpperShootTrigger();

        if (forcedCrouchComboActive)
        {
            ExitForcedCrouchComboDisplay();
            return;
        }

        if (forcedAirComboActive)
        {
            ExitForcedAirComboDisplay();
            return;
        }

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
            if (clearLookDownSpecialHang)
                playerMovement?.ClearAirHang();
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

    void EnsureOverrideControllers()
    {
        if (upperAnimator != null && upperOverrideController == null)
        {
            var current = upperAnimator.runtimeAnimatorController;
            if (current is AnimatorOverrideController existing)
            {
                upperOverrideController = existing;
                upperBaseController = existing.runtimeAnimatorController;
            }
            else if (current != null)
            {
                upperBaseController = current;
                upperOverrideController = new AnimatorOverrideController(upperBaseController)
                {
                    name = upperBaseController.name + "_WeaponOverride",
                };
                upperAnimator.runtimeAnimatorController = upperOverrideController;
            }
        }

        if (crouchAnimator != null && crouchOverrideController == null)
        {
            var current = crouchAnimator.runtimeAnimatorController;
            if (current is AnimatorOverrideController existing)
            {
                crouchOverrideController = existing;
                crouchBaseController = existing.runtimeAnimatorController;
            }
            else if (current != null)
            {
                crouchBaseController = current;
                crouchOverrideController = new AnimatorOverrideController(crouchBaseController)
                {
                    name = crouchBaseController.name + "_WeaponOverride",
                };
                crouchAnimator.runtimeAnimatorController = crouchOverrideController;
            }
        }
    }

    void ApplyOverridesToController(AnimatorOverrideController overrideController, WeaponDefinition def)
    {
        if (overrideController == null || def == null)
            return;

        var pairs = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<AnimationClip, AnimationClip>>();
        overrideController.GetOverrides(pairs);

        for (int i = 0; i < pairs.Count; i++)
        {
            AnimationClip original = pairs[i].Key;
            if (original == null)
                continue;

            AnimationClip replacement = def.weaponId == 0
                ? original
                : def.GetOverrideForBaseClip(original);

            pairs[i] = new System.Collections.Generic.KeyValuePair<AnimationClip, AnimationClip>(
                original,
                replacement != null ? replacement : original);
        }

        overrideController.ApplyOverrides(pairs);
    }

    static bool HasAnimatorParam(Animator animator, string paramName)
    {
        if (animator == null || string.IsNullOrEmpty(paramName))
            return false;

        foreach (var param in animator.parameters)
        {
            if (param.name == paramName)
                return true;
        }

        return false;
    }
}
