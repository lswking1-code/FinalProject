using UnityEngine;

/// <summary>
/// 远程敌人蹲射状态：蹲姿持续射击，结束后进入 Reload 冷却。
/// </summary>
public class RangedCrouchShootState : BaseState
{
    RangedEnemy rangedEnemy;
    float actionTimer;
    float fireTimer;

    public override void OnEnter(Enemy enemy)
    {
        currentEnemy = enemy;
        rangedEnemy = enemy as RangedEnemy;

        if (rangedEnemy == null)
            return;

        rangedEnemy.OnActionEntered(EnemyAction.CrouchShoot);
        rangedEnemy.FacePlayer();

        actionTimer = rangedEnemy.actionDuration;
        fireTimer = 0f;

        currentEnemy.anim.SetBool("crouch", true);
        currentEnemy.anim.SetBool("shoot", true);
        rangedEnemy.FireProjectile();
    }

    public override void LogicUpdate()
    {
        if (rangedEnemy == null || currentEnemy.isDead)
            return;

        actionTimer -= Time.deltaTime;
        fireTimer -= Time.deltaTime;

        if (fireTimer <= 0f)
        {
            rangedEnemy.FireProjectile();
            fireTimer = rangedEnemy.fireInterval;
        }

        if (actionTimer <= 0f)
            rangedEnemy.SwitchState(NPCState.Reload);
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
        currentEnemy.anim.SetBool("shoot", false);
        currentEnemy.anim.SetBool("crouch", false);
    }
}
