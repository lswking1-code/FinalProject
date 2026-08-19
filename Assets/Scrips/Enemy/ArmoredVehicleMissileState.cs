using UnityEngine;

/// <summary>
/// 装甲车导弹：开仓动画结束后，交替从两个发射点向上发射索敌导弹。
/// </summary>
public class ArmoredVehicleMissileState : BaseState
{
    enum Phase
    {
        OpenBay,
        Firing
    }

    ArmoredVehicleEnemy vehicle;
    Phase phase;
    float timer;
    int missilesRemaining;
    int shotIndex;

    public override void OnEnter(Enemy enemy)
    {
        currentEnemy = enemy;
        vehicle = enemy as ArmoredVehicleEnemy;
        if (vehicle == null)
            return;

        vehicle.OnActionEntered(EnemyAction.Missile);
        vehicle.SetBumpersActive(false);
        vehicle.StopHorizontalMotion();
        vehicle.SetAnimBool("walk", false);
        vehicle.SetAnimBool("shoot", false);
        vehicle.SetAnimBool("ram", false);
        vehicle.SetAnimBool("ramWindup", false);
        vehicle.SetAnimBool("missile", true);

        missilesRemaining = vehicle.RollMissileCount();
        shotIndex = 0;
        timer = vehicle.missileBayDuration;
        phase = Phase.OpenBay;
    }

    public override void LogicUpdate()
    {
        if (vehicle == null || currentEnemy.isDead)
            return;

        if (phase == Phase.OpenBay)
            UpdateOpenBay();
        else
            UpdateFiring();
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

        vehicle.SetAnimBool("missile", false);
    }

    void UpdateOpenBay()
    {
        timer -= Time.deltaTime;
        if (vehicle.IsNamedAnimFinished(vehicle.missileBayStateName) || timer <= 0f)
            EnterFiring();
    }

    void EnterFiring()
    {
        phase = Phase.Firing;
        timer = 0f;
        FireOne();
    }

    void UpdateFiring()
    {
        if (missilesRemaining <= 0)
        {
            vehicle.FinishActionAndRecover();
            return;
        }

        timer += Time.deltaTime;
        if (timer < vehicle.missileFireInterval)
            return;

        timer = 0f;
        FireOne();
    }

    void FireOne()
    {
        vehicle.FireHomingMissile(shotIndex);
        shotIndex++;
        missilesRemaining--;
        if (missilesRemaining <= 0)
            vehicle.FinishActionAndRecover();
    }
}
