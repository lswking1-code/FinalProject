using UnityEngine;

/// <summary>
/// 装甲车脱战后返回出生点，抵达后重新进入站岗 Idle。回位时不转身。
/// </summary>
public class ArmoredVehicleReturnHomeState : BaseState
{
    ArmoredVehicleEnemy vehicle;

    public override void OnEnter(Enemy enemy)
    {
        currentEnemy = enemy;
        vehicle = enemy as ArmoredVehicleEnemy;
        currentEnemy.isAggro = false;
        currentEnemy.currentSpeed = currentEnemy.normalSpeed > 0f
            ? currentEnemy.normalSpeed
            : currentEnemy.chaseSpeed;

        if (vehicle != null)
        {
            vehicle.lastAction = null;
            vehicle.SetBumpersActive(false);
            vehicle.SetAnimBool("shoot", false);
            vehicle.SetAnimBool("missile", false);
            vehicle.SetAnimBool("ram", false);
            vehicle.SetAnimBool("ramWindup", false);
            vehicle.SetAnimBool("walk", true);
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
        if (vehicle == null || currentEnemy.isHurt || currentEnemy.isDead)
            return;

        float dx = currentEnemy.homePosition.x - currentEnemy.transform.position.x;
        if (Mathf.Abs(dx) <= currentEnemy.returnArriveDistance)
            return;

        float dir = Mathf.Sign(dx);
        if (dir == 0f)
            return;

        if (vehicle.IsWallInDirection(dir) || currentEnemy.IsLedgeBlocking(dir))
        {
            vehicle.StopHorizontalMotion();
            return;
        }

        vehicle.MoveHorizontal(dir);
    }

    public override void OnExit()
    {
        if (vehicle != null)
            vehicle.SetAnimBool("walk", false);
    }
}
