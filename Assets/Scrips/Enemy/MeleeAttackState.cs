using UnityEngine;

/// <summary>
/// 近战攻击：前摇 → 挥刀动画（跟 Animator Melee 状态播完）→ 后摇。
/// Hitbox 由动画控制；本状态不生成判定框。
/// </summary>
public class MeleeAttackState : BaseState
{
    const string MeleeStateName = "Melee";

    enum Phase
    {
        Windup,
        Slash,
        Recovery
    }

    MeleeEnemy meleeEnemy;
    Phase phase;
    float timer;

    public override void OnEnter(Enemy enemy)
    {
        currentEnemy = enemy;
        meleeEnemy = enemy as MeleeEnemy;

        if (meleeEnemy == null)
            return;

        meleeEnemy.FacePlayer();
        StopHorizontal();

        if (currentEnemy.anim != null)
        {
            currentEnemy.anim.SetBool("walk", false);
            currentEnemy.anim.SetBool("melee", false);
            currentEnemy.anim.SetBool("meleeWindup", true);
        }

        phase = Phase.Windup;
        timer = Mathf.Max(0.01f, meleeEnemy.windupDuration);
    }

    public override void LogicUpdate()
    {
        if (meleeEnemy == null || currentEnemy.isDead)
            return;

        if (phase == Phase.Slash)
        {
            if (IsMeleeAnimFinished())
                EnterRecovery();
            return;
        }

        timer -= Time.deltaTime;
        if (timer > 0f)
            return;

        switch (phase)
        {
            case Phase.Windup:
                EnterSlash();
                break;
            case Phase.Recovery:
                meleeEnemy.EvaluateCycle();
                break;
        }
    }

    public override void PhysicsUpdate()
    {
        if (currentEnemy.isHurt || currentEnemy.isDead || currentEnemy.Rb == null)
            return;

        StopHorizontal();
    }

    public override void OnExit()
    {
        if (currentEnemy?.anim == null)
            return;

        currentEnemy.anim.SetBool("melee", false);
        currentEnemy.anim.SetBool("meleeWindup", false);
    }

    void EnterSlash()
    {
        phase = Phase.Slash;
        meleeEnemy.FacePlayer();

        if (currentEnemy.anim != null)
        {
            currentEnemy.anim.SetBool("meleeWindup", false);
            currentEnemy.anim.SetBool("melee", true);
        }
    }

    void EnterRecovery()
    {
        phase = Phase.Recovery;
        timer = Mathf.Max(0.01f, meleeEnemy.recoveryDuration);

        if (currentEnemy.anim != null)
            currentEnemy.anim.SetBool("melee", false);
    }

    /// <summary>
    /// Melee 状态播完一整遍（非循环）后返回 true；尚未进入该状态时继续等待。
    /// </summary>
    bool IsMeleeAnimFinished()
    {
        var anim = currentEnemy.anim;
        if (anim == null)
            return true;

        var info = anim.GetCurrentAnimatorStateInfo(0);
        if (!info.IsName(MeleeStateName))
            return false;

        return info.normalizedTime >= 1f;
    }

    void StopHorizontal()
    {
        if (currentEnemy.Rb == null)
            return;

        Vector2 vel = currentEnemy.Rb.linearVelocity;
        vel.x = 0f;
        currentEnemy.Rb.linearVelocity = vel;
    }
}
