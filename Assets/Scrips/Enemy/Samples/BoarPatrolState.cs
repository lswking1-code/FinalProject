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

        // 前方无地面或贴墙时停下等待转身（前瞻边缘，避免走出平台后才反应）
        if (currentEnemy.IsLedgeBlocking(currentEnemy.faceDir.x)
            || (currentEnemy.physicsCheck.touchLeftWall && currentEnemy.faceDir.x < 0)
            || (currentEnemy.physicsCheck.touchRightWall && currentEnemy.faceDir.x > 0))
        {
            currentEnemy.wait = true;
            currentEnemy.anim.SetBool("walk", false);
        }
        else
        {
            currentEnemy.anim.SetBool("walk", true);
        }
    }

    public override void PhysicsUpdate() { }

    public override void OnExit()
    {
        currentEnemy.anim.SetBool("walk", false);
    }
}
