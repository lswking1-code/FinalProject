using UnityEngine;

/// <summary>
/// 玩家动画公共 API。具体实现：<see cref="PlayerAnim"/>（上下半身分轨）或 <see cref="PlayerFullBodyAnim"/>（单 Animator 全身）。
/// Prefab 上只挂其中一种，不要同时挂。
/// </summary>
public class PlayerAnimBase : MonoBehaviour
{
    public enum AirPhaseType
    {
        Ground = 0,
        Jump = 1,
        Fall = 2,
        Leap = 3,
        LeapAir = 4,
    }

    public virtual bool IsCrouching => false;
    public virtual bool IsShooting => false;
    public virtual bool IsCharging => false;
    public virtual bool IsDispatching => false;
    public virtual MachinistChargeAim ActiveChargeAim => MachinistChargeAim.Forward;
    public virtual bool IsPlayingMachinistComboShoot => false;
    public virtual bool IsPlayingLoadBullet => false;
    public virtual bool IsThrowing => false;
    public virtual bool IsMelee => false;
    public virtual bool IsSwitchingWeapon => false;
    public virtual bool IsDead => false;
    public virtual bool IsLookingUp => false;
    public virtual bool IsLookingDown => false;
    public virtual AirPhaseType CurrentAirPhase => AirPhaseType.Ground;
    public virtual bool IsInFullBody => false;
    public virtual string CurrentFullBodyState => null;
    public virtual bool IsPlayingLand => false;
    public virtual bool IsTurning => false;

    public virtual void UpdateAirState(bool grounded) { }

    public virtual void UpdateAirState(bool grounded, float velocityY) { }

    public virtual void PlayJumpAnim(bool hasHorizontalInput) { }

    public virtual bool PlayTurnAnim() => false;

    public virtual bool PlayCrouchTurnAnim() => false;

    public virtual bool TryPlayRunStopLand() => false;

    public virtual void PlayIdleAnim() { }

    public virtual void PlayRunAnim() { }

    public virtual void PlayCrouchAnim() { }

    public virtual void PlayStandAnim() { }

    public virtual bool TryPlayShootAnim() => false;

    public virtual bool TryPlayMachinistShootAnim(MachinistShootKind kind) => false;

    public virtual void InterruptMachinistComboShootFromInput() { }

    public virtual bool TryPlayLoadBulletAnim() => false;

    public virtual bool BeginMachinistCharge() => false;

    public virtual void SetChargeAim(MachinistChargeAim aim) { }

    public virtual void SyncChargeAimFromInput(bool wantLookUp, bool wantLookDown, bool wantCrouch) { }

    public virtual bool ReleaseMachinistCharge() => false;

    public virtual bool BeginDispatch() => false;

    public virtual void SetDispatchHold(bool hold) { }

    public virtual void SetDispatchAutoEnd(bool autoEnd) { }

    public virtual void EndDispatch() { }

    public virtual bool TryPlayThrowAnim() => false;

    public virtual bool TryPlayMeleeAnim() => false;

    public virtual void ApplyWeaponDefinition(WeaponDefinition def) { }

    public virtual bool TryPlayWeaponSwitchAnim(WeaponDefinition def) => false;

    public virtual bool TryGetMeleeAnimProgress(out float normalizedTime)
    {
        normalizedTime = 0f;
        return false;
    }

    public virtual void PlayDieAnim() { }

    public virtual bool TryGetDieAnimProgress(out float normalizedTime)
    {
        normalizedTime = 0f;
        return false;
    }

    public virtual void ResetFromDeath() { }

    public virtual void SetLookUp(bool active) { }

    public virtual void SetLookDown(bool active) { }

    public virtual void EnterFullBody(string stateName, bool autoExitOnComplete) { }

    public virtual void ExitFullBody() { }

    public virtual void OnFullBodyAnimationFinished() { }

    public virtual void OnLandAnimationFinished() => OnFullBodyAnimationFinished();

    public virtual bool InterruptLand() => false;

    public virtual bool InterruptTurn() => false;
}
