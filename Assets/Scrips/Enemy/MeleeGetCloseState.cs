using UnityEngine;

/// <summary>
/// 近战敌人靠近状态：朝同侧站位槽移动，到达后进入行动循环。
/// </summary>
public class MeleeGetCloseState : BaseState
{
    MeleeEnemy meleeEnemy;

    public override void OnEnter(Enemy enemy)
    {
        currentEnemy = enemy;
        meleeEnemy = enemy as MeleeEnemy;
        currentEnemy.currentSpeed = currentEnemy.chaseSpeed;
        currentEnemy.FacePlayer();

        if (currentEnemy.anim != null)
            currentEnemy.SetAnimBool("walk", true);
    }

    public override void LogicUpdate()
    {
        if (meleeEnemy == null || currentEnemy.isDead)
            return;

        if (meleeEnemy.IsWithinApproachRange())
            meleeEnemy.EvaluateCycle();
    }

    public override void PhysicsUpdate()
    {
        if (meleeEnemy == null || currentEnemy.isHurt || currentEnemy.isDead)
            return;

        meleeEnemy.MoveTowardCombatSlot(meleeEnemy.GetApproachStopRange());
        meleeEnemy.TryFlipOnObstacle(meleeEnemy.GetCombatSlotMoveDir(meleeEnemy.GetApproachStopRange()));
    }

    public override void OnExit()
    {
        if (currentEnemy?.anim != null)
            currentEnemy.SetAnimBool("walk", false);
    }
}
