using UnityEngine;

/// <summary>
/// 招式 C：与装甲车相同，瞄准→锁定连射 N 发，循环 3~5 波；全部打完后换弹硬直。
/// </summary>
public class AE74MachineGunState : BaseState
{
    enum Phase
    {
        Aim,
        Burst,
        Reload
    }

    AE74Enemy robot;
    Phase phase;
    int cyclesRemaining;
    int shotsRemaining;
    float timer;

    public override void OnEnter(Enemy enemy)
    {
        currentEnemy = enemy;
        robot = enemy as AE74Enemy;
        if (robot == null)
            return;

        robot.OnActionEntered(EnemyAction.Shot);
        robot.ClearActionAnims();
        robot.StopHorizontalMotion();
        currentEnemy.blockSeparation = true;
        currentEnemy.FacePlayer();
        robot.SetFacingLocked(true);
        cyclesRemaining = robot.RollMachineGunCycleCount();
        EnterAim();
    }

    public override void LogicUpdate()
    {
        if (robot == null || currentEnemy.isDead)
            return;

        robot.StopHorizontalMotion();

        switch (phase)
        {
            case Phase.Aim:
                UpdateAim();
                break;
            case Phase.Burst:
                UpdateBurst();
                break;
            case Phase.Reload:
                UpdateReload();
                break;
        }
    }

    public override void PhysicsUpdate()
    {
        if (robot == null || currentEnemy.isDead)
            return;

        robot.StopHorizontalMotion();
    }

    public override void OnExit()
    {
        if (robot == null)
            return;

        currentEnemy.blockSeparation = false;
        robot.SetFacingLocked(false);
        robot.RestoreGunPose();
        robot.SetAnimBool("shoot", false);
    }

    void EnterAim()
    {
        phase = Phase.Aim;
        timer = 0f;
        robot.BeginGunAim();
        if (robot.mgAimDuration <= 0f)
        {
            robot.SnapGunToPlayer();
            robot.LockFireDirection();
            robot.HoldLockedGun();
            EnterBurst();
        }
    }

    void UpdateAim()
    {
        timer += Time.deltaTime;
        float duration = Mathf.Max(0.0001f, robot.mgAimDuration);
        robot.AimGunAtPlayer(timer / duration);
        if (timer < robot.mgAimDuration)
            return;

        robot.SnapGunToPlayer();
        robot.LockFireDirection();
        robot.HoldLockedGun();
        EnterBurst();
    }

    void EnterBurst()
    {
        phase = Phase.Burst;
        shotsRemaining = robot.RollMachineGunBurstCount();
        timer = 0f;
        robot.PlayShootOnceAndHold();
        robot.HoldLockedGun();
        FireOne();
    }

    void UpdateBurst()
    {
        if (shotsRemaining <= 0)
        {
            FinishBurst();
            return;
        }

        timer += Time.deltaTime;
        if (timer < robot.mgFireInterval)
            return;

        timer = 0f;
        FireOne();
    }

    void FireOne()
    {
        robot.FireLockedProjectile();
        shotsRemaining--;
        if (shotsRemaining <= 0)
            FinishBurst();
    }

    void FinishBurst()
    {
        cyclesRemaining--;

        if (cyclesRemaining > 0)
            EnterAim();
        else
            EnterReload();
    }

    void EnterReload()
    {
        phase = Phase.Reload;
        timer = Mathf.Max(0f, robot.mgReloadStun);
        robot.PlayShootOnceAndHold();
    }

    void UpdateReload()
    {
        timer -= Time.deltaTime;
        if (timer <= 0f)
            robot.FinishActionAndRecover();
    }
}
