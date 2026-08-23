using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 单 Animator 全身动画（近战角色 / Bob）。Animator 状态名需与 <see cref="PlayerAnim"/> 全身层一致：
/// Idle, Run, Jump, Fall, Leap, LeapAir, Land, Turn, CrouchStart, Crouch, CrouchTurn, CrouchMove,
/// Melee, AirMelee, UpMelee, AirUpMelee, DownMelee, JumpDownAttack, CrouchMelee, Special,
/// rush_ult / whip_ult / buzzsaw_ult, Die, WeaponSwitch。
/// 推荐 AnimatorController：<c>Assets/Animations/melee/melee_full.controller</c>。
/// 切枪：Apply 目标姿后，按 from→to 覆盖 default_switch（三武器六边；空手相关用 to.weaponSwitch）。
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
public class PlayerFullBodyAnim : PlayerAnimBase
{
    const string LandStateName = "Land";
    const string TurnStateName = "Turn";
    const string CrouchTurnStateName = "CrouchTurn";
    const string CrouchStateName = "Crouch";
    const string CrouchStartStateName = "CrouchStart";
    const string MeleeStateName = "Melee";
    const string AirMeleeStateName = "AirMelee";
    const string UpMeleeStateName = "UpMelee";
    const string AirUpMeleeStateName = "AirUpMelee";
    const string DownMeleeStateName = "DownMelee";
    const string JumpDownAttackStateName = "JumpDownAttack";
    const string CrouchMeleeStateName = "CrouchMelee";
    const string SpecialStateName = "Special";
    const string RushUltStateName = "rush_ult";
    const string WhipUltStateName = "whip_ult";
    const string BuzzsawUltStateName = "buzzsaw_ult";
    const string DieStateName = "Die";
    const string WeaponSwitchStateName = "WeaponSwitch";
    const string DefaultSwitchClipName = "default_switch";
    const string DoubleJumpStateName = "default_doublejump";

    /// <summary>rush</summary>
    const int WeaponIdA = 1;
    /// <summary>whip</summary>
    const int WeaponIdB = 2;
    /// <summary>buzzsaw</summary>
    const int WeaponIdC = 3;

    [Header("全身 Animator")]
    public Animator bodyAnimator;

    [Header("空中")]
    [Tooltip("竖直速度低于等于该值时视为开始下落")]
    public float descendVelocityThreshold = 0f;

    [Header("Bob 方向切枪 (1=rush 2=whip 3=buzzsaw)")]
    [Tooltip("未配置时回退目标武器 weaponSwitch")]
    public AnimationClip rushToWhip;
    public AnimationClip rushToBuzz;
    public AnimationClip whipToRush;
    public AnimationClip whipToBuzz;
    public AnimationClip buzzToRush;
    public AnimationClip buzzToWhip;

    enum AirTrack
    {
        None,
        Jump,
        Leap,
    }

    Rigidbody2D rb;
    PhysicsCheck physicsCheck;
    PlayerMovement playerMovement;

    AirPhaseType airPhase = AirPhaseType.Ground;
    AirTrack airTrack = AirTrack.None;

    string activeOneShotState;
    bool oneShotAutoExit;

    bool isCrouching;
    bool isRunning;
    bool isMelee;
    bool isUltimate;
    bool isSwitchingWeapon;
    bool isDead;
    bool isLookingUp;
    bool isLookingDown;
    bool jumpInvokedThisFrame;
    bool wasGrounded;
    bool airStateInitialized;
    /// <summary>二段跳上升段改播 <see cref="DoubleJumpStateName"/>，下落后清掉。</summary>
    bool useDoubleJumpAnim;

    int lastSyncedPhase = -1;
    string lastLocomotionState;
    bool lastSyncedRun;

    string activeMeleeStateName;
    float meleeStartedAt = -1f;
    float oneShotStartedAt = -1f;
    bool? meleeLookUpOverride;
    AnimatorOverrideController bodyOverrideController;
    RuntimeAnimatorController bodyBaseController;
    WeaponDefinition appliedWeaponDef;

    const string WeaponIdParam = "WeaponID";
    const float OneShotEnterGrace = 0.08f;
    const float MeleeMaxDurationFallback = 2f;

    public override bool IsCrouching => isCrouching;
    public override bool IsMelee => isMelee;
    /// <summary>当前是否为站立/空中向上攻击（UpMelee / AirUpMelee）。</summary>
    public bool IsUpwardMelee =>
        isMelee
        && (activeMeleeStateName == UpMeleeStateName || activeMeleeStateName == AirUpMeleeStateName);
    /// <summary>当前是否为空中向下砸地攻击（JumpDownAttack）。</summary>
    public bool IsJumpDownAttack =>
        isMelee && activeMeleeStateName == JumpDownAttackStateName;
    /// <summary>
    /// 落地冲击未结算前为 true：禁止结束 JumpDownAttack，空中片可继续播到末帧。
    /// </summary>
    public bool HoldJumpDownAttackUntilImpact { get; set; }
    /// <summary>兼容旧名：空中下攻即 JumpDownAttack。</summary>
    public bool IsDownwardMelee => IsJumpDownAttack;
    /// <summary>当前是否为蹲伏攻击（CrouchMelee）。</summary>
    public bool IsCrouchMelee =>
        isMelee && activeMeleeStateName == CrouchMeleeStateName;
    /// <summary>当前是否为武器特技（Special）或大招（命中逻辑共用 special profile）。</summary>
    public override bool IsSpecial =>
        isMelee
        && (activeMeleeStateName == SpecialStateName || IsUltimateStateName(activeMeleeStateName));
    /// <summary>当前是否为大招（I / Ability2）：各武器独立 *_ult 状态。</summary>
    public bool IsUltimate =>
        isMelee && isUltimate && IsUltimateStateName(activeMeleeStateName);
    public override bool IsSwitchingWeapon => isSwitchingWeapon;
    public override bool IsDead => isDead;
    public override bool IsLookingUp => isLookingUp;
    public override bool IsLookingDown => isLookingDown;
    public override AirPhaseType CurrentAirPhase => airPhase;
    /// <summary>最近一次 <see cref="ApplyWeaponDefinition"/> 套用的武器；未套用前为 null。</summary>
    public WeaponDefinition AppliedWeaponDefinition => appliedWeaponDef;
    /// <summary>当前姿态武器 ID；未套用定义时视为 0。</summary>
    public int AppliedWeaponId => appliedWeaponDef != null ? appliedWeaponDef.weaponId : 0;
    public override string CurrentFullBodyState =>
        isSwitchingWeapon ? WeaponSwitchStateName : activeOneShotState;
    public override bool IsPlayingLand => activeOneShotState == LandStateName;
    public override bool IsTurning =>
        activeOneShotState == TurnStateName || activeOneShotState == CrouchTurnStateName;
    public override bool IsInFullBody =>
        isCrouching || isSwitchingWeapon || isMelee || !string.IsNullOrEmpty(activeOneShotState);

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        physicsCheck = GetComponent<PhysicsCheck>();
        playerMovement = GetComponent<PlayerMovement>();
        EnsureOverrideController();
    }

    void Start()
    {
        EnsureOverrideController();
        if (physicsCheck != null)
        {
            physicsCheck.Check();
            wasGrounded = physicsCheck.isGround;
            airStateInitialized = true;
        }

        SyncLocomotion();
    }

    void LateUpdate() => jumpInvokedThisFrame = false;

    public override void SetLookUp(bool active) => isLookingUp = active;

    public override void SetLookDown(bool active) => isLookingDown = active;

    public override void UpdateAirState(bool grounded) =>
        UpdateAirState(grounded, rb != null ? rb.linearVelocity.y : 0f);

    public override void UpdateAirState(bool grounded, float velocityY)
    {
        if (isDead)
            return;

        if (isSwitchingWeapon)
        {
            MaintainWeaponSwitchCompletion();
            wasGrounded = grounded;
            return;
        }

        if (isCrouching && !grounded)
            ExitCrouchForAir(velocityY);

        if (isCrouching)
        {
            TryCompleteCrouchStart();
            TryAutoExitCrouchTurn();
            MaintainMeleeCompletion();
            MaintainWeaponSwitchCompletion();
            wasGrounded = grounded;
            return;
        }

        // 近战（含 JumpDownAttack）期间不要播 Land / 切位移，否则会盖掉攻击片并提前 Complete
        if (isMelee)
        {
            if (IsPlayingLand)
                InterruptLand();

            if (IsSolidlyGrounded(grounded) && airPhase != AirPhaseType.Ground)
            {
                airPhase = AirPhaseType.Ground;
                airTrack = AirTrack.None;
            }

            MaintainMeleeCompletion();
            MaintainWeaponSwitchCompletion();
            wasGrounded = grounded;
            airStateInitialized = true;
            return;
        }

        // 转身期间若仍按住水平方向：立刻打断，避免 IsTurning 锁死移动
        // （攻击 FaceToward 后按原方向跑时很容易进 Turn，且 SyncAnimation 在 IsTurning 时不会走 InterruptTurn）
        if (IsTurning && HasHorizontalMoveInput())
        {
            InterruptTurn();
        }

        if (!string.IsNullOrEmpty(activeOneShotState))
        {
            TryAutoExitOneShot();
            MaintainMeleeCompletion();
            MaintainWeaponSwitchCompletion();
            wasGrounded = grounded;
            return;
        }

        AdvanceAirPhase(grounded, velocityY);
        SyncLocomotion();
        MaintainMeleeCompletion();
        MaintainWeaponSwitchCompletion();
        wasGrounded = grounded;
        airStateInitialized = true;
    }

    public override void PlayJumpAnim(bool hasHorizontalInput)
    {
        BeginAirJump(hasHorizontalInput, doubleJump: false);
    }

    /// <summary>
    /// Bob 二段跳：空中再跳时播 Animator 状态 <c>default_doublejump</c>（对应 default_doublejump 动画）。
    /// </summary>
    public void PlayDoubleJumpAnim(bool hasHorizontalInput)
    {
        BeginAirJump(hasHorizontalInput, doubleJump: true);
    }

    void BeginAirJump(bool hasHorizontalInput, bool doubleJump)
    {
        if (isSwitchingWeapon)
            CompleteWeaponSwitch();

        InterruptLand();
        InterruptTurn();

        isCrouching = false;
        activeOneShotState = null;
        oneShotAutoExit = false;
        useDoubleJumpAnim = doubleJump;

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

        InvalidateLocomotionCache();
        SyncLocomotion();
    }

    public override bool PlayTurnAnim()
    {
        if (IsPlayingLand)
            InterruptLand();

        if (isSwitchingWeapon || isCrouching || !string.IsNullOrEmpty(activeOneShotState))
            return false;
        if (airPhase != AirPhaseType.Ground)
            return false;

        PlayOneShot(TurnStateName, autoExit: true);
        return true;
    }

    public override bool PlayCrouchTurnAnim()
    {
        if (isSwitchingWeapon || !isCrouching || bodyAnimator == null)
            return false;
        if (activeOneShotState == CrouchTurnStateName)
            return false;

        PlayOneShot(CrouchTurnStateName, autoExit: true);
        return true;
    }

    public override bool TryPlayRunStopLand()
    {
        if (isSwitchingWeapon || !isRunning || isCrouching || IsTurning || IsPlayingLand)
            return false;
        if (!string.IsNullOrEmpty(activeOneShotState) || airPhase != AirPhaseType.Ground)
            return false;
        if (isMelee || isDead)
            return false;

        isRunning = false;
        PlayOneShot(LandStateName, autoExit: true);
        return true;
    }

    public override void PlayIdleAnim()
    {
        isRunning = false;

        if (isSwitchingWeapon)
            return;

        if (isCrouching)
        {
            if (bodyAnimator != null)
                bodyAnimator.SetBool("IsRun", false);
            return;
        }

        if (string.IsNullOrEmpty(activeOneShotState) && airPhase == AirPhaseType.Ground && !isMelee)
            SyncLocomotion();
    }

    public override void PlayRunAnim()
    {
        if (isSwitchingWeapon)
            return;

        if (isCrouching && isMelee)
            return;

        isRunning = true;

        if (InterruptLand())
            return;
        if (InterruptTurn())
            return;

        if (isCrouching)
        {
            if (bodyAnimator != null)
                bodyAnimator.SetBool("IsRun", true);
            return;
        }

        if (string.IsNullOrEmpty(activeOneShotState) && airPhase == AirPhaseType.Ground)
            SyncLocomotion();
    }

    public override void PlayCrouchAnim()
    {
        if (isCrouching)
            return;

        if (isSwitchingWeapon)
            CompleteWeaponSwitch();

        InterruptLand();
        isCrouching = true;
        airPhase = AirPhaseType.Ground;
        airTrack = AirTrack.None;
        PlayOneShot(CrouchStartStateName, autoExit: false);
    }

    public override void PlayStandAnim()
    {
        if (!isCrouching)
            return;

        if (isSwitchingWeapon)
            CompleteWeaponSwitch();

        isCrouching = false;
        activeOneShotState = null;
        oneShotAutoExit = false;

        if (isMelee)
            CompleteMelee();

        InvalidateLocomotionCache();
        SyncLocomotion();
    }

    public override bool TryPlayMeleeAnim()
    {
        if (isDead || bodyAnimator == null)
            return false;

        // 切枪可被近战取消；近战本身仍须播完才能接下一段（由 Bob 输入锁保证）
        if (isSwitchingWeapon)
            CompleteWeaponSwitch();

        if (IsPlayingLand)
            InterruptLand();
        else if (IsTurning)
            InterruptTurn();

        string stateName = ResolveMeleeStateName();
        if (isCrouching && bodyAnimator != null)
            bodyAnimator.SetBool("IsRun", false);

        isMelee = true;
        isUltimate = false;
        activeMeleeStateName = stateName;
        meleeStartedAt = Time.time;
        bodyAnimator.Play(stateName, 0, 0f);
        return true;
    }

    /// <summary>
    /// 强制仰视/非仰视解析近战状态（忽略摇杆），用于 Rush 普攻↔上攻连段。
    /// </summary>
    public bool TryPlayMeleeAnimForcedLookUp(bool lookUp)
    {
        meleeLookUpOverride = lookUp;
        bool played = TryPlayMeleeAnim();
        meleeLookUpOverride = null;
        return played;
    }

    /// <summary>
    /// 播放当前武器特技（Special）。仅 weaponId 1/2/3 且 WeaponDefinition.special 已配置时可用。
    /// 走与近战相同的 IsMelee / 完成检测，便于 Bob_Controller 复用命中窗。
    /// </summary>
    public override bool TryPlaySpecialAnim()
        => TryPlaySpecialAnimInternal(ultimate: false);

    /// <summary>
    /// 大招：播放当前武器对应的 *_ult 状态（rush_ult / whip_ult / buzzsaw_ult）。
    /// 命中逻辑仍走 special profile，由 Bob_Controller 提高伤害并消耗 AbilityPower。
    /// </summary>
    public bool TryPlayUltimateAnim()
        => TryPlaySpecialAnimInternal(ultimate: true);

    bool TryPlaySpecialAnimInternal(bool ultimate)
    {
        if (isDead || bodyAnimator == null)
            return false;

        // 切枪可被特技/大招取消
        if (isSwitchingWeapon)
            CompleteWeaponSwitch();

        if (appliedWeaponDef == null
            || appliedWeaponDef.weaponId == 0
            || appliedWeaponDef.special == null)
            return false;

        string stateName = ultimate
            ? ResolveUltimateStateName(appliedWeaponDef.weaponId)
            : SpecialStateName;
        if (string.IsNullOrEmpty(stateName))
            return false;

        if (IsPlayingLand)
            InterruptLand();
        else if (IsTurning)
            InterruptTurn();

        if (isCrouching && bodyAnimator != null)
            bodyAnimator.SetBool("IsRun", false);

        isMelee = true;
        isUltimate = ultimate;
        activeMeleeStateName = stateName;
        meleeStartedAt = Time.time;
        bodyAnimator.Play(stateName, 0, 0f);
        return true;
    }

    static string ResolveUltimateStateName(int weaponId)
    {
        switch (weaponId)
        {
            case 1: return RushUltStateName;
            case 2: return WhipUltStateName;
            case 3: return BuzzsawUltStateName;
            default: return null;
        }
    }

    static bool IsUltimateStateName(string stateName)
        => stateName == RushUltStateName
            || stateName == WhipUltStateName
            || stateName == BuzzsawUltStateName;

    string ResolveMeleeStateName()
    {
        if (isCrouching)
        {
            meleeLookUpOverride = null;
            return CrouchMeleeStateName;
        }

        bool grounded = airPhase == AirPhaseType.Ground;
        bool lookUp;
        bool lookDown;
        if (meleeLookUpOverride.HasValue)
        {
            lookUp = meleeLookUpOverride.Value;
            lookDown = false;
            meleeLookUpOverride = null;
        }
        else
        {
            lookUp = IsLookUpHeld();
            lookDown = IsLookDownHeld();
        }

        // 空手仅地面有独立上攻；空中上看仍走 jump_attack。Rush 等武器空中可上攻。
        bool allowUpMelee = AppliedWeaponId != 0 || grounded;

        if (grounded)
        {
            if (lookUp && allowUpMelee)
                return UpMeleeStateName;
            return MeleeStateName;
        }

        if (lookUp && allowUpMelee)
            return AirUpMeleeStateName;

        if (lookDown)
            return JumpDownAttackStateName;

        return AirMeleeStateName;
    }

    /// <summary>
    /// 攻击判定时直接读当前移动输入，避免 Bob 比 PlayerMovement.HandleLook 更早 Update 时
    /// <see cref="isLookingUp"/> 仍为上一帧 false，导致站立 upattack 落成普通 Melee。
    /// </summary>
    bool IsLookUpHeld()
    {
        if (playerMovement != null
            && playerMovement.MoveInput.y > playerMovement.InputThreshold)
            return true;
        return isLookingUp;
    }

    bool IsLookDownHeld()
    {
        if (playerMovement != null
            && !physicsCheck.isGround
            && playerMovement.MoveInput.y < -playerMovement.InputThreshold)
            return true;
        return isLookingDown;
    }

    public override bool TryGetMeleeAnimProgress(out float normalizedTime)
    {
        normalizedTime = 0f;
        if (!isMelee || bodyAnimator == null || string.IsNullOrEmpty(activeMeleeStateName))
            return false;

        var info = bodyAnimator.GetCurrentAnimatorStateInfo(0);
        if (!info.IsName(activeMeleeStateName))
            return false;

        normalizedTime = info.normalizedTime;
        return true;
    }

    /// <summary>落地砸地等：重新从 0 播放当前近战片，并重置完成计时。</summary>
    public void RestartCurrentMeleeAnim()
    {
        if (!isMelee || bodyAnimator == null || string.IsNullOrEmpty(activeMeleeStateName))
            return;

        meleeStartedAt = Time.time;
        bodyAnimator.Play(activeMeleeStateName, 0, 0f);
    }

    public override void ApplyWeaponDefinition(WeaponDefinition def)
    {
        if (def == null)
            return;

        EnsureOverrideController();
        ApplyOverridesToController(bodyOverrideController, def);
        appliedWeaponDef = def;

        if (bodyAnimator != null && HasAnimatorParam(bodyAnimator, WeaponIdParam))
            bodyAnimator.SetInteger(WeaponIdParam, def.weaponId);

        InvalidateLocomotionCache();
    }

    public override bool TryPlayWeaponSwitchAnim(WeaponDefinition def)
        => TryPlayWeaponSwitchAnim(null, def);

    public override bool TryPlayWeaponSwitchAnim(WeaponDefinition fromDef, WeaponDefinition toDef)
    {
        if (toDef == null || isDead || bodyAnimator == null)
            return false;

        // 攻击/特技/大招须播完；不可用切枪打断
        if (isMelee)
            return false;

        if (isSwitchingWeapon)
            CompleteWeaponSwitch();

        if (IsPlayingLand)
            InterruptLand();
        else if (IsTurning)
            InterruptTurn();

        ApplyWeaponDefinition(toDef);

        AnimationClip switchClip = ResolveDirectionalSwitchClip(fromDef, toDef);
        OverrideNamedBaseClip(DefaultSwitchClipName, switchClip);

        isSwitchingWeapon = true;
        activeOneShotState = null;
        oneShotAutoExit = false;

        if (isCrouching)
            bodyAnimator.SetBool("IsRun", false);

        bodyAnimator.Play(WeaponSwitchStateName, 0, 0f);
        return true;
    }

    AnimationClip ResolveDirectionalSwitchClip(WeaponDefinition fromDef, WeaponDefinition toDef)
    {
        int fromId = fromDef != null ? fromDef.weaponId : -1;
        int toId = toDef.weaponId;

        if (IsDirectionalCycleWeapon(fromId) && IsDirectionalCycleWeapon(toId) && fromId != toId)
        {
            AnimationClip edge = GetDirectionalEdgeClip(fromId, toId);
            if (edge != null)
                return edge;
        }

        return toDef.weaponSwitch;
    }

    static bool IsDirectionalCycleWeapon(int weaponId)
        => weaponId == WeaponIdA || weaponId == WeaponIdB || weaponId == WeaponIdC;

    AnimationClip GetDirectionalEdgeClip(int fromId, int toId)
    {
        if (fromId == WeaponIdA && toId == WeaponIdB) return rushToWhip;
        if (fromId == WeaponIdA && toId == WeaponIdC) return rushToBuzz;
        if (fromId == WeaponIdB && toId == WeaponIdA) return whipToRush;
        if (fromId == WeaponIdB && toId == WeaponIdC) return whipToBuzz;
        if (fromId == WeaponIdC && toId == WeaponIdA) return buzzToRush;
        if (fromId == WeaponIdC && toId == WeaponIdB) return buzzToWhip;
        return null;
    }

    public override void PlayDieAnim()
    {
        if (isSwitchingWeapon)
            CompleteWeaponSwitch();

        isDead = true;
        isCrouching = false;
        isRunning = false;
        isMelee = false;
        isUltimate = false;
        activeOneShotState = null;
        oneShotAutoExit = false;
        PlayOneShot(DieStateName, autoExit: false);
    }

    public override void PlayHurtAnim()
    {
        if (bodyAnimator == null || !bodyAnimator.isActiveAndEnabled)
            return;

        foreach (var p in bodyAnimator.parameters)
        {
            if (p.type == AnimatorControllerParameterType.Trigger && p.name == "hurt")
            {
                bodyAnimator.SetTrigger("hurt");
                return;
            }
        }
    }

    public override bool TryGetDieAnimProgress(out float normalizedTime)
    {
        normalizedTime = 0f;
        if (!isDead || bodyAnimator == null)
            return false;

        var info = bodyAnimator.GetCurrentAnimatorStateInfo(0);
        if (!info.IsName(DieStateName))
            return false;

        normalizedTime = info.normalizedTime;
        return true;
    }

    public override void ResetFromDeath()
    {
        isDead = false;
        isSwitchingWeapon = false;
        isMelee = false;
        isUltimate = false;
        activeMeleeStateName = null;
        meleeStartedAt = -1f;
        activeOneShotState = null;
        oneShotAutoExit = false;
        oneShotStartedAt = -1f;
        InvalidateLocomotionCache();
        SyncLocomotion();
    }

    public override void OnFullBodyAnimationFinished()
    {
        if (!oneShotAutoExit || string.IsNullOrEmpty(activeOneShotState))
            return;

        if (activeOneShotState == CrouchTurnStateName && isCrouching)
        {
            CompleteCrouchTurnExit();
            return;
        }

        CompleteAutoOneShotExit();
    }

    public override bool InterruptLand()
    {
        if (!IsPlayingLand)
            return false;

        CompleteAutoOneShotExit();
        return true;
    }

    public override bool InterruptTurn()
    {
        if (!IsTurning)
            return false;

        if (activeOneShotState == CrouchTurnStateName)
            CompleteCrouchTurnExit();
        else
            CompleteAutoOneShotExit();

        return true;
    }

    void PlayOneShot(string stateName, bool autoExit)
    {
        activeOneShotState = stateName;
        oneShotAutoExit = autoExit;
        oneShotStartedAt = Time.time;
        if (bodyAnimator != null)
            bodyAnimator.Play(stateName, 0, 0f);
    }

    void AdvanceAirPhase(bool grounded, float velocityY)
    {
        switch (airPhase)
        {
            case AirPhaseType.Ground:
                if (airStateInitialized && wasGrounded && !grounded && !jumpInvokedThisFrame)
                {
                    if (velocityY > descendVelocityThreshold)
                        break;
                    if (physicsCheck != null && physicsCheck.WasOnSlopeRecently && velocityY > -8f)
                        break;

                    airTrack = AirTrack.Jump;
                    airPhase = AirPhaseType.Fall;
                }
                break;

            case AirPhaseType.Jump:
                if (ShouldLandFromAir(grounded, velocityY))
                {
                    airPhase = AirPhaseType.Fall;
                    useDoubleJumpAnim = false;
                    PlayOneShot(LandStateName, autoExit: true);
                    break;
                }
                if (velocityY <= descendVelocityThreshold)
                {
                    airPhase = AirPhaseType.Fall;
                    useDoubleJumpAnim = false;
                }
                break;

            case AirPhaseType.Leap:
                if (ShouldLandFromAir(grounded, velocityY))
                {
                    airPhase = AirPhaseType.LeapAir;
                    useDoubleJumpAnim = false;
                    PlayOneShot(LandStateName, autoExit: true);
                    break;
                }
                if (velocityY <= descendVelocityThreshold)
                {
                    airPhase = AirPhaseType.LeapAir;
                    useDoubleJumpAnim = false;
                }
                break;

            case AirPhaseType.Fall:
            case AirPhaseType.LeapAir:
                useDoubleJumpAnim = false;
                if (IsSolidlyGrounded(grounded))
                    PlayOneShot(LandStateName, autoExit: true);
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

    void SyncLocomotion()
    {
        if (bodyAnimator == null || isMelee || isSwitchingWeapon)
            return;

        if (isCrouching)
            return;

        int phase = (int)airPhase;
        bodyAnimator.SetInteger("AirPhase", phase);
        bodyAnimator.SetBool("IsRun", isRunning);

        string stateName = GetLocomotionStateName();
        bool phaseChanged = phase != lastSyncedPhase;
        bool stateChanged = stateName != lastLocomotionState;
        bool runChanged = airPhase == AirPhaseType.Ground && isRunning != lastSyncedRun;

        if (!phaseChanged && !stateChanged && !runChanged)
            return;

        if (stateChanged)
        {
            lastLocomotionState = stateName;
            bodyAnimator.Play(stateName, 0, 0f);
        }

        if (phaseChanged)
            lastSyncedPhase = phase;

        if (airPhase == AirPhaseType.Ground && runChanged)
            lastSyncedRun = isRunning;
    }

    string GetLocomotionStateName()
    {
        if (airPhase == AirPhaseType.Ground)
        {
            useDoubleJumpAnim = false;
            return isRunning ? "Run" : "Idle";
        }

        if (useDoubleJumpAnim
            && (airPhase == AirPhaseType.Jump || airPhase == AirPhaseType.Leap))
            return DoubleJumpStateName;

        return airPhase switch
        {
            AirPhaseType.Jump => "Jump",
            AirPhaseType.Fall => "Fall",
            AirPhaseType.Leap => "Leap",
            AirPhaseType.LeapAir => "LeapAir",
            _ => "Idle",
        };
    }

    void InvalidateLocomotionCache()
    {
        lastSyncedPhase = -1;
        lastLocomotionState = null;
        lastSyncedRun = !isRunning;
    }

    void ExitCrouchForAir(float velocityY)
    {
        isCrouching = false;
        activeOneShotState = null;
        oneShotAutoExit = false;

        bool hasHorizontal =
            isRunning || (rb != null && Mathf.Abs(rb.linearVelocity.x) > 0.1f);

        if (velocityY > descendVelocityThreshold)
        {
            airTrack = hasHorizontal ? AirTrack.Leap : AirTrack.Jump;
            airPhase = hasHorizontal ? AirPhaseType.Leap : AirPhaseType.Jump;
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

        InvalidateLocomotionCache();
        SyncLocomotion();
    }

    void TryAutoExitOneShot()
    {
        if (!oneShotAutoExit || string.IsNullOrEmpty(activeOneShotState))
            return;
        if (!IsOneShotDone(activeOneShotState))
            return;

        CompleteAutoOneShotExit();
    }

    void TryCompleteCrouchStart()
    {
        if (activeOneShotState != CrouchStartStateName || bodyAnimator == null)
            return;

        var info = bodyAnimator.GetCurrentAnimatorStateInfo(0);
        if (info.IsName(CrouchStateName) || info.IsName("CrouchMove"))
        {
            activeOneShotState = null;
            return;
        }

        if (info.IsName(CrouchStartStateName) && info.normalizedTime >= 1f)
        {
            activeOneShotState = null;
            bodyAnimator.Play(CrouchStateName, 0, 0f);
        }
    }

    void TryAutoExitCrouchTurn()
    {
        if (!oneShotAutoExit || activeOneShotState != CrouchTurnStateName)
            return;
        if (!IsOneShotDone(CrouchTurnStateName))
            return;

        CompleteCrouchTurnExit();
    }

    void CompleteCrouchTurnExit()
    {
        activeOneShotState = null;
        oneShotAutoExit = false;
        oneShotStartedAt = -1f;

        if (bodyAnimator == null)
            return;

        if (isMelee)
            bodyAnimator.Play(CrouchMeleeStateName, 0, 0f);
        else
            bodyAnimator.Play(CrouchStateName, 0, 0f);
    }

    void CompleteAutoOneShotExit()
    {
        if (activeOneShotState == LandStateName)
        {
            airPhase = AirPhaseType.Ground;
            airTrack = AirTrack.None;
        }

        activeOneShotState = null;
        oneShotAutoExit = false;
        oneShotStartedAt = -1f;
        InvalidateLocomotionCache();
        SyncLocomotion();
    }

    bool IsOneShotDone(string stateName)
    {
        if (bodyAnimator == null)
            return true;

        var info = bodyAnimator.GetCurrentAnimatorStateInfo(0);
        if (info.IsName(stateName))
        {
            // 空 Motion / 长度为 0 时 normalizedTime 可能不涨
            if (info.length <= 0.0001f)
                return Time.time - oneShotStartedAt >= OneShotEnterGrace;
            return info.normalizedTime >= 1f;
        }

        // Play 后短宽限期内允许尚未切到目标状态；超时仍不在该状态则视为结束，防止 IsTurning 永久锁移动
        return Time.time - oneShotStartedAt >= OneShotEnterGrace;
    }

    void MaintainMeleeCompletion()
    {
        if (!isMelee || bodyAnimator == null || string.IsNullOrEmpty(activeMeleeStateName))
            return;

        if (!bodyAnimator.isActiveAndEnabled)
        {
            CompleteMelee();
            return;
        }

        float elapsed = Time.time - meleeStartedAt;
        var info = bodyAnimator.GetCurrentAnimatorStateInfo(0);

        // 砸地：空中或冲击未结算时保持当前片（播完则停在末帧，不重绕、不切 Land）
        if (IsJumpDownAttack)
        {
            bool grounded = physicsCheck != null && physicsCheck.isGround;
            bool waitingForImpact = !grounded || HoldJumpDownAttackUntilImpact;
            if (waitingForImpact)
                return;

            if (!info.IsName(JumpDownAttackStateName))
            {
                if (elapsed < OneShotEnterGrace)
                    return;
                CompleteMelee();
                return;
            }

            if (info.length <= 0.0001f)
            {
                CompleteMelee();
                return;
            }

            if (info.normalizedTime < 1f)
                return;

            CompleteMelee();
            return;
        }

        if (!info.IsName(activeMeleeStateName))
        {
            if (isCrouching
                && activeMeleeStateName == CrouchMeleeStateName
                && (info.IsName(CrouchStateName) || info.IsName("CrouchMove")))
                return;

            // 给 Animator.Play 一帧宽限，避免同帧误 Complete
            if (elapsed < OneShotEnterGrace)
                return;

            CompleteMelee();
            return;
        }

        if (info.length <= 0.0001f)
        {
            if (elapsed >= OneShotEnterGrace)
                CompleteMelee();
            return;
        }

        if (info.normalizedTime >= 1f)
        {
            CompleteMelee();
            return;
        }

        // 循环攻击片 / 异常卡住兜底：超过片长一定比例强制结束
        float maxDuration = Mathf.Max(info.length * 1.25f, MeleeMaxDurationFallback);
        if (elapsed >= maxDuration)
            CompleteMelee();
    }

    void CompleteMelee()
    {
        isMelee = false;
        isUltimate = false;
        activeMeleeStateName = null;
        meleeStartedAt = -1f;
        HoldJumpDownAttackUntilImpact = false;

        if (isSwitchingWeapon)
            return;

        if (isCrouching && bodyAnimator != null)
        {
            bodyAnimator.SetBool("IsRun", isRunning);
            bodyAnimator.Play(CrouchStateName, 0, 0f);
            return;
        }

        InvalidateLocomotionCache();
        SyncLocomotion();
    }

    bool HasHorizontalMoveInput()
    {
        if (playerMovement == null)
            return false;
        return Mathf.Abs(playerMovement.MoveInput.x) > playerMovement.InputThreshold;
    }

    void MaintainWeaponSwitchCompletion()
    {
        if (!isSwitchingWeapon || bodyAnimator == null)
            return;

        if (!bodyAnimator.isActiveAndEnabled)
        {
            CompleteWeaponSwitch();
            return;
        }

        var info = bodyAnimator.GetCurrentAnimatorStateInfo(0);
        if (!info.IsName(WeaponSwitchStateName))
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

        if (isDead)
            return;

        if (isCrouching && bodyAnimator != null)
        {
            bodyAnimator.SetBool("IsRun", isRunning);
            bodyAnimator.Play(CrouchStateName, 0, 0f);
            return;
        }

        InvalidateLocomotionCache();
        SyncLocomotion();
    }

    void EnsureOverrideController()
    {
        if (bodyAnimator == null || bodyOverrideController != null)
            return;

        var current = bodyAnimator.runtimeAnimatorController;
        if (current is AnimatorOverrideController existing)
        {
            bodyOverrideController = existing;
            bodyBaseController = existing.runtimeAnimatorController;
        }
        else if (current != null)
        {
            bodyBaseController = current;
            bodyOverrideController = new AnimatorOverrideController(bodyBaseController)
            {
                name = bodyBaseController.name + "_WeaponOverride",
            };
            bodyAnimator.runtimeAnimatorController = bodyOverrideController;
        }
    }

    void ApplyOverridesToController(AnimatorOverrideController overrideController, WeaponDefinition def)
    {
        if (overrideController == null || def == null)
            return;

        var pairs = new List<KeyValuePair<AnimationClip, AnimationClip>>();
        overrideController.GetOverrides(pairs);

        for (int i = 0; i < pairs.Count; i++)
        {
            AnimationClip original = pairs[i].Key;
            if (original == null)
                continue;

            AnimationClip replacement = def.weaponId == 0
                ? original
                : def.GetOverrideForBaseClip(original);

            pairs[i] = new KeyValuePair<AnimationClip, AnimationClip>(
                original,
                replacement != null ? replacement : original);
        }

        overrideController.ApplyOverrides(pairs);
    }

    /// <summary>在全量 Apply 之后，单独覆盖某一基座 clip（用于方向性 default_switch）。</summary>
    void OverrideNamedBaseClip(string baseClipName, AnimationClip replacement)
    {
        if (bodyOverrideController == null || string.IsNullOrEmpty(baseClipName))
            return;

        var pairs = new List<KeyValuePair<AnimationClip, AnimationClip>>();
        bodyOverrideController.GetOverrides(pairs);

        bool changed = false;
        for (int i = 0; i < pairs.Count; i++)
        {
            AnimationClip original = pairs[i].Key;
            if (original == null || original.name != baseClipName)
                continue;

            pairs[i] = new KeyValuePair<AnimationClip, AnimationClip>(
                original,
                replacement != null ? replacement : original);
            changed = true;
        }

        if (changed)
            bodyOverrideController.ApplyOverrides(pairs);
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
