using UnityEngine;

/// <summary>
/// 手雷敌人投掷状态：进入时投出一枚手雷，原地站立 actionDuration 秒。
/// </summary>
public class GrenadeThrowState : BaseState
{
    GrenadeEnemy grenadeEnemy;
    float actionTimer;

    public override void OnEnter(Enemy enemy)
    {
        currentEnemy = enemy;
        grenadeEnemy = enemy as GrenadeEnemy;

        if (grenadeEnemy == null)
            return;

        grenadeEnemy.OnActionEntered(EnemyAction.Shot);
        grenadeEnemy.FacePlayer();

        actionTimer = grenadeEnemy.actionDuration;

        currentEnemy.anim.SetBool("throw", true);
        grenadeEnemy.ThrowGrenade();
    }

    public override void LogicUpdate()
    {
        if (grenadeEnemy == null || currentEnemy.isDead)
            return;

        actionTimer -= Time.deltaTime;

        if (actionTimer <= 0f)
            grenadeEnemy.EvaluateCycle();
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
        currentEnemy.anim.SetBool("throw", false);
    }
}
