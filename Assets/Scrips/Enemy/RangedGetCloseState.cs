using UnityEngine;

/// <summary>
/// 远程敌人靠近状态：朝同侧站位槽移动，进入射击距离后立即进入 Action 判定。
/// </summary>
public class RangedGetCloseState : BaseState
{
    RangedEnemy rangedEnemy;

    public override void OnEnter(Enemy enemy)
    {
        currentEnemy = enemy;
        rangedEnemy = enemy as RangedEnemy;
        currentEnemy.currentSpeed = currentEnemy.chaseSpeed;
        currentEnemy.FacePlayer();
        currentEnemy.anim.SetBool("walk", true);
    }

    public override void LogicUpdate()
    {
        if (rangedEnemy == null)
            return;

        if (rangedEnemy.GetCombatDistanceToPlayer() <= rangedEnemy.GetSlottedRange(rangedEnemy.shootRange))
            rangedEnemy.EvaluateCycle();
    }

    public override void PhysicsUpdate()
    {
        if (rangedEnemy == null || currentEnemy.isHurt || currentEnemy.isDead)
            return;

        rangedEnemy.MoveTowardCombatSlot(rangedEnemy.shootRange);
        rangedEnemy.TryFlipOnObstacle(rangedEnemy.GetCombatSlotMoveDir(rangedEnemy.shootRange));
    }

    public override void OnExit()
    {
        currentEnemy.anim.SetBool("walk", false);
    }
}
