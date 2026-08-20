using UnityEngine;

/// <summary>
/// 近战敌人巡逻站岗：原地 Idle，索敌范围内发现玩家后进入战斗循环。
/// </summary>
public class MeleeIdleGuardState : BaseState
{
    MeleeEnemy meleeEnemy;

    public override void OnEnter(Enemy enemy)
    {
        currentEnemy = enemy;
        meleeEnemy = enemy as MeleeEnemy;
        currentEnemy.currentSpeed = 0f;
        currentEnemy.isAggro = false;

        if (currentEnemy.Rb != null)
            currentEnemy.Rb.linearVelocity = new Vector2(0f, currentEnemy.Rb.linearVelocity.y);

        if (currentEnemy.anim != null)
        {
            currentEnemy.SetAnimBool("walk", false);
            currentEnemy.SetAnimBool("melee", false);
            currentEnemy.SetAnimBool("meleeWindup", false);
        }
    }

    public override void LogicUpdate()
    {
        if (meleeEnemy == null || currentEnemy.isDead)
            return;

        if (!currentEnemy.isPatrol)
        {
            currentEnemy.isAggro = true;
            meleeEnemy.EvaluateCycle();
            return;
        }

        if (currentEnemy.IsPlayerInPatrolRange())
        {
            currentEnemy.EnterPatrolCombat();
            meleeEnemy.EvaluateCycle();
        }
    }

    public override void PhysicsUpdate() { }

    public override void OnExit() { }
}
