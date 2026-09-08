using UnityEngine;

/// <summary>
/// 招式 D：45° 起跳飞行 → 空中多波机枪 → 原地悬停 N 秒 → 飞到落点上方践踏。
/// </summary>
public class AE74TacticalJumpState : BaseState
{
    enum Phase
    {
        Takeoff,
        FlyToApex,
        AirAim,
        AirBurst,
        PostShootHold,
        FlyToHover,
        Drop,
        Impact
    }

    AE74Enemy robot;
    Phase phase;
    float timer;
    int cyclesRemaining;
    int shotsRemaining;
    Vector2 apexTarget;
    Vector2 hoverTarget;
    bool descendingToHover;

    public override void OnEnter(Enemy enemy)
    {
        currentEnemy = enemy;
        robot = enemy as AE74Enemy;
        if (robot == null)
            return;

        robot.OnActionEntered(EnemyAction.Jump);
        robot.ClearActionAnims();
        robot.SetMeleeAttackerActive(false);
        robot.SetStompHitboxActive(false);
        currentEnemy.blockSeparation = true;
        currentEnemy.FacePlayer();

        apexTarget = robot.ComputeDiagonalJumpTarget();
        currentEnemy.ApplyFacing(apexTarget.x - currentEnemy.transform.position.x);

        robot.SetFlightPhysics(true);
        robot.SetAnimBool("jump", true);
        phase = Phase.Takeoff;
        timer = Mathf.Max(0.05f, robot.jumpTakeoffDuration);
    }

    public override void LogicUpdate()
    {
        if (robot == null || currentEnemy.isDead)
            return;

        switch (phase)
        {
            case Phase.Takeoff:
                UpdateTakeoff();
                break;
            case Phase.FlyToApex:
                UpdateFlyToApex();
                break;
            case Phase.AirAim:
                UpdateAirAim();
                break;
            case Phase.AirBurst:
                UpdateAirBurst();
                break;
            case Phase.PostShootHold:
                UpdatePostShootHold();
                break;
            case Phase.FlyToHover:
                if (descendingToHover)
                    robot.UpdateStompPlatformPassThrough();
                UpdateFlyToHover();
                break;
            case Phase.Drop:
                robot.UpdateStompPlatformPassThrough();
                UpdateDrop();
                break;
            case Phase.Impact:
                UpdateImpact();
                break;
        }
    }

    public override void PhysicsUpdate()
    {
        if (robot == null || currentEnemy.isDead)
            return;

        switch (phase)
        {
            case Phase.FlyToApex:
                robot.MoveKinematicToward(apexTarget, robot.flySpeed);
                break;
            case Phase.FlyToHover:
                if (descendingToHover)
                    robot.UpdateStompPlatformPassThrough();
                robot.MoveKinematicToward(hoverTarget, robot.flySpeed);
                break;
            case Phase.Drop:
                robot.UpdateStompPlatformPassThrough();
                robot.BeginStompFall();
                break;
            default:
                robot.StopAllMotion();
                break;
        }
    }

    public override void OnExit()
    {
        if (robot == null)
            return;

        currentEnemy.blockSeparation = false;
        robot.SetFacingLocked(false);
        robot.RestoreGunPose();
        robot.SetBoostActive(false);
        robot.SetStompHitboxActive(false);
        robot.SetMeleeAttackerActive(false);
        robot.RestoreStompPlatformIgnores();
        robot.RestoreGroundPhysics();
        robot.ClearActionAnims();
    }

    void UpdateTakeoff()
    {
        timer -= Time.deltaTime;
        if (timer > 0f)
            return;

        robot.SetAnimBool("jump", false);
        robot.SetAnimBool("fly", true);
        robot.SetBoostActive(true);
        phase = Phase.FlyToApex;
    }

    void UpdateFlyToApex()
    {
        if (!robot.HasArrivedAt(apexTarget, 0.2f))
            return;

        robot.SetBoostActive(true);
        robot.SetAnimBool("fly", false);
        robot.SetAnimBool("airAttack", true);
        currentEnemy.FacePlayer();
        robot.SetFacingLocked(true);
        cyclesRemaining = robot.RollAirCycleCount();
        EnterAirAim();
    }

    void EnterAirAim()
    {
        phase = Phase.AirAim;
        timer = 0f;
        robot.BeginGunAim();
        if (robot.airAimDuration <= 0f)
        {
            robot.SnapGunToPlayer();
            robot.LockFireDirection();
            robot.HoldLockedGun();
            EnterAirBurst();
        }
    }

    void UpdateAirAim()
    {
        timer += Time.deltaTime;
        float duration = Mathf.Max(0.0001f, robot.airAimDuration);
        robot.AimGunAtPlayer(timer / duration);
        if (timer < robot.airAimDuration)
            return;

        robot.SnapGunToPlayer();
        robot.LockFireDirection();
        robot.HoldLockedGun();
        EnterAirBurst();
    }

    void EnterAirBurst()
    {
        phase = Phase.AirBurst;
        shotsRemaining = robot.RollAirBurstCount();
        timer = 0f;
        robot.HoldLockedGun();
        FireAirShot();
    }

    void UpdateAirBurst()
    {
        if (shotsRemaining <= 0)
        {
            FinishAirBurst();
            return;
        }

        timer += Time.deltaTime;
        if (timer < robot.airFireInterval)
            return;

        timer = 0f;
        FireAirShot();
    }

    void FireAirShot()
    {
        robot.FireAirScatter();
        shotsRemaining--;
        if (shotsRemaining <= 0)
            FinishAirBurst();
    }

    void FinishAirBurst()
    {
        cyclesRemaining--;
        if (cyclesRemaining > 0)
            EnterAirAim();
        else
            EnterPostShootHold();
    }

    void EnterPostShootHold()
    {
        robot.SetFacingLocked(false);
        robot.RestoreGunPose();
        robot.SetAnimBool("airAttack", false);
        robot.SetAnimBool("fly", true);
        robot.SetBoostActive(true);

        if (robot.airPostShootHold <= 0f)
        {
            BeginHoverTravel();
            return;
        }

        phase = Phase.PostShootHold;
        timer = robot.airPostShootHold;
    }

    void UpdatePostShootHold()
    {
        timer -= Time.deltaTime;
        if (timer > 0f)
            return;

        BeginHoverTravel();
    }

    void BeginHoverTravel()
    {
        robot.TryLockStompTarget(out hoverTarget);
        descendingToHover = hoverTarget.y < currentEnemy.transform.position.y - 0.1f;
        robot.SetFacingLocked(false);
        robot.RestoreGunPose();
        robot.SetAnimBool("airAttack", false);
        robot.SetAnimBool("fly", true);
        robot.SetBoostActive(true);
        currentEnemy.ApplyFacing(hoverTarget.x - currentEnemy.transform.position.x);
        phase = Phase.FlyToHover;
    }

    void UpdateFlyToHover()
    {
        if (!robot.HasArrivedAt(hoverTarget, 0.2f))
            return;

        robot.SetBoostActive(false);
        robot.SetAnimBool("fly", false);
        robot.SetAnimBool("landStart", true);
        robot.BeginStompFall();
        phase = Phase.Drop;
    }

    void UpdateDrop()
    {
        if (!robot.HasReachedStompLanding())
            return;

        robot.SnapOntoLanding();
        robot.RestoreStompPlatformIgnores();
        robot.RestoreGroundPhysics();
        robot.SetAnimBool("landStart", false);
        robot.SetAnimBool("landEnd", true);
        robot.SetStompHitboxActive(true);
        robot.SpawnShockwaves();
        timer = Mathf.Max(0.05f, robot.landImpactDuration);
        phase = Phase.Impact;
    }

    void UpdateImpact()
    {
        var anim = currentEnemy.anim;
        if (anim != null)
        {
            var info = anim.GetCurrentAnimatorStateInfo(0);
            if (info.IsName(robot.landEndStateName) && info.normalizedTime >= 1f)
            {
                Finish();
                return;
            }
        }

        timer -= Time.deltaTime;
        if (timer <= 0f)
            Finish();
    }

    void Finish()
    {
        robot.SetStompHitboxActive(false);
        robot.SetAnimBool("landEnd", false);
        robot.FinishActionAndRecover();
    }
}
