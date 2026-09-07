using UnityEngine;

/// <summary>
/// 装甲车缓慢前进或倒车，不翻转车体。普通移动不开启 Bumper，不伤人、不击退。
/// </summary>
public class ArmoredVehicleMoveState : BaseState
{
    ArmoredVehicleEnemy vehicle;
    float actionTimer;
    float moveDir;

    public override void OnEnter(Enemy enemy)
    {
        currentEnemy = enemy;
        vehicle = enemy as ArmoredVehicleEnemy;
        if (vehicle == null)
            return;

        vehicle.OnActionEntered(EnemyAction.Move);
        actionTimer = vehicle.actionDuration;
        moveDir = PickMoveDir();

        currentEnemy.currentSpeed = vehicle.moveSpeed > 0f ? vehicle.moveSpeed : currentEnemy.normalSpeed;
        vehicle.SetAnimBool("shoot", false);
        vehicle.SetAnimBool("missile", false);
        vehicle.SetAnimBool("ram", false);
        vehicle.SetAnimBool("ramWindup", false);
        vehicle.SetAnimBool("walk", true);
        vehicle.SetBumpersActive(false);
    }

    public override void LogicUpdate()
    {
        if (vehicle == null || currentEnemy.isDead)
            return;

        actionTimer -= Time.deltaTime;
        if (actionTimer <= 0f)
            vehicle.FinishActionAndRecover();
    }

    public override void PhysicsUpdate()
    {
        if (vehicle == null || currentEnemy.isHurt || currentEnemy.isDead)
            return;

        if (vehicle.IsWallInDirection(moveDir) || currentEnemy.IsLedgeBlocking(moveDir))
        {
            float reverse = -moveDir;
            if (!vehicle.IsWallInDirection(reverse) && currentEnemy.HasGroundAhead(reverse))
                moveDir = reverse;
            else
            {
                vehicle.StopHorizontalMotion();
                return;
            }
        }

        vehicle.MoveHorizontal(moveDir);
    }

    public override void OnExit()
    {
        if (vehicle == null)
            return;

        vehicle.SetAnimBool("walk", false);
        vehicle.SetBumpersActive(false);
        vehicle.StopHorizontalMotion();
    }

    float PickMoveDir()
    {
        float forward = vehicle.GetForwardSign();
        return Random.value < 0.5f ? forward : -forward;
    }
}
