using UnityEngine;

/// <summary>
/// 装甲车站岗：原地 Idle，索敌范围内发现玩家后进入战斗循环。
/// </summary>
public class ArmoredVehicleIdleGuardState : BaseState
{
    ArmoredVehicleEnemy vehicle;

    public override void OnEnter(Enemy enemy)
    {
        currentEnemy = enemy;
        vehicle = enemy as ArmoredVehicleEnemy;
        currentEnemy.currentSpeed = 0f;
        currentEnemy.isAggro = false;

        if (currentEnemy.Rb != null)
            currentEnemy.Rb.linearVelocity = new Vector2(0f, currentEnemy.Rb.linearVelocity.y);

        if (vehicle != null)
        {
            vehicle.SetBumpersActive(false);
            vehicle.SetAnimBool("walk", false);
            vehicle.SetAnimBool("shoot", false);
            vehicle.SetAnimBool("missile", false);
            vehicle.SetAnimBool("ram", false);
            vehicle.SetAnimBool("ramWindup", false);
        }
    }

    public override void LogicUpdate()
    {
        if (vehicle == null || currentEnemy.isDead)
            return;

        if (!currentEnemy.isPatrol)
        {
            currentEnemy.isAggro = true;
            vehicle.EvaluateCycle();
            return;
        }

        if (currentEnemy.IsPlayerInPatrolRange())
        {
            currentEnemy.EnterPatrolCombat();
            vehicle.EvaluateCycle();
        }
    }

    public override void PhysicsUpdate() { }

    public override void OnExit() { }
}
