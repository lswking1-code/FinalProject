using UnityEngine;

/// <summary>
/// 远程敌人随机移动状态：持续 actionDuration 秒，随机水平走位。
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
        moveDir = Random.value < 0.5f ? -1f : 1f;
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
        // #region agent log
        if (Time.frameCount % 60 == 0)
            DebugAgentLog.Log("H3", "RangedMoveState.PhysicsUpdate", "entered",
                $"{{\"isHurt\":{currentEnemy.isHurt.ToString().ToLower()},\"isDead\":{currentEnemy.isDead.ToString().ToLower()},\"moveDir\":{moveDir},\"currentSpeed\":{currentEnemy.currentSpeed}}}");
        // #endregion

        if (rangedEnemy == null || currentEnemy.isHurt || currentEnemy.isDead)
            return;

        rangedEnemy.MoveHorizontal(moveDir);

        if (rangedEnemy.TryFlipOnObstacle(moveDir))
            moveDir = -moveDir;
    }

    public override void OnExit()
    {
        currentEnemy.anim.SetBool("walk", false);
    }
}
