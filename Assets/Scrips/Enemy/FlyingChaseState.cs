using UnityEngine;

/// <summary>
/// 飞行敌人追击：飞向玩家头顶扇区，进入后重新进入战斗循环。
/// </summary>
public class FlyingChaseState : BaseState
{
    FlyingEnemy flyingEnemy;

    public override void OnEnter(Enemy enemy)
    {
        currentEnemy = enemy;
        flyingEnemy = enemy as FlyingEnemy;
        currentEnemy.currentSpeed = currentEnemy.chaseSpeed;
        currentEnemy.FacePlayer();

        if (currentEnemy.anim != null)
            currentEnemy.anim.SetBool("walk", true);
    }

    public override void LogicUpdate()
    {
        if (flyingEnemy == null || currentEnemy.isDead)
            return;

        if (flyingEnemy.IsInOverheadFan())
            flyingEnemy.EvaluateCycle();
    }

    public override void PhysicsUpdate()
    {
        if (flyingEnemy == null || currentEnemy.isHurt || currentEnemy.isDead)
            return;

        flyingEnemy.MoveTowardOverheadFan(currentEnemy.currentSpeed);
    }

    public override void OnExit()
    {
        if (currentEnemy?.anim != null)
            currentEnemy.anim.SetBool("walk", false);
    }
}
