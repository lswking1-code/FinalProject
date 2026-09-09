using UnityEngine;

/// <summary>
/// 招式 B：近距直接近战；否则 Dash_Start 发射两枚追踪导弹后二维冲刺（穿 Platform、开 Boost），到位再近战。
/// </summary>
public class AE74MeleeDashState : BaseState
{
    enum Phase
    {
        DashStart,
        Dash,
        Slash
    }

    AE74Enemy robot;
    Phase phase;
    float timer;
    Vector2 lockedTarget;
    bool missilesFired;

    public override void OnEnter(Enemy enemy)
    {
        currentEnemy = enemy;
        robot = enemy as AE74Enemy;
        if (robot == null)
            return;

        robot.OnActionEntered(EnemyAction.MeleeAttack);
        robot.ClearActionAnims();
        robot.SetMeleeAttackerActive(false);
        currentEnemy.blockSeparation = true;
        LockTargetFromPlayer();
        currentEnemy.ApplyFacing(lockedTarget.x - currentEnemy.transform.position.x);

        if (robot.IsPlayerInMeleeRange())
            EnterSlash();
        else
            EnterDashStart();
    }

    public override void LogicUpdate()
    {
        if (robot == null || currentEnemy.isDead)
            return;

        switch (phase)
        {
            case Phase.DashStart:
                UpdateDashStart();
                break;
            case Phase.Dash:
                UpdateDash();
                break;
            case Phase.Slash:
                UpdateSlash();
                break;
        }
    }

    public override void PhysicsUpdate()
    {
        if (robot == null || currentEnemy.isHurt || currentEnemy.isDead || currentEnemy.Rb == null)
            return;

        if (phase != Phase.Dash)
        {
            if (phase == Phase.DashStart)
                robot.StopHorizontalMotion();
            else
                robot.StopAllMotion();
            return;
        }

        robot.UpdateDashPlatformPassThrough();

        float dir = GetMoveDirTowardLocked();
        if (IsDashBlocked(dir))
        {
            EndDashFlight();
            EnterSlash();
            return;
        }

        currentEnemy.ApplyFacing(dir);
        robot.MoveKinematicToward(lockedTarget, robot.dashSpeed);
    }

    public override void OnExit()
    {
        if (robot == null)
            return;

        currentEnemy.blockSeparation = false;
        EndDashFlight();
        robot.SetMeleeAttackerActive(false);
        robot.SetAnimBool("dashStart", false);
        robot.SetAnimBool("dash", false);
        robot.SetAnimBool("melee", false);
    }

    void EnterDashStart()
    {
        phase = Phase.DashStart;
        missilesFired = false;
        robot.SetAnimBool("melee", false);
        robot.SetAnimBool("dash", false);
        robot.SetAnimBool("dashStart", true);
        robot.SetBoostActive(true);
        timer = Mathf.Max(0.05f, robot.dashStartDuration);
        FireMissilesOnce();
    }

    void UpdateDashStart()
    {
        FireMissilesOnce();
        timer -= Time.deltaTime;
        if (timer > 0f)
            return;

        EnterDash();
    }

    void EnterDash()
    {
        phase = Phase.Dash;
        timer = Mathf.Max(0.05f, robot.dashTimeout);
        robot.SetAnimBool("dashStart", false);
        robot.SetAnimBool("dash", true);
        robot.SetBoostActive(true);
        robot.SetFlightPhysics(true);
        // 冲刺目标只在起步时锁一次，之后不再跟踪玩家
        LockTargetFromPlayer();
    }

    void UpdateDash()
    {
        timer -= Time.deltaTime;
        if (timer <= 0f || HasArrivedForMelee())
        {
            EndDashFlight();
            EnterSlash();
        }
    }

    void EnterSlash()
    {
        phase = Phase.Slash;
        EndDashFlight();
        robot.StopAllMotion();
        currentEnemy.ApplyFacing(lockedTarget.x - currentEnemy.transform.position.x);
        robot.SetAnimBool("dashStart", false);
        robot.SetAnimBool("dash", false);
        robot.SetAnimBool("melee", true);
        robot.PlayMeleeAttackSfx();
    }

    void UpdateSlash()
    {
        var anim = currentEnemy.anim;
        if (anim == null)
        {
            robot.FinishActionAndRecover();
            return;
        }

        var info = anim.GetCurrentAnimatorStateInfo(0);
        if (!info.IsName(robot.meleeStateName))
            return;

        robot.SyncMeleeHitbox(info.normalizedTime);
        if (info.normalizedTime >= 1f)
        {
            robot.SetMeleeAttackerActive(false);
            robot.FinishActionAndRecover();
        }
    }

    void EndDashFlight()
    {
        robot.SetBoostActive(false);
        robot.RestoreGroundPhysics();
        robot.RestoreStompPlatformIgnores();
    }

    void FireMissilesOnce()
    {
        if (missilesFired)
            return;

        missilesFired = true;
        robot.FireDashHomingMissiles();
    }

    void LockTargetFromPlayer()
    {
        lockedTarget = robot != null ? robot.GetCombatAimPoint() : (Vector2)currentEnemy.transform.position;
    }


    bool HasArrivedForMelee()
    {
        return robot.HasArrivedAt(lockedTarget, robot.dashArriveDistance);
    }

    float GetMoveDirTowardLocked()
    {
        float dir = Mathf.Sign(lockedTarget.x - currentEnemy.transform.position.x);
        return dir == 0f ? currentEnemy.faceDir.x : dir;
    }

    bool IsDashBlocked(float dir)
    {
        if (robot.IsWallInDirection(dir))
            return true;

        var check = currentEnemy.physicsCheck;
        if (check == null)
            return false;

        return (check.touchLeftWall && dir < 0f) || (check.touchRightWall && dir > 0f);
    }
}
