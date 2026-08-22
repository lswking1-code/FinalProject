using UnityEngine;

/// <summary>
/// 直升机攻击：原地召唤小兵，召唤完成前保持静止，随后进入后摇。
/// </summary>
public class HelicopterSpawnState : BaseState
{
    HelicopterEnemy helicopter;
    bool summonStarted;

    public override void OnEnter(Enemy enemy)
    {
        currentEnemy = enemy;
        helicopter = enemy as HelicopterEnemy;
        summonStarted = false;

        if (helicopter == null)
            return;

        helicopter.FacePlayer();
        helicopter.StopHorizontalMotion();

        if (currentEnemy.anim != null)
        {
            currentEnemy.SetAnimBool("walk", false);
            currentEnemy.SetAnimBool("shoot", true);
            currentEnemy.SetAnimBool("shootDown", false);
        }

        summonStarted = helicopter.StartSummonAttack();
        if (!summonStarted)
            helicopter.SwitchState(NPCState.Reload);
    }

    public override void LogicUpdate()
    {
        if (helicopter == null || currentEnemy.isDead)
            return;

        if (!summonStarted || helicopter.IsSummonFinished)
            helicopter.SwitchState(NPCState.Reload);
    }

    public override void PhysicsUpdate()
    {
        if (helicopter == null || currentEnemy.isHurt || currentEnemy.isDead)
            return;

        helicopter.StopHorizontalMotion();
    }

    public override void OnExit()
    {
        if (currentEnemy?.anim == null)
            return;

        currentEnemy.SetAnimBool("shoot", false);
        currentEnemy.SetAnimBool("shootDown", false);
    }
}
