using UnityEngine;

/// <summary>
/// AE-74 脱战后返回出生点。
/// </summary>
public class AE74ReturnHomeState : BaseState
{
    AE74Enemy robot;

    public override void OnEnter(Enemy enemy)
    {
        currentEnemy = enemy;
        robot = enemy as AE74Enemy;
        currentEnemy.isAggro = false;
        currentEnemy.currentSpeed = currentEnemy.GetReturnHomeSpeed();

        if (robot == null)
            return;

        robot.lastAction = null;
        robot.RestoreGroundPhysics();
        robot.RestoreStompPlatformIgnores();
        robot.ClearActionAnims();
        robot.SetAnimBool("walk", true);
        robot.SetBoostActive(false);
    }

    public override void LogicUpdate()
    {
        if (currentEnemy == null || currentEnemy.isDead)
            return;

        float dx = currentEnemy.homePosition.x - currentEnemy.transform.position.x;
        if (Mathf.Abs(dx) <= currentEnemy.returnArriveDistance)
            currentEnemy.FinishPatrolReset();
    }

    public override void PhysicsUpdate()
    {
        if (robot == null || currentEnemy.isHurt || currentEnemy.isDead)
            return;

        float dx = currentEnemy.homePosition.x - currentEnemy.transform.position.x;
        if (Mathf.Abs(dx) <= currentEnemy.returnArriveDistance)
            return;

        float dir = Mathf.Sign(dx);
        if (dir == 0f)
            return;

        if (robot.IsWallInDirection(dir) || currentEnemy.IsLedgeBlocking(dir))
        {
            robot.StopHorizontalMotion();
            return;
        }

        currentEnemy.FaceDirection(dir);
        currentEnemy.MoveHorizontal(dir);
    }

    public override void OnExit()
    {
        if (robot != null)
            robot.SetAnimBool("walk", false);
    }
}
