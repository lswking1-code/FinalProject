using UnityEngine;

/// <summary>
/// 近战敌人随机移动状态：持续 actionDuration 秒，在 idealRange 附近走位。
/// 过近时优先远离玩家；朝向与移动方向同步，并在墙体 / 实心地面边缘处转身。
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

        KeepIdealDistance();

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

    /// <summary>
    /// 贴得比理想距离更近时改为远离玩家，避免 Move 期间贴脸。
    /// </summary>
    void KeepIdealDistance()
    {
        if (meleeEnemy.GetHorizontalDistanceToPlayer() >= meleeEnemy.GetSlottedRange(meleeEnemy.GetIdealRange()) - meleeEnemy.idealRangeSlack)
            return;

        float away = meleeEnemy.GetMoveDirAwayFromPlayer();
        if (away == 0f || Mathf.Sign(moveDir) == Mathf.Sign(away))
            return;

        if (!meleeEnemy.HasGroundAhead(away) && meleeEnemy.HasGroundAhead(-away))
            return;

        moveDir = away;
        meleeEnemy.FaceDirection(moveDir);
    }

    float PickSafeMoveDir()
    {
        float prefer;
        float dist = meleeEnemy.GetHorizontalDistanceToPlayer();
        float ideal = meleeEnemy.GetSlottedRange(meleeEnemy.GetIdealRange());

        if (dist < ideal - meleeEnemy.idealRangeSlack)
            prefer = meleeEnemy.GetMoveDirAwayFromPlayer();
        else
            prefer = Random.value < 0.5f ? -1f : 1f;

        if (meleeEnemy.HasGroundAhead(prefer) || !meleeEnemy.HasGroundAhead(-prefer))
            return prefer;

        return -prefer;
    }
}
