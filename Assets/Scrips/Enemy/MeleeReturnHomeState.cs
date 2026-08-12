using UnityEngine;

/// <summary>
/// 近战敌人脱战后返回出生点，抵达后重新进入站岗 Idle。
/// </summary>
public class MeleeReturnHomeState : BaseState
{
    MeleeEnemy meleeEnemy;

    public override void OnEnter(Enemy enemy)
    {
        currentEnemy = enemy;
        meleeEnemy = enemy as MeleeEnemy;
        currentEnemy.isAggro = false;
        currentEnemy.currentSpeed = currentEnemy.normalSpeed > 0f
            ? currentEnemy.normalSpeed
            : currentEnemy.chaseSpeed;

        if (currentEnemy.anim != null)
        {
            currentEnemy.anim.SetBool("melee", false);
            currentEnemy.anim.SetBool("meleeWindup", false);
            currentEnemy.anim.SetBool("walk", true);
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
        if (meleeEnemy == null || currentEnemy.isHurt || currentEnemy.isDead)
            return;

        float dx = currentEnemy.homePosition.x - currentEnemy.transform.position.x;
        if (Mathf.Abs(dx) <= currentEnemy.returnArriveDistance)
            return;

        float dir = Mathf.Sign(dx);
        if (dir == 0f)
            return;

        meleeEnemy.MoveHorizontal(dir);
        meleeEnemy.TryFlipOnObstacle(dir);
        currentEnemy.FaceDirection(dir);
    }

    public override void OnExit()
    {
        if (currentEnemy?.anim != null)
            currentEnemy.anim.SetBool("walk", false);
    }
}
