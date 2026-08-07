using UnityEngine;

/// <summary>
/// 单 Animator 全身动画（近战角色）。Animator 状态名需与 <see cref="PlayerAnim"/> 全身层一致：
/// Idle, Run, Jump, Fall, Leap, LeapAir, Land, Turn, CrouchStart, Crouch, CrouchTurn, CrouchMove, Melee, AirMelee, CrouchMelee, Die。
/// 推荐 AnimatorController：<c>Assets/Animation/meleeFullBody.controller</c>。
/// Prefab：只挂本组件，勿与 <see cref="PlayerAnim"/> 同挂；配合 <see cref="MeleeAttackInput"/> 而非 PlayerShooting。
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
    const string CrouchMeleeStateName = "CrouchMelee";
    const string DieStateName = "Die";

    [Header("全身 Animator")]
    public Animator bodyAnimator;

    [Header("空中")]
    [Tooltip("竖直速度低于等于该值时视为开始下落")]
    public float descendVelocityThreshold = 0f;

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
    bool isDead;
    bool jumpInvokedThisFrame;
    bool wasGrounded;
    bool airStateInitialized;

    int lastSyncedPhase = -1;
    string lastLocomotionState;
    bool lastSyncedRun;

    string activeMeleeStateName;
    AnimatorOverrideController bodyOverrideController;
    RuntimeAnimatorController bodyBaseController;

    const string WeaponIdParam = "WeaponID";

    public override bool IsCrouching => isCrouching;
    public override bool IsMelee => isMelee;
    public override bool IsDead => isDead;
    public override AirPhaseType CurrentAirPhase => airPhase;
    public override string CurrentFullBodyState => activeOneShotState;
    public override bool IsPlayingLand => activeOneShotState == LandStateName;
    public override bool IsTurning =>
        activeOneShotState == TurnStateName || activeOneShotState == CrouchTurnStateName;
    public override bool IsInFullBody => isCrouching || !string.IsNullOrEmpty(activeOneShotState);

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

    public override void UpdateAirState(bool grounded) =>
        UpdateAirState(grounded, rb != null ? rb.linearVelocity.y : 0f);

    public override void UpdateAirState(bool grounded, float velocityY)
    {
        if (isDead)
            return;

        if (isCrouching && !grounded)
            ExitCrouchForAir(velocityY);

        if (isCrouching)
        {
            TryCompleteCrouchStart();
            TryAutoExitCrouchTurn();
            MaintainMeleeCompletion();
            wasGrounded = grounded;
            return;
        }

        if (!string.IsNullOrEmpty(activeOneShotState))
        {
            TryAutoExitOneShot();
            MaintainMeleeCompletion();
            wasGrounded = grounded;
            return;
        }

        AdvanceAirPhase(grounded, velocityY);
        SyncLocomotion();
        MaintainMeleeCompletion();
        wasGrounded = grounded;
        airStateInitialized = true;
    }

    public override void PlayJumpAnim(bool hasHorizontalInput)
    {
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
        if (isCrouching || !string.IsNullOrEmpty(activeOneShotState))
            return false;
        if (airPhase != AirPhaseType.Ground)
            return false;

        PlayOneShot(TurnStateName, autoExit: true);
        return true;
    }

    public override bool PlayCrouchTurnAnim()
    {
        if (!isCrouching || bodyAnimator == null)
            return false;
        if (activeOneShotState == CrouchTurnStateName)
            return false;

        PlayOneShot(CrouchTurnStateName, autoExit: true);
        return true;
    }

    public override bool TryPlayRunStopLand()
    {
        if (!isRunning || isCrouching || IsTurning || IsPlayingLand)
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
        if (IsPlayingLand)
            InterruptLand();
        else if (IsTurning)
            InterruptTurn();

        string stateName;
        if (isCrouching)
        {
            stateName = CrouchMeleeStateName;
            if (bodyAnimator != null)
                bodyAnimator.SetBool("IsRun", false);
        }
        else
            stateName = airPhase == AirPhaseType.Ground ? MeleeStateName : AirMeleeStateName;

        if (bodyAnimator == null)
            return false;

        isMelee = true;
        activeMeleeStateName = stateName;
        bodyAnimator.Play(stateName, 0, 0f);
        return true;
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

    public override void ApplyWeaponDefinition(WeaponDefinition def)
    {
        if (def == null)
            return;

        EnsureOverrideController();
        ApplyOverridesToController(bodyOverrideController, def);

        if (bodyAnimator != null && HasAnimatorParam(bodyAnimator, WeaponIdParam))
            bodyAnimator.SetInteger(WeaponIdParam, def.weaponId);

        InvalidateLocomotionCache();
    }

    public override bool TryPlayWeaponSwitchAnim(WeaponDefinition def)
    {
        if (def == null || isDead)
            return false;

        // melee_full 无 WeaponSwitch 状态：即时换装，不进入 IsSwitchingWeapon 阻塞
        ApplyWeaponDefinition(def);
        return true;
    }

    public override void PlayDieAnim()
    {
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
        if (bodyAnimator == null || isMelee)
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
