using UnityEngine;

/// <summary>
/// 装甲车机枪：瞄准阶段转动炮塔，随后沿锁定方向连射；可循环多轮瞄准→射击。
/// </summary>
public class ArmoredVehicleGunState : BaseState
{
    enum Phase
    {
        Aim,
        Burst
    }

    ArmoredVehicleEnemy vehicle;
    Phase phase;
    int cyclesRemaining;
    int shotsRemaining;
    float timer;

    public override void OnEnter(Enemy enemy)
    {
        currentEnemy = enemy;
        vehicle = enemy as ArmoredVehicleEnemy;
        if (vehicle == null)
            return;

        vehicle.OnActionEntered(EnemyAction.Shot);
        vehicle.SetBumpersActive(false);
        vehicle.StopHorizontalMotion();
        vehicle.SetAnimBool("walk", false);
        vehicle.SetAnimBool("missile", false);
        vehicle.SetAnimBool("ram", false);
        vehicle.SetAnimBool("ramWindup", false);

        cyclesRemaining = vehicle.RollGunCycleCount();
        EnterAim();
    }

    public override void LogicUpdate()
    {
        if (vehicle == null || currentEnemy.isDead)
            return;

        vehicle.StopHorizontalMotion();

        if (phase == Phase.Aim)
            UpdateAim();
        else
            UpdateBurst();
    }

    public override void PhysicsUpdate()
    {
        if (vehicle == null || currentEnemy.isHurt || currentEnemy.isDead)
            return;

        vehicle.StopHorizontalMotion();
    }

    public override void OnExit()
    {
        if (vehicle == null)
            return;

        vehicle.SetAnimBool("shoot", false);
    }

    void EnterAim()
    {
        phase = Phase.Aim;
        timer = 0f;
        vehicle.SetAnimBool("shoot", false);
        vehicle.BeginGunAim();

        if (vehicle.mgAimDuration <= 0f)
        {
            vehicle.SnapGunToPlayer();
            vehicle.LockFireDirection();
            EnterBurst();
        }
    }

    void UpdateAim()
    {
        timer += Time.deltaTime;
        float duration = Mathf.Max(0.0001f, vehicle.mgAimDuration);
        vehicle.AimGunAtPlayer(timer / duration);

        if (timer < vehicle.mgAimDuration)
            return;

        vehicle.SnapGunToPlayer();
        vehicle.LockFireDirection();
        EnterBurst();
    }

    void EnterBurst()
    {
        phase = Phase.Burst;
        shotsRemaining = Mathf.Max(1, vehicle.mgBurstCount);
        timer = 0f;
        vehicle.SetAnimBool("shoot", true);
        FireOne();
    }

    void UpdateBurst()
    {
        if (shotsRemaining <= 0)
        {
            FinishBurst();
            return;
        }

        timer += Time.deltaTime;
        if (timer < vehicle.mgFireInterval)
            return;

        timer = 0f;
        FireOne();
    }

    void FireOne()
    {
        vehicle.FireLockedProjectile();
        shotsRemaining--;
        if (shotsRemaining <= 0)
            FinishBurst();
    }

    void FinishBurst()
    {
        cyclesRemaining--;
        vehicle.SetAnimBool("shoot", false);

        if (cyclesRemaining > 0)
            EnterAim();
        else
            vehicle.FinishActionAndRecover();
    }
}
