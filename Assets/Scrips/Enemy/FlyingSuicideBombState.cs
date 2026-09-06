using UnityEngine;

/// <summary>
/// 飞行敌人自爆：进入时锁定玩家位置，预警接近该点，停下延迟后爆炸并自毁。
/// </summary>
public class FlyingSuicideBombState : BaseState
{
    enum Phase
    {
        Approach,
        Fuse,
        Bomb
    }

    const float BombAnimFallbackDuration = 1.25f;
    const string BombStateName = "Bomb";

    FlyingEnemy flyingEnemy;
    Phase phase;
    float fuseTimer;
    float bombFallbackTimer;

    public override void OnEnter(Enemy enemy)
    {
        currentEnemy = enemy;
        flyingEnemy = enemy as FlyingEnemy;
        if (flyingEnemy == null)
            return;

        flyingEnemy.BeginSuicideAttack();
        flyingEnemy.CacheSuicideLockPoint();
        flyingEnemy.StopHorizontalMotion();
        flyingEnemy.FaceSuicideLockPoint();

        phase = Phase.Approach;
        fuseTimer = 0f;
        bombFallbackTimer = BombAnimFallbackDuration;

        if (currentEnemy.anim == null)
            return;

        currentEnemy.SetAnimBool("walk", false);
        currentEnemy.SetAnimBool("shoot", false);
        currentEnemy.SetAnimBool("shootDown", false);
        currentEnemy.SetAnimBool("bomb", false);
        currentEnemy.SetAnimBool("warning", true);
    }

    public override void LogicUpdate()
    {
        if (flyingEnemy == null)
            return;

        if (currentEnemy.isDead && !flyingEnemy.IsSuicideDetonating)
            return;

        switch (phase)
        {
            case Phase.Approach:
                if (flyingEnemy.IsInSuicideDetonateRange())
                    EnterFuse();
                break;
            case Phase.Fuse:
                fuseTimer -= Time.deltaTime;
                if (fuseTimer <= 0f)
                    EnterBomb();
                break;
            case Phase.Bomb:
                bombFallbackTimer -= Time.deltaTime;
                if (currentEnemy.IsNamedAnimFinished(BombStateName) || bombFallbackTimer <= 0f)
                    currentEnemy.DestroyAfterAnimation();
                break;
        }
    }

    public override void PhysicsUpdate()
    {
        if (flyingEnemy == null)
            return;

        if (currentEnemy.isDead || currentEnemy.isHurt || phase != Phase.Approach)
        {
            flyingEnemy.StopHorizontalMotion();
            return;
        }

        flyingEnemy.MoveTowardSuicideLockPoint(flyingEnemy.GetBombApproachSpeed());
    }

    public override void OnExit()
    {
        flyingEnemy?.EndSuicideAttack();

        if (currentEnemy?.anim == null)
            return;

        currentEnemy.SetAnimBool("warning", false);
        if (flyingEnemy == null || !flyingEnemy.IsSuicideDetonating)
            currentEnemy.SetAnimBool("bomb", false);
    }

    void EnterFuse()
    {
        phase = Phase.Fuse;
        fuseTimer = flyingEnemy.BombFuseDelay;
        flyingEnemy.StopHorizontalMotion();
        flyingEnemy.FaceSuicideLockPoint();
    }

    void EnterBomb()
    {
        phase = Phase.Bomb;
        bombFallbackTimer = BombAnimFallbackDuration;
        flyingEnemy.StopHorizontalMotion();
        flyingEnemy.BeginSuicideDetonation();
    }
}
