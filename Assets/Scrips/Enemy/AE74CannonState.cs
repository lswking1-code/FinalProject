using UnityEngine;

/// <summary>
/// 招式 A：Dash_Start 前摇后水平发射一发火箭弹。
/// </summary>
public class AE74CannonState : BaseState
{
    AE74Enemy robot;
    float timer;
    bool fired;

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

        timer = Mathf.Max(0.01f, robot.cannonWindup);
        fired = false;
    }

    public override void LogicUpdate()
    {
        if (robot == null || currentEnemy.isDead)
            return;

        robot.StopHorizontalMotion();
        timer -= Time.deltaTime;
        if (timer > 0f || fired)
            return;

        fired = true;
        robot.FireCannonMissile();
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
