using UnityEngine;

/// <summary>
/// 远程敌人换弹冷却：射击类行为结束后进入，结束后再重新选择行为。
/// </summary>
public class RangedReloadState : BaseState
{
    RangedEnemy rangedEnemy;
    float reloadTimer;

    public override void OnEnter(Enemy enemy)
    {
        currentEnemy = enemy;
        rangedEnemy = enemy as RangedEnemy;

        if (rangedEnemy == null)
            return;

        rangedEnemy.FacePlayer();
        reloadTimer = rangedEnemy.reloadDuration;

        currentEnemy.SetAnimBool("shoot", false);
        currentEnemy.SetAnimBool("shotPrep", false);
        currentEnemy.SetAnimBool("crouch", false);
        currentEnemy.SetAnimBool("walk", false);
        currentEnemy.SetAnimBool("reload", true);
    }

    public override void LogicUpdate()
    {
        if (rangedEnemy == null || currentEnemy.isDead)
            return;

        reloadTimer -= Time.deltaTime;

        if (reloadTimer <= 0f)
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
        currentEnemy.SetAnimBool("reload", false);
    }
}
