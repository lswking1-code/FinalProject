using UnityEngine;

/// <summary>
/// 野猪追击状态：以追击速度追踪玩家，丢失目标后返回巡逻。
/// </summary>
public class BoarChaseState : BaseState
{
    public override void OnEnter(Enemy enemy)
    {
        currentEnemy = enemy;
        currentEnemy.currentSpeed = currentEnemy.chaseSpeed;
        currentEnemy.anim.SetBool("run", true);
    }

    public override void LogicUpdate()
    {
        if (currentEnemy.lostTimeCounter <= 0)
            currentEnemy.SwitchState(NPCState.Patrol);

        // 遇到悬崖或墙壁时转向
        if (!currentEnemy.physicsCheck.isGround
            || (currentEnemy.physicsCheck.touchLeftWall && currentEnemy.faceDir.x < 0)
            || (currentEnemy.physicsCheck.touchRightWall && currentEnemy.faceDir.x > 0))
        {
            currentEnemy.transform.localScale = new Vector3(currentEnemy.faceDir.x, 1, 1);
        }
    }

    public override void PhysicsUpdate() { }

    public override void OnExit()
    {
        currentEnemy.anim.SetBool("run", false);
    }
}
