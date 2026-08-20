using UnityEngine;

/// <summary>
/// 远程敌人巡逻站岗：原地 Idle，索敌范围内发现玩家后进入战斗循环。
/// </summary>
public class RangedIdleGuardState : BaseState
{
    RangedEnemy rangedEnemy;

    public override void OnEnter(Enemy enemy)
    {
        currentEnemy = enemy;
        rangedEnemy = enemy as RangedEnemy;
        currentEnemy.currentSpeed = 0f;
        currentEnemy.isAggro = false;

        if (currentEnemy.Rb != null)
            currentEnemy.Rb.linearVelocity = new Vector2(0f, currentEnemy.Rb.linearVelocity.y);

        if (currentEnemy.anim != null)
        {
            currentEnemy.SetAnimBool("walk", false);
            currentEnemy.SetAnimBool("shoot", false);
            currentEnemy.SetAnimBool("crouch", false);
            currentEnemy.SetAnimBool("reload", false);
        }
    }

    public override void LogicUpdate()
    {
        if (rangedEnemy == null || currentEnemy.isDead)
            return;

        if (!currentEnemy.isPatrol)
        {
            currentEnemy.isAggro = true;
            rangedEnemy.EvaluateCycle();
            return;
        }

        if (currentEnemy.IsPlayerInPatrolRange())
        {
            currentEnemy.EnterPatrolCombat();
            rangedEnemy.EvaluateCycle();
        }
    }

    public override void PhysicsUpdate() { }

    public override void OnExit() { }
}
