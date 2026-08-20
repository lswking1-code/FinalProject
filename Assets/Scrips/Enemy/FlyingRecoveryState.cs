using UnityEngine;

/// <summary>
/// 飞行敌人射击后摇：原地停留 recoveryDuration，结束后重新进入循环。
/// </summary>
public class FlyingRecoveryState : BaseState
{
    FlyingEnemy flyingEnemy;
    float recoveryTimer;

    public override void OnEnter(Enemy enemy)
    {
        currentEnemy = enemy;
        flyingEnemy = enemy as FlyingEnemy;

        if (flyingEnemy == null)
            return;

        flyingEnemy.FacePlayer();
        recoveryTimer = flyingEnemy.recoveryDuration;

        if (currentEnemy.anim != null)
        {
            currentEnemy.SetAnimBool("shoot", false);
            currentEnemy.SetAnimBool("shootDown", false);
            currentEnemy.SetAnimBool("walk", false);
        }

        flyingEnemy.StopHorizontalMotion();
    }

    public override void LogicUpdate()
    {
        if (flyingEnemy == null || currentEnemy.isDead)
            return;

        recoveryTimer -= Time.deltaTime;
        if (recoveryTimer <= 0f)
            flyingEnemy.EvaluateCycle();
    }

    public override void PhysicsUpdate()
    {
        if (flyingEnemy == null || currentEnemy.isHurt || currentEnemy.isDead)
            return;

        flyingEnemy.StopHorizontalMotion();
    }

    public override void OnExit() { }
}
