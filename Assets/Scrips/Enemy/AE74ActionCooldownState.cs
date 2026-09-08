using UnityEngine;

/// <summary>
/// 招式冷却：超时一律掷下一招；冷却中玩家超射程则 Run 接近。
/// </summary>
public class AE74ActionCooldownState : BaseState
{
    AE74Enemy robot;
    float timer;

    public override void OnEnter(Enemy enemy)
    {
        currentEnemy = enemy;
        robot = enemy as AE74Enemy;
        if (robot == null)
            return;

        timer = Mathf.Max(0f, robot.actionCooldown);
        robot.RestoreGroundPhysics();
        robot.RestoreStompPlatformIgnores();
        robot.ClearActionAnims();
        robot.SetMeleeAttackerActive(false);
        robot.SetStompHitboxActive(false);
        robot.SetBoostActive(false);
        currentEnemy.blockSeparation = false;
    }

    public override void LogicUpdate()
    {
        if (robot == null || currentEnemy.isDead)
            return;

        timer -= Time.deltaTime;
        if (timer <= 0f)
        {
            robot.EvaluateCycle();
            return;
        }

        bool chase = !currentEnemy.IsPlayerInCombatRange();
        robot.SetAnimBool("walk", chase);
        if (!chase)
            currentEnemy.currentSpeed = 0f;
    }

    public override void PhysicsUpdate()
    {
        if (robot == null || currentEnemy.isDead)
            return;

        if (currentEnemy.IsPlayerInCombatRange() || currentEnemy.player == null)
        {
            robot.StopHorizontalMotion();
            return;
        }

        float dir = currentEnemy.GetMoveDirTowardPlayer();
        if (robot.IsWallInDirection(dir) || currentEnemy.IsLedgeBlocking(dir))
        {
            robot.StopHorizontalMotion();
            return;
        }

        currentEnemy.currentSpeed = currentEnemy.normalSpeed > 0f ? currentEnemy.normalSpeed : robot.moveSpeed;
        currentEnemy.FaceDirection(dir);
        currentEnemy.MoveHorizontal(dir);
    }

    public override void OnExit()
    {
        if (robot != null)
            robot.SetAnimBool("walk", false);
    }
}
