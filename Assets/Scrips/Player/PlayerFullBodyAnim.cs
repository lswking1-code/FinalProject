using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 单 Animator 全身动画（近战角色 / Bob）。Animator 状态名需与 <see cref="PlayerAnim"/> 全身层一致：
/// Idle, Run, Jump, Fall, Leap, LeapAir, Land, Turn, CrouchStart, Crouch, CrouchTurn, CrouchMove,
/// Melee, AirMelee, UpMelee, AirUpMelee, DownMelee, CrouchMelee, Die, WeaponSwitch。
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
    const string CrouchMeleeStateName = "CrouchMelee";
    const string DieStateName = "Die";
    const string WeaponSwitchStateName = "WeaponSwitch";
    const string DefaultSwitchClipName = "default_switch";

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

    AirPhaseType airPhase = AirPhaseType.Ground;
    AirTrack airTrack = AirTrack.None;

    string activeOneShotState;
    bool oneShotAutoExit;

    bool isCrouching;
    bool isRunning;
    bool isMelee;
    bool isSwitchingWeapon;
    bool isDead;
    bool isLookingUp;
    bool isLookingDown;
    bool jumpInvokedThisFrame;
    bool wasGrounded;
    bool airStateInitialized;

    int lastSyncedPhase = -1;
    string lastLocomotionState;
    bool lastSyncedRun;

    string activeMeleeStateName;
    AnimatorOverrideController bodyOverrideController;
    RuntimeAnimatorController bodyBaseController;
    WeaponDefinition appliedWeaponDef;

    const string WeaponIdParam = "WeaponID";

    public override bool IsCrouching => isCrouching;
    public override bool IsMelee => isMelee;
    public override bool IsSwitchingWeapon => isSwitchingWeapon;
    public override bool IsDead => isDead;
    public override bool IsLookingUp => isLookingUp;
    public override bool IsLookingDown => isLookingDown;
    public override AirPhaseType CurrentAirPhase => airPhase;
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
        if (isSwitchingWeapon)
            CompleteWeaponSwitch();

        InterruptLand();
        InterruptTurn();

        isCrouching = false;
        activeOneShotState = null;
        oneShotAutoExit = false;

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
        if (isSwitchingWeapon || isDead || bodyAnimator == null)
            return false;

        if (IsPlayingLand)
            InterruptLand();
        else if (IsTurning)
            InterruptTurn();

        string stateName = ResolveMeleeStateName();
        if (isCrouching && bodyAnimator != null)
            bodyAnimator.SetBool("IsRun", false);

        isMelee = true;
        activeMeleeStateName = stateName;
        bodyAnimator.Play(stateName, 0, 0f);
        return true;
    }

    string ResolveMeleeStateName()
    {
        if (isCrouching)
            return CrouchMeleeStateName;

        bool grounded = airPhase == AirPhaseType.Ground;

        if (grounded)
        {
            if (isLookingUp && HasClip(appliedWeaponDef != null ? appliedWeaponDef.upMelee : null))
                return UpMeleeStateName;
            return MeleeStateName;
        }

        if (isLookingUp && HasClip(appliedWeaponDef != null ? appliedWeaponDef.airUpMelee : null))
            return AirUpMeleeStateName;

        if (isLookingDown && HasClip(appliedWeaponDef != null ? appliedWeaponDef.downMelee : null))
            return DownMeleeStateName;

        return AirMeleeStateName;
    }

    static bool HasClip(AnimationClip clip) => clip != null;

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

        if (isSwitchingWeapon)
            CompleteWeaponSwitch();

        if (isMelee)
            CompleteMelee();

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
        activeOneShotState = null;
        oneShotAutoExit = false;
        PlayOneShot(DieStateName, autoExit: false);
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
        activeOneShotState = null;
        oneShotAutoExit = false;
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
                    PlayOneShot(LandStateName, autoExit: true);
                break;
        }
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
            return isRunning ? "Run" : "Idle";

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
        InvalidateLocomotionCache();
        SyncLocomotion();
    }

    bool IsOneShotDone(string stateName)
    {
        if (bodyAnimator == null)
            return true;

        var info = bodyAnimator.GetCurrentAnimatorStateInfo(0);
        return info.IsName(stateName) && info.normalizedTime >= 1f;
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

        var info = bodyAnimator.GetCurrentAnimatorStateInfo(0);
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
