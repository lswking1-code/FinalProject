using UnityEngine;

/// <summary>开舱完成后悬停召唤，本批生成完毕后关舱，再进入原有后摇或离场判断。</summary>
public class HelicopterSpawnState : BaseState
{
    enum Phase { Opening, Summoning, Closing, Complete }
    HelicopterEnemy helicopter;
    Phase phase;

    public override void OnEnter(Enemy enemy)
    {
        currentEnemy = enemy;
        helicopter = enemy as HelicopterEnemy;
        phase = Phase.Opening;
        if (helicopter == null)
            return;

        helicopter.SetSummonDoorOpen(false);
        helicopter.FacePlayer();
        helicopter.StopHorizontalMotion();
        helicopter.blockSeparation = true;
        helicopter.SetAnimBool("walk", false);
        helicopter.SetAnimBool("shoot", false);
        helicopter.SetAnimBool("shootDown", false);
        helicopter.SetAnimBool("summoning", true);
        helicopter.anim?.ResetTrigger("hurt");

        // Reserve the session with its gate closed, including zero-interval waves.
        if (!helicopter.StartSummonAttack())
        {
            phase = Phase.Complete;
            helicopter.SwitchState(NPCState.Reload);
            return;
        }
        helicopter.PlaySummonAnimation("OpenDoor");
    }

    public override void LogicUpdate()
    {
        if (helicopter == null || helicopter.isDead)
            return;
        switch (phase)
        {
            case Phase.Opening:
                if (!helicopter.IsSummonAnimationFinished("OpenDoor"))
                    return;
                phase = Phase.Summoning;
                helicopter.PlaySummonAnimation("HoverOpen");
                helicopter.SetSummonDoorOpen(true);
                break;
            case Phase.Summoning:
                if (!helicopter.IsSummonFinished)
                    return;
                helicopter.SetSummonDoorOpen(false);
                phase = Phase.Closing;
                helicopter.PlaySummonAnimation("CloseDoor");
                break;
            case Phase.Closing:
                if (!helicopter.IsSummonAnimationFinished("CloseDoor"))
                    return;
                phase = Phase.Complete;
                helicopter.SwitchState(helicopter.ShouldDepartAfterSummon ? NPCState.Depart : NPCState.Reload);
                break;
        }
    }

    public override void PhysicsUpdate()
    {
        if (helicopter != null && !helicopter.isDead && !helicopter.isHurt)
            helicopter.StopHorizontalMotion();
    }

    public override void OnExit()
    {
        if (helicopter == null)
            return;
        helicopter.SetSummonDoorOpen(false);
        helicopter.blockSeparation = false;
        if (phase != Phase.Complete)
            helicopter.StopSummonAttack();
        helicopter.SetAnimBool("summoning", false);
        helicopter.SetAnimBool("shoot", false);
        helicopter.SetAnimBool("shootDown", false);
        helicopter.anim?.ResetTrigger("hurt");
        if (!helicopter.isDead && helicopter.isActiveAndEnabled)
            helicopter.PlaySummonAnimation("Idle");
    }
}
