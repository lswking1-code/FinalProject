using UnityEngine;

/// <summary>
/// 装甲车每个行动结束后的 Idle 后摇：停步、清动画，等待 actionRecoveryDuration 后再掷骰。
/// </summary>
public class ArmoredVehicleActionRecoveryState : BaseState
{
    ArmoredVehicleEnemy vehicle;
    float timer;

    public override void OnEnter(Enemy enemy)
    {
        currentEnemy = enemy;
        vehicle = enemy as ArmoredVehicleEnemy;
        if (vehicle == null)
            return;

        timer = Mathf.Max(0f, vehicle.actionRecoveryDuration);
        vehicle.EnterIdlePose();
    }

    public override void LogicUpdate()
    {
        if (vehicle == null || currentEnemy.isDead)
            return;

        timer -= Time.deltaTime;
        if (timer <= 0f)
            vehicle.EvaluateCycle();
    }

    public override void PhysicsUpdate()
    {
        if (vehicle == null || currentEnemy.isDead)
            return;

        vehicle.StopHorizontalMotion();
    }

    public override void OnExit() { }
}
