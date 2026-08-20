using UnityEngine;

/// <summary>
/// 手雷敌人跃起状态：随机左右起跳，经历 Jump → Fall → Land 后回到权重循环。
/// </summary>
public class GrenadeJumpState : BaseState
{
    const float MinAirTime = 0.05f;

    enum Phase
    {
        Rising,
        Falling,
        Landing
    }

    GrenadeEnemy grenadeEnemy;
    Phase phase;
    float landTimer;
    float airTimer;
    bool leftGround;
    float jumpDir;

    public override void OnEnter(Enemy enemy)
    {
        currentEnemy = enemy;
        grenadeEnemy = enemy as GrenadeEnemy;

        if (grenadeEnemy == null)
            return;

        grenadeEnemy.OnActionEntered(EnemyAction.Jump);

        jumpDir = Random.value < 0.5f ? -1f : 1f;
        FaceJumpDirection(jumpDir);

        ClearLocomotionBools();
        currentEnemy.SetAnimBool("jump", true);

        phase = Phase.Rising;
        leftGround = false;
        airTimer = 0f;
        landTimer = 0f;

        ApplyJumpVelocity();
    }

    public override void LogicUpdate()
    {
        if (grenadeEnemy == null || currentEnemy.isDead)
            return;

        if (currentEnemy.isHurt)
            return;

        if (phase != Phase.Landing)
            airTimer += Time.deltaTime;

        switch (phase)
        {
            case Phase.Rising:
                UpdateRising();
                break;
            case Phase.Falling:
                UpdateFalling();
                break;
            case Phase.Landing:
                UpdateLanding();
                break;
        }
    }

    public override void PhysicsUpdate()
    {
        if (grenadeEnemy == null || currentEnemy.isHurt || currentEnemy.isDead || currentEnemy.Rb == null)
            return;

        if (phase == Phase.Landing)
        {
            Vector2 vel = currentEnemy.Rb.linearVelocity;
            vel.x = 0f;
            currentEnemy.Rb.linearVelocity = vel;
        }
    }

    public override void OnExit()
    {
        ClearAirBools();
    }

    void UpdateRising()
    {
        TrackLeftGround();

        if (currentEnemy.Rb != null && currentEnemy.Rb.linearVelocity.y < 0f)
            EnterFalling();
        else if (CanLand())
            EnterLanding();
    }

    void UpdateFalling()
    {
        TrackLeftGround();

        if (CanLand())
            EnterLanding();
    }

    void UpdateLanding()
    {
        landTimer -= Time.deltaTime;
        if (landTimer > 0f)
            return;

        currentEnemy.SetAnimBool("land", false);
        grenadeEnemy.EvaluateCycle();
    }

    void EnterFalling()
    {
        phase = Phase.Falling;
        currentEnemy.SetAnimBool("jump", false);
        currentEnemy.SetAnimBool("fall", true);
    }

    void EnterLanding()
    {
        phase = Phase.Landing;
        landTimer = grenadeEnemy.landDuration;

        currentEnemy.SetAnimBool("jump", false);
        currentEnemy.SetAnimBool("fall", false);
        currentEnemy.SetAnimBool("land", true);

        if (currentEnemy.Rb != null)
        {
            Vector2 vel = currentEnemy.Rb.linearVelocity;
            vel.x = 0f;
            currentEnemy.Rb.linearVelocity = vel;
        }
    }

    void ApplyJumpVelocity()
    {
        if (currentEnemy.Rb == null)
            return;

        float gravity = Mathf.Abs(Physics2D.gravity.y * currentEnemy.Rb.gravityScale);
        if (gravity < 0.01f)
            gravity = Mathf.Abs(Physics2D.gravity.y);

        float jumpVelocity = Mathf.Sqrt(2f * gravity * Mathf.Max(0.01f, grenadeEnemy.jumpHeight));
        currentEnemy.Rb.linearVelocity = new Vector2(
            jumpDir * grenadeEnemy.jumpHorizontalSpeed,
            jumpVelocity);
    }

    void TrackLeftGround()
    {
        if (!IsGrounded())
            leftGround = true;
    }

    bool CanLand()
    {
        if (!leftGround || !IsGrounded() || airTimer < MinAirTime)
            return false;

        return currentEnemy.Rb == null || currentEnemy.Rb.linearVelocity.y <= 0f;
    }

    bool IsGrounded()
    {
        return currentEnemy.physicsCheck != null && currentEnemy.physicsCheck.isGround;
    }

    void FaceJumpDirection(float dir)
    {
        currentEnemy.FaceDirection(dir);
    }

    void ClearLocomotionBools()
    {
        currentEnemy.SetAnimBool("walk", false);
        currentEnemy.SetAnimBool("throw", false);
        currentEnemy.SetAnimBool("fall", false);
        currentEnemy.SetAnimBool("land", false);
    }

    void ClearAirBools()
    {
        currentEnemy.SetAnimBool("jump", false);
        currentEnemy.SetAnimBool("fall", false);
        currentEnemy.SetAnimBool("land", false);
    }
}
