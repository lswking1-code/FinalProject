using UnityEngine;

/// <summary>
/// 近战敌人靠近状态：持续朝玩家移动，进入理想站位距离后进入行动循环。
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
            currentEnemy.anim.SetBool("walk", true);
    }

    public override void LogicUpdate()
    {
        if (meleeEnemy == null || currentEnemy.isDead)
            return;

        if (meleeEnemy.GetHorizontalDistanceToPlayer() <= meleeEnemy.GetApproachStopRange())
            meleeEnemy.EvaluateCycle();
    }

    public override void PhysicsUpdate()
    {
        if (meleeEnemy == null || currentEnemy.isHurt || currentEnemy.isDead)
            return;

        meleeEnemy.MoveTowardPlayer();
        meleeEnemy.TryFlipOnObstacle(meleeEnemy.GetMoveDirTowardPlayer());
    }

    public override void OnExit()
    {
        if (currentEnemy?.anim != null)
            currentEnemy.anim.SetBool("walk", false);
    }
}
