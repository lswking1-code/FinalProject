using UnityEngine;

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
    const string ThrowStateName = "Throw";
    const string AirThrowStateName = "AirThrow";
    const string CrouchThrowStateName = "CrouchThrow";
    const string MeleeStateName = "Melee";
    const string AirMeleeStateName = "AirMelee";
    const string CrouchMeleeStateName = "CrouchMelee";
    const int UpperLookAirPhaseBlock = 5; // 无 AnyState 映射，Look 期间阻止 Ground→Idle 抢状态
    const string IsLookUpParam = "IsLookUp";
    const string IsLookDownParam = "IsLookDown";
    const string ShootTriggerParam = "Shoot";

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
    BodyDisplayMode displayMode = BodyDisplayMode.Split;
    AirPhaseType airPhase = AirPhaseType.Ground;
    AirTrack airTrack = AirTrack.None;

    string activeFullBodyState;
    bool fullBodyAutoExit; // 全身动作播完后是否自动切回 Split

    bool isCrouching;
    bool isRunning;
    bool isShooting;
    bool isThrowing;
    bool isMelee;
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
    string activeThrowStateName;
    Animator activeThrowAnimator;
    string activeMeleeStateName;
    Animator activeMeleeAnimator;

    public bool IsCrouching => isCrouching;
    public bool IsShooting => isShooting;
    public bool IsThrowing => isThrowing;
    public bool IsMelee => isMelee;
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
        if (isCrouching && !grounded)
            ExitCrouchForAir();

        if (isCrouching)
        {
            TryAutoExitCrouchTurn();
            MaintainShootCompletion();
            MaintainThrowCompletion();
            MaintainMeleeCompletion();
            wasGrounded = grounded;
            return;
        }

        if (displayMode == BodyDisplayMode.FullBody)
        {
            TryAutoExitFullBody(); // normalizedTime 兜底退出，配合 Animation Event
            wasGrounded = grounded;
            return;
        }

        AdvanceAirPhase(grounded, velocityY);
        SyncSplitAnimators();
        MaintainShootCompletion();
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

        if (displayMode == BodyDisplayMode.Split && airPhase == AirPhaseType.Ground && !isShooting && !isThrowing && !isMelee)
            SyncSplitAnimators();
    }

    public void PlayRunAnim() // 跑步；蹲姿时只驱动全身层 IsRun
    {
        if (isCrouching && (isShooting || isThrowing || isMelee))
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
                BeginLookShoot(true, false, LookUpShootStateName);
            }
            else if (shootLookDown)
            {
                stateName = LookDownShootStateName;
                upperShootUsesAnimatorParam = true;
                BeginLookShoot(false, true, LookDownShootStateName);
            }
            else
            {
                stateName = ShootStateName;
                upperShootUsesAnimatorParam = false;
                if (IsUpperLookActive())
                    StopLook();
            }
        }

        if (animator == null)
            return false;

        isShooting = true;
        activeShootStateName = stateName;
        activeShootAnimator = animator;

        if (isCrouching)
            animator.Play(stateName, 0, 0f);
        else if (!upperShootUsesAnimatorParam)
            animator.Play(stateName, 0, 0f); // 水平射击：从仰视/俯视射击切回时强制 Play

        return true;
    }

    public bool TryPlayThrowAnim() // 投掷中再次按 U 会从头重播；可打断转身/着陆/蹲伏起步
    {
        if (isMelee)
            CompleteMelee();

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

    public void SetLookUp(bool active)
    {
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

            isLookingUp = true;
            isEndingLookUp = false;
            ApplyUpperLookParams(lookUp: true, lookDown: false);
            TrySwitchHorizontalShootToLookShoot(LookUpShootStateName);
        }
        else if (isLookingUp)
        {
            isLookingUp = false;
            isEndingLookUp = true;
            SetUpperLookBool(IsLookUpParam, false);
        }
    }

    public void SetLookDown(bool active)
    {
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

            isLookingDown = true;
            isEndingLookDown = false;
            ApplyUpperLookParams(lookUp: false, lookDown: true);
            TrySwitchHorizontalShootToLookShoot(LookDownShootStateName);
        }
        else if (isLookingDown)
        {
            isLookingDown = false;
            isEndingLookDown = true;
            SetUpperLookBool(IsLookDownParam, false);
        }
    }

    void BeginLookShoot(bool lookUp, bool lookDown, string shootStateName)
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

        if (ShouldPlayLookShootDirectly(shootStateName))
            upperAnimator.Play(shootStateName, 0, 0f);
        else
            upperAnimator.SetTrigger(ShootTriggerParam);
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
    }

    bool ShouldPlayLookShootDirectly(string shootStateName)
    {
        if (upperAnimator == null)
            return true;

        var info = upperAnimator.GetCurrentAnimatorStateInfo(0);
        if (info.IsName(shootStateName))
            return false;

        if (info.IsName(LookUpStartStateName) || info.IsName(LookDownStartStateName))
            return true;

        if (info.IsName(LookUpStateName) || info.IsName(LookDownStateName))
            return false;

        return true;
    }

    void ExitCrouchForAir()
    {
        isCrouching = false;
        ResetFullBodyParams();
        SetSplitDisplay();
    }

    public void EnterFullBody(string stateName, bool autoExitOnComplete) // 切全身层并从头播放指定状态
    {
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

        InvalidateUpperLocomotionCache();
        SyncSplitAnimators();
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
                if (airStateInitialized && wasGrounded && !grounded && !jumpInvokedThisFrame) // 刚离开地面才进 Fall，避免误判
                {
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

        int phase = (int)airPhase;
        lowerAnimator.SetInteger("AirPhase", phase);
        lowerAnimator.SetBool("IsRun", isRunning);

        if (IsUpperLookActive())
        {
            SyncUpperLookParams();
            MaintainUpperLookEndCompletion();
            return;
        }

        if (isShooting || isThrowing || isMelee)
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

        if (isEndingLookUp && info.IsName(LookUpEndStateName) && info.normalizedTime >= 1f)
            CompleteUpperLookEnd();
        else if (isEndingLookDown && info.IsName(LookDownEndStateName) && info.normalizedTime >= 1f)
            CompleteUpperLookEnd();
    }

    void CompleteUpperLookEnd()
    {
        isEndingLookUp = false;
        isEndingLookDown = false;
        ResetUpperLookParams();
        RestoreUpperLocomotion();
    }

    bool IsUpperLookActive() => isLookingUp || isLookingDown || isEndingLookUp || isEndingLookDown;

    void StopLook()
    {
        isLookingUp = false;
        isLookingDown = false;
        isEndingLookUp = false;
        isEndingLookDown = false;
        ResetUpperLookParams();
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
        ResetUpperLookParams();
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

        var info = activeShootAnimator.GetCurrentAnimatorStateInfo(0);
        if (!info.IsName(activeShootStateName))
        {
            CompleteShoot();
            return;
        }

        if (info.normalizedTime < 1f)
            return;

        CompleteShoot();
    }

    void MaintainThrowCompletion()
    {
        if (!isThrowing || activeThrowAnimator == null || string.IsNullOrEmpty(activeThrowStateName))
            return;

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
