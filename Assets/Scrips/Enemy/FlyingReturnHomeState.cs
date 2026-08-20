using UnityEngine;

/// <summary>
/// 飞行敌人脱战后飞回出生点（含 Y），抵达后重新进入站岗 Idle。
/// </summary>
public class FlyingReturnHomeState : BaseState
{
    FlyingEnemy flyingEnemy;

    public override void OnEnter(Enemy enemy)
    {
        currentEnemy = enemy;
        flyingEnemy = enemy as FlyingEnemy;
        currentEnemy.isAggro = false;
        currentEnemy.currentSpeed = currentEnemy.normalSpeed > 0f
            ? currentEnemy.normalSpeed
            : currentEnemy.chaseSpeed;

        if (currentEnemy.anim != null)
        {
            currentEnemy.SetAnimBool("shoot", false);
            currentEnemy.SetAnimBool("walk", true);
        }
    }

    public override void LogicUpdate()
    {
        if (currentEnemy == null || currentEnemy.isDead)
            return;

        if (flyingEnemy != null && flyingEnemy.GetDistanceToHome() <= currentEnemy.returnArriveDistance)
            currentEnemy.FinishPatrolReset();
    }

    public override void PhysicsUpdate()
    {
        if (flyingEnemy == null || currentEnemy.isHurt || currentEnemy.isDead)
            return;

        if (flyingEnemy.GetDistanceToHome() <= currentEnemy.returnArriveDistance)
            return;

        flyingEnemy.MoveTowardHome(currentEnemy.currentSpeed);
    }

    public override void OnExit()
    {
        if (currentEnemy?.anim != null)
            currentEnemy.SetAnimBool("walk", false);
    }
}
