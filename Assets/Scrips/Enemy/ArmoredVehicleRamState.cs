using UnityEngine;

/// <summary>
/// 装甲车冲撞：预备动画后朝玩家所在水平侧冲刺（前冲或倒车，不转身），撞玩家/墙后进入后摇。
/// </summary>
public class ArmoredVehicleRamState : BaseState
{
    enum Phase
    {
        Windup,
        Dash,
        Recovery
    }

    ArmoredVehicleEnemy vehicle;
    Phase phase;
    float timer;
    float dashDir;
    bool hitPlayer;

    public override void OnEnter(Enemy enemy)
    {
        currentEnemy = enemy;
        vehicle = enemy as ArmoredVehicleEnemy;
        if (vehicle == null)
            return;

        vehicle.OnActionEntered(EnemyAction.Ram);
        vehicle.SetBumpersActive(false);
        vehicle.StopHorizontalMotion();
        vehicle.SetAnimBool("walk", false);
        vehicle.SetAnimBool("shoot", false);
        vehicle.SetAnimBool("missile", false);
        vehicle.SetAnimBool("ram", false);
        vehicle.SetAnimBool("ramWindup", true);

        dashDir = vehicle.GetRamDashSign();
        hitPlayer = false;
        timer = vehicle.ramWindupDuration;
        phase = Phase.Windup;

        vehicle.SubscribeBumperHits(OnBumperHit);
    }

    public override void LogicUpdate()
    {
        if (vehicle == null || currentEnemy.isDead)
            return;

        switch (phase)
        {
            case Phase.Windup:
                UpdateWindup();
                break;
            case Phase.Dash:
                UpdateDash();
                break;
            case Phase.Recovery:
                UpdateRecovery();
                break;
        }
    }

    public override void PhysicsUpdate()
    {
        if (vehicle == null || currentEnemy.isHurt || currentEnemy.isDead)
            return;

        if (phase != Phase.Dash)
        {
            vehicle.StopHorizontalMotion();
            return;
        }

        currentEnemy.currentSpeed = vehicle.ramSpeed;
        vehicle.MoveHorizontal(dashDir);
    }

    public override void OnExit()
    {
        if (vehicle == null)
            return;

        vehicle.UnsubscribeBumperHits(OnBumperHit);
        vehicle.SetBumpersActive(false);
        vehicle.SetAnimBool("ramWindup", false);
        vehicle.SetAnimBool("ram", false);
        vehicle.StopHorizontalMotion();
    }

    void UpdateWindup()
    {
        timer -= Time.deltaTime;
        if (vehicle.IsNamedAnimFinished(vehicle.ramWindupStateName) || timer <= 0f)
            EnterDash();
    }

    void EnterDash()
    {
        phase = Phase.Dash;
        timer = vehicle.ramMaxDuration;
        hitPlayer = false;
        vehicle.SetAnimBool("ramWindup", false);
        vehicle.SetAnimBool("ram", true);
        vehicle.SetBumpersActive(true);
        currentEnemy.currentSpeed = vehicle.ramSpeed;
    }

    void UpdateDash()
    {
        if (hitPlayer
            || vehicle.IsWallInDirection(dashDir)
            || currentEnemy.IsLedgeBlocking(dashDir))
        {
            EnterRecovery();
            return;
        }

        timer -= Time.deltaTime;
        if (timer <= 0f)
            EnterRecovery();
    }

    void EnterRecovery()
    {
        phase = Phase.Recovery;
        timer = Mathf.Max(0f, vehicle.ramRecoveryDuration);
        vehicle.SetBumpersActive(false);
        vehicle.SetAnimBool("ram", false);
        vehicle.StopHorizontalMotion();
        currentEnemy.currentSpeed = 0f;
    }

    void UpdateRecovery()
    {
        timer -= Time.deltaTime;
        if (timer <= 0f)
            vehicle.FinishActionAndRecover();
    }

    void OnBumperHit(Character target, int damage)
    {
        if (target == null || !target.CompareTag("Player"))
            return;

        hitPlayer = true;
    }
}
