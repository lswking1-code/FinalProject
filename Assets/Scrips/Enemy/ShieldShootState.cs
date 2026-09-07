using UnityEngine;

/// <summary>
/// 盾兵射击：播 shooting 动画，第 4 帧左右出弹；期间撤盾，正面可打本体。
/// 射击中本体受击只闪红，不进硬直、不打断射击。
/// </summary>
public class ShieldShootState : BaseState
{
    const float FailsafeDuration = 2f;

    ShieldEnemy shieldEnemy;
    bool hasFired;
    float failsafeTimer;

    public override void OnEnter(Enemy enemy)
    {
        currentEnemy = enemy;
        shieldEnemy = enemy as ShieldEnemy;
        hasFired = false;
        failsafeTimer = FailsafeDuration;

        if (shieldEnemy == null)
            return;

        currentEnemy.currentSpeed = 0f;
        currentEnemy.blockSeparation = true;
        if (currentEnemy.Rb != null)
            currentEnemy.Rb.linearVelocity = new Vector2(0f, currentEnemy.Rb.linearVelocity.y);

        currentEnemy.FacePlayer();
        currentEnemy.SetAnimBool("walk", false);
        currentEnemy.SetAnimBool("melee", false);
        currentEnemy.SetAnimBool("meleeWindup", false);
        currentEnemy.SetAnimBool("shoot", true);
        shieldEnemy.SetShieldWithdrawn(true);
        shieldEnemy.InterruptShieldHitForShoot();
    }

    public override void LogicUpdate()
    {
        if (shieldEnemy == null || currentEnemy.isDead)
            return;

        TryFireIfReady();

        failsafeTimer -= Time.deltaTime;
        if (failsafeTimer <= 0f || currentEnemy.IsNamedAnimFinished(shieldEnemy.shootStateName))
            FinishShoot();
    }

    public override void PhysicsUpdate()
    {
        if (currentEnemy == null || currentEnemy.isHurt || currentEnemy.isDead || currentEnemy.Rb == null)
            return;

        currentEnemy.Rb.linearVelocity = new Vector2(0f, currentEnemy.Rb.linearVelocity.y);
    }

    public override void OnExit()
    {
        if (currentEnemy != null)
        {
            currentEnemy.SetAnimBool("shoot", false);
            currentEnemy.blockSeparation = false;
        }

        if (shieldEnemy != null)
        {
            shieldEnemy.SetShieldWithdrawn(false);
            shieldEnemy.BeginShootCooldown();
        }

        hasFired = false;
        failsafeTimer = 0f;
    }

    void TryFireIfReady()
    {
        if (hasFired)
            return;

        var anim = currentEnemy.anim;
        if (anim == null || anim.runtimeAnimatorController == null)
            return;

        AnimatorStateInfo info = anim.GetCurrentAnimatorStateInfo(0);
        if (!info.IsName(shieldEnemy.shootStateName))
            return;

        if (info.normalizedTime < shieldEnemy.fireNormalizedTime)
            return;

        hasFired = true;
        shieldEnemy.FireProjectile();
    }

    void FinishShoot()
    {
        shieldEnemy.SwitchState(NPCState.Skill);
    }
}
