using UnityEngine;

/// <summary>
/// AE-74 站岗：原地 Idle，索敌范围内发现玩家后进入战斗循环。
/// </summary>
public class AE74IdleGuardState : BaseState
{
    AE74Enemy robot;

    public override void OnEnter(Enemy enemy)
    {
        currentEnemy = enemy;
        robot = enemy as AE74Enemy;
        currentEnemy.currentSpeed = 0f;
        currentEnemy.isAggro = false;

        if (robot != null)
            robot.EnterIdlePose();
    }

    public override void LogicUpdate()
    {
        if (robot == null || currentEnemy.isDead)
            return;

        if (!currentEnemy.isPatrol)
        {
            currentEnemy.isAggro = true;
            robot.EvaluateCycle();
            return;
        }

        if (currentEnemy.IsPlayerInPatrolRange())
        {
            currentEnemy.EnterPatrolCombat();
            robot.EvaluateCycle();
        }
    }

    public override void PhysicsUpdate() { }

    public override void OnExit() { }
}
