using UnityEngine;

/// <summary>
/// 飞行敌人射击：按相对位置选择向下或斜向单发，射击时静止，随后进入原地后摇。
/// </summary>
public class FlyingShotState : BaseState
{
    FlyingEnemy flyingEnemy;
    bool firingDown;

    public override void OnEnter(Enemy enemy)
    {
        currentEnemy = enemy;
        flyingEnemy = enemy as FlyingEnemy;

        if (flyingEnemy == null)
            return;

        flyingEnemy.FacePlayer();
        flyingEnemy.StopHorizontalMotion();

        firingDown = flyingEnemy.ShouldFireDown();

        if (currentEnemy.anim != null)
        {
            currentEnemy.anim.SetBool("walk", false);
            currentEnemy.anim.SetBool("shoot", !firingDown);
            currentEnemy.anim.SetBool("shootDown", firingDown);
        }

        if (firingDown)
            flyingEnemy.FireDownProjectile();
        else
            flyingEnemy.FireDiagonalProjectile();
    }

    public override void LogicUpdate()
    {
        if (flyingEnemy == null || currentEnemy.isDead)
            return;

        flyingEnemy.SwitchState(NPCState.Reload);
    }

    public override void PhysicsUpdate()
    {
        if (flyingEnemy == null || currentEnemy.isHurt || currentEnemy.isDead)
            return;

        flyingEnemy.StopHorizontalMotion();
    }

    public override void OnExit()
    {
        if (currentEnemy?.anim == null)
            return;

        currentEnemy.anim.SetBool("shoot", false);
        currentEnemy.anim.SetBool("shootDown", false);
    }
}
