using UnityEngine;

/// <summary>
/// 遭遇生成后先走到配置的目标点，抵达后再进入战斗/专注。
/// </summary>
public class EnemyApproachTargetState : BaseState
{
    public override void OnEnter(Enemy enemy)
    {
        currentEnemy = enemy;
        currentEnemy.currentSpeed = currentEnemy.normalSpeed > 0f
            ? currentEnemy.normalSpeed
            : currentEnemy.chaseSpeed;

        if (currentEnemy.anim != null)
            currentEnemy.SetAnimBool("walk", true);
    }

    public override void LogicUpdate()
    {
        if (currentEnemy == null || currentEnemy.isDead)
            return;

        if (currentEnemy.HasReachedSpawnTarget())
            currentEnemy.FinishSpawnApproach();
    }

    public override void PhysicsUpdate()
    {
        if (currentEnemy == null || currentEnemy.isHurt || currentEnemy.isDead)
            return;

        if (currentEnemy.HasReachedSpawnTarget())
            return;

        currentEnemy.MoveTowardSpawnTarget();
    }

    public override void OnExit()
    {
        if (currentEnemy?.anim != null)
            currentEnemy.SetAnimBool("walk", false);
    }
}
