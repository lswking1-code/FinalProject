using UnityEngine;

/// <summary>
/// 招式 A：Dash_Start 前摇后，从两个专用开火点按随机顺序水平连射两发火箭弹。
/// </summary>
public class AE74CannonState : BaseState
{
    AE74Enemy robot;
    float timer;
    int shotsFired;
    Transform firstPoint;
    Transform secondPoint;

    public override void OnEnter(Enemy enemy)
    {
        currentEnemy = enemy;
        robot = enemy as AE74Enemy;
        if (robot == null)
            return;

        robot.OnActionEntered(EnemyAction.Missile);
        robot.ClearActionAnims();
        robot.StopHorizontalMotion();
        currentEnemy.blockSeparation = true;
        currentEnemy.FacePlayer();
        robot.SetAnimBool("dashStart", true);

        robot.GetCannonFirePoints(out firstPoint, out secondPoint);
        if (Random.value < 0.5f)
        {
            Transform swap = firstPoint;
            firstPoint = secondPoint;
            secondPoint = swap;
        }

        timer = Mathf.Max(0.01f, robot.cannonWindup);
        shotsFired = 0;
    }

    public override void LogicUpdate()
    {
        if (robot == null || currentEnemy.isDead)
            return;

        robot.StopHorizontalMotion();
        timer -= Time.deltaTime;
        if (timer > 0f)
            return;

        if (shotsFired == 0)
        {
            robot.FireCannonMissileFrom(firstPoint);
            shotsFired = 1;
            timer = Mathf.Max(0f, robot.cannonBurstInterval);
            if (timer > 0f)
                return;
        }

        robot.FireCannonMissileFrom(secondPoint);
        shotsFired = 2;
        robot.FinishActionAndRecover();
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
        robot.SetAnimBool("dashStart", false);
    }
}
