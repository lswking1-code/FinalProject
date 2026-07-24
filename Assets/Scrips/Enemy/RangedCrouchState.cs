using UnityEngine;

/// <summary>
/// 远程敌人蹲伏状态：站桩 actionDuration 秒，不射击，结束后重新选择行为。
/// </summary>
public class RangedCrouchState : BaseState
{
    RangedEnemy rangedEnemy;
    float actionTimer;

    public override void OnEnter(Enemy enemy)
    {
        currentEnemy = enemy;
        rangedEnemy = enemy as RangedEnemy;

        if (rangedEnemy == null)
            return;

        rangedEnemy.OnActionEntered(EnemyAction.Crouch);
        rangedEnemy.FacePlayer();
        actionTimer = rangedEnemy.actionDuration;
        currentEnemy.anim.SetBool("crouch", true);
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
        if (currentEnemy.isHurt || currentEnemy.isDead || currentEnemy.Rb == null)
            return;

        Vector2 vel = currentEnemy.Rb.linearVelocity;
        vel.x = 0f;
        currentEnemy.Rb.linearVelocity = vel;
    }

    public override void OnExit()
    {
        currentEnemy.anim.SetBool("crouch", false);
    }
}
