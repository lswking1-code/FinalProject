using UnityEngine;

/// <summary>
/// 远程敌人脱战后返回出生点，抵达后重新进入站岗 Idle。
/// </summary>
public class RangedReturnHomeState : BaseState
{
    RangedEnemy rangedEnemy;

    public override void OnEnter(Enemy enemy)
    {
        currentEnemy = enemy;
        rangedEnemy = enemy as RangedEnemy;
        currentEnemy.isAggro = false;
        currentEnemy.currentSpeed = currentEnemy.normalSpeed > 0f
            ? currentEnemy.normalSpeed
            : currentEnemy.chaseSpeed;

        if (rangedEnemy != null)
            rangedEnemy.lastAction = null;

        if (currentEnemy.anim != null)
        {
            currentEnemy.SetAnimBool("shoot", false);
            currentEnemy.SetAnimBool("shotPrep", false);
            currentEnemy.SetAnimBool("crouch", false);
            currentEnemy.SetAnimBool("reload", false);
            currentEnemy.SetAnimBool("walk", true);
        }
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
        if (rangedEnemy == null || currentEnemy.isHurt || currentEnemy.isDead)
            return;

        float dx = currentEnemy.homePosition.x - currentEnemy.transform.position.x;
        if (Mathf.Abs(dx) <= currentEnemy.returnArriveDistance)
            return;

        float dir = Mathf.Sign(dx);
        if (dir == 0f)
            return;

        rangedEnemy.MoveHorizontal(dir);
        rangedEnemy.TryFlipOnObstacle(dir);
        currentEnemy.FaceDirection(dir);
    }

    public override void OnExit()
    {
        if (currentEnemy?.anim != null)
            currentEnemy.SetAnimBool("walk", false);
    }
}
