using UnityEngine;

/// <summary>
/// 远程敌人随机移动状态：持续 actionDuration 秒，随机水平走位。
/// 朝向与移动方向同步，并在墙体 / 实心地面边缘处转身；单向平台上可走下去。
/// </summary>
public class RangedMoveState : BaseState
{
    RangedEnemy rangedEnemy;
    float actionTimer;
    float moveDir;

    public override void OnEnter(Enemy enemy)
    {
        currentEnemy = enemy;
        rangedEnemy = enemy as RangedEnemy;

        if (rangedEnemy == null)
            return;

        rangedEnemy.OnActionEntered(EnemyAction.Move);
        moveDir = PickSafeMoveDir();
        rangedEnemy.FaceDirection(moveDir);
        actionTimer = rangedEnemy.actionDuration;
        currentEnemy.currentSpeed = currentEnemy.normalSpeed;
        currentEnemy.anim.SetBool("walk", true);
    }

    public override void LogicUpdate()
    {
        if (rangedEnemy == null || currentEnemy.isDead)
            return;

        actionTimer -= Time.deltaTime;

        if (actionTimer <= 0f)
            rangedEnemy.EvaluateCycle();
    }

    public override void PhysicsUpdate()
    {
        if (rangedEnemy == null || currentEnemy.isHurt || currentEnemy.isDead)
            return;

        if (rangedEnemy.TryFlipOnObstacleOrLedge(moveDir))
            moveDir = -moveDir;
        else if (!rangedEnemy.HasGroundAhead(moveDir))
        {
            // 两侧都无地面（窄台边缘等）：停步，避免来回抖动掉下
            rangedEnemy.MoveHorizontal(0f);
            return;
        }

        rangedEnemy.MoveHorizontal(moveDir);
    }

    public override void OnExit()
    {
        currentEnemy.anim.SetBool("walk", false);
    }

    float PickSafeMoveDir()
    {
        float prefer = Random.value < 0.5f ? -1f : 1f;
        if (rangedEnemy.HasGroundAhead(prefer) || !rangedEnemy.HasGroundAhead(-prefer))
            return prefer;

        return -prefer;
    }
}
