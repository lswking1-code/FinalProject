using UnityEngine;

/// <summary>
/// 远程敌人蹲射：蹲下 → 与站立射击相同的预备 → 开火播 CrouchShoot → Reload。
/// </summary>
public class RangedCrouchShootState : BaseState
{
    enum Phase
    {
        CrouchIn,
        Prep,
        Fire,
        Hold
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

        rangedEnemy.OnActionEntered(EnemyAction.CrouchShoot);
        rangedEnemy.FacePlayer();
        EnterCrouchIn();
    }

    public override void LogicUpdate()
    {
        if (rangedEnemy == null || currentEnemy.isDead)
            return;

        if (phase == Phase.CrouchIn)
        {
            timer -= Time.deltaTime;
            if (timer <= 0f
                || currentEnemy.IsNamedAnimFinished(rangedEnemy.crouchStartStateName)
                || currentEnemy.IsNamedAnimPlaying("Crouch"))
                EnterPrep();
            return;
        }

        if (phase == Phase.Prep)
        {
            timer -= Time.deltaTime;
            if (timer <= 0f || currentEnemy.IsNamedAnimFinished(rangedEnemy.crouchShotPrepStateName))
                EnterFire();
            return;
        }

        if (phase == Phase.Hold)
        {
            timer -= Time.deltaTime;
            if (timer <= 0f || currentEnemy.IsNamedAnimFinished(rangedEnemy.crouchShootStateName))
                rangedEnemy.SwitchState(NPCState.Reload);
        }
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
        rangedEnemy?.SetCrouchPose(false);
        currentEnemy.SetAnimBool("shotPrep", false);
        currentEnemy.SetAnimBool("shoot", false);
        currentEnemy.SetAnimBool("crouch", false);
    }

    void EnterCrouchIn()
    {
        phase = Phase.CrouchIn;
        currentEnemy.SetAnimBool("walk", false);
        currentEnemy.SetAnimBool("shoot", false);
        currentEnemy.SetAnimBool("shotPrep", false);
        rangedEnemy.SetCrouchPose(true);
        currentEnemy.SetAnimBool("crouch", true);
        timer = Mathf.Max(0.01f, rangedEnemy.crouchStartDuration);
    }

    void EnterPrep()
    {
        phase = Phase.Prep;
        rangedEnemy.FacePlayer();
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
        phase = Phase.Hold;
        timer = Mathf.Max(0.01f, rangedEnemy.crouchShootHoldDuration);
    }
}
