using UnityEngine;

/// <summary>
/// 远程敌人射击状态：进入时开一枪，随后立刻进入 Reload。
/// </summary>
public class RangedShotState : BaseState
{
    RangedEnemy rangedEnemy;

    public override void OnEnter(Enemy enemy)
    {
        currentEnemy = enemy;
        rangedEnemy = enemy as RangedEnemy;

        if (rangedEnemy == null)
            return;

        rangedEnemy.OnActionEntered(EnemyAction.Shot);
        rangedEnemy.FacePlayer();

        currentEnemy.SetAnimBool("shoot", true);
        rangedEnemy.FireProjectile();
    }

    public override void LogicUpdate()
    {
        if (rangedEnemy == null || currentEnemy.isDead)
            return;

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
        currentEnemy.SetAnimBool("shoot", false);
    }
}
