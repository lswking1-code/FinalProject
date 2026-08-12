using UnityEngine;

/// <summary>
/// 近战敌人随机移动状态：持续 actionDuration 秒，随机水平走位。
/// 朝向与移动方向同步，并在墙体 / 平台边缘处转身，避免倒着走出平台。
/// </summary>
public class MeleeMoveState : BaseState
{
    MeleeEnemy meleeEnemy;
    float actionTimer;
    float moveDir;

    public override void OnEnter(Enemy enemy)
    {
        currentEnemy = enemy;
        meleeEnemy = enemy as MeleeEnemy;

        if (meleeEnemy == null)
            return;

        meleeEnemy.OnActionEntered(EnemyAction.Move);
        moveDir = PickSafeMoveDir();
        meleeEnemy.FaceDirection(moveDir);
        actionTimer = meleeEnemy.actionDuration;
        currentEnemy.currentSpeed = currentEnemy.normalSpeed;
        currentEnemy.anim.SetBool("walk", true);
    }

    public override void LogicUpdate()
    {
        if (meleeEnemy == null || currentEnemy.isDead)
            return;

        actionTimer -= Time.deltaTime;

        if (actionTimer <= 0f)
            meleeEnemy.EvaluateCycle();
    }

    public override void PhysicsUpdate()
    {
        if (meleeEnemy == null || currentEnemy.isHurt || currentEnemy.isDead)
            return;

        if (meleeEnemy.TryFlipOnObstacleOrLedge(moveDir))
            moveDir = -moveDir;
        else if (!meleeEnemy.HasGroundAhead(moveDir))
        {
            // 两侧都无地面（窄台边缘等）：停步，避免来回抖动掉下
            meleeEnemy.MoveHorizontal(0f);
            return;
        }

        meleeEnemy.MoveHorizontal(moveDir);
    }

    public override void OnExit()
    {
        currentEnemy.anim.SetBool("walk", false);
    }

    float PickSafeMoveDir()
    {
        float prefer = Random.value < 0.5f ? -1f : 1f;
        if (meleeEnemy.HasGroundAhead(prefer) || !meleeEnemy.HasGroundAhead(-prefer))
            return prefer;

        return -prefer;
    }
}
