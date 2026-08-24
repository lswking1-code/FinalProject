using UnityEngine;

/// <summary>
/// 远程敌人射击：先播预备动作，结束后开火，再进入 Reload。
/// </summary>
public class RangedShotState : BaseState
{
    enum Phase
    {
        Prep,
        Fire
    }

    RangedEnemy rangedEnemy;
    Phase phase;
    float timer;

    public override void OnEnter(Enemy enemy)
    {
        currentEnemy = enemy;
        rangedEnemy = enemy as RangedEnemy;

        if (rangedEnemy == null)
            return;

        rangedEnemy.OnActionEntered(EnemyAction.Shot);
        rangedEnemy.FacePlayer();
        EnterPrep();
    }

    public override void LogicUpdate()
    {
        if (rangedEnemy == null || currentEnemy.isDead)
            return;

        if (phase == Phase.Prep)
        {
            timer -= Time.deltaTime;
            if (timer <= 0f || currentEnemy.IsNamedAnimFinished(rangedEnemy.shotPrepStateName))
                EnterFire();
            return;
        }

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
        currentEnemy.SetAnimBool("shotPrep", false);
        currentEnemy.SetAnimBool("shoot", false);
    }

    void EnterPrep()
    {
        phase = Phase.Prep;
        currentEnemy.SetAnimBool("walk", false);
        currentEnemy.SetAnimBool("shoot", false);
        currentEnemy.SetAnimBool("shotPrep", true);
        timer = Mathf.Max(0.01f, rangedEnemy.shotPrepDuration);
    }

    void EnterFire()
    {
        phase = Phase.Fire;
        rangedEnemy.FacePlayer();
        currentEnemy.SetAnimBool("shotPrep", false);
        currentEnemy.SetAnimBool("shoot", true);
        rangedEnemy.FireProjectile();
    }
}
