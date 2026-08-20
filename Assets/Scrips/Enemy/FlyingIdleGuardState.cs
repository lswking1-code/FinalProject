using UnityEngine;

/// <summary>
/// 飞行敌人巡逻站岗：悬停 Idle，索敌范围内发现玩家后进入战斗循环。
/// </summary>
public class FlyingIdleGuardState : BaseState
{
    FlyingEnemy flyingEnemy;

    public override void OnEnter(Enemy enemy)
    {
        currentEnemy = enemy;
        flyingEnemy = enemy as FlyingEnemy;
        currentEnemy.currentSpeed = 0f;
        currentEnemy.isAggro = false;

        if (currentEnemy.Rb != null)
            currentEnemy.Rb.linearVelocity = Vector2.zero;

        if (currentEnemy.anim != null)
        {
            currentEnemy.SetAnimBool("walk", false);
            currentEnemy.SetAnimBool("shoot", false);
            currentEnemy.SetAnimBool("shootDown", false);
        }
    }

    public override void LogicUpdate()
    {
        if (flyingEnemy == null || currentEnemy.isDead)
            return;

        if (!currentEnemy.isPatrol)
        {
            currentEnemy.isAggro = true;
            flyingEnemy.EvaluateCycle();
            return;
        }

        if (currentEnemy.IsPlayerInPatrolRange())
        {
            currentEnemy.EnterPatrolCombat();
            flyingEnemy.EvaluateCycle();
        }
    }

    public override void PhysicsUpdate()
    {
        if (flyingEnemy == null || currentEnemy.isHurt || currentEnemy.isDead)
            return;

        flyingEnemy.ApplyHoverBobInPlace();
    }

    public override void OnExit() { }
}
