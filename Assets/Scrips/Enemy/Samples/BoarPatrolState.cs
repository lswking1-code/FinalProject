/// <summary>
/// 野猪巡逻状态：沿当前方向行走，发现玩家后切换为追击。
/// </summary>
public class BoarPatrolState : BaseState
{
    public override void OnEnter(Enemy enemy)
    {
        currentEnemy = enemy;
        currentEnemy.currentSpeed = currentEnemy.normalSpeed;
    }

    public override void LogicUpdate()
    {
        if (currentEnemy.FoundPlayer())
            currentEnemy.SwitchState(NPCState.Chase);

        // 前方无实心地面或贴墙时停下等待转身（平台上不拦截，可走下去）
        if (currentEnemy.IsLedgeBlocking(currentEnemy.faceDir.x)
            || (currentEnemy.physicsCheck.touchLeftWall && currentEnemy.faceDir.x < 0)
            || (currentEnemy.physicsCheck.touchRightWall && currentEnemy.faceDir.x > 0))
        {
            currentEnemy.wait = true;
            currentEnemy.SetAnimBool("walk", false);
        }
        else
        {
            currentEnemy.SetAnimBool("walk", true);
        }
    }

    public override void PhysicsUpdate() { }

    public override void OnExit()
    {
        currentEnemy.SetAnimBool("walk", false);
    }
}
