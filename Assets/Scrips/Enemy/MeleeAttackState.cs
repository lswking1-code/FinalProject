using UnityEngine;

/// <summary>
/// 近战攻击：前摇 → 挥刀动画（跟 Animator Melee 状态播完）→ 后摇。
/// Hitbox（Attacker1）按 Melee clip 归一化时间在出刀帧开启，状态退出时关闭。
/// </summary>
public class MeleeAttackState : BaseState
{
    const string MeleeStateName = "Melee";
    const string AttackerChildName = "Attacker1";
    /// <summary>enemy_melee 出刀帧（sprite 3）起始 normalizedTime ≈ 0.5。</summary>
    const float HitboxActiveFromNormalized = 0.5f;

    enum Phase
    {
        Windup,
        Slash,
        Recovery
    }

    MeleeEnemy meleeEnemy;
    Phase phase;
    float timer;
    Transform attacker1;

    public override void OnEnter(Enemy enemy)
    {
        currentEnemy = enemy;
        meleeEnemy = enemy as MeleeEnemy;

        if (meleeEnemy == null)
            return;

        attacker1 = currentEnemy.transform.Find(AttackerChildName);
        SetAttackerActive(false);

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
            SyncHitboxWithSlashAnim();
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
        SetAttackerActive(false);

        if (currentEnemy?.anim == null)
            return;

        currentEnemy.anim.SetBool("melee", false);
        currentEnemy.anim.SetBool("meleeWindup", false);
    }

    void EnterSlash()
    {
        phase = Phase.Slash;
        meleeEnemy.FacePlayer();
        SetAttackerActive(false);

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
        SetAttackerActive(false);

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

    /// <summary>
    /// 仅在 Melee 动画后半段（enemy_melee_3 / _4）开启伤害判定。
    /// </summary>
    void SyncHitboxWithSlashAnim()
    {
        var anim = currentEnemy.anim;
        if (anim == null)
        {
            SetAttackerActive(false);
            return;
        }

        var info = anim.GetCurrentAnimatorStateInfo(0);
        if (!info.IsName(MeleeStateName))
        {
            SetAttackerActive(false);
            return;
        }

        float n = info.normalizedTime;
        bool active = n >= HitboxActiveFromNormalized && n < 1f;
        SetAttackerActive(active);
    }

    void StopHorizontal()
    {
        if (currentEnemy.Rb == null)
            return;

        Vector2 vel = currentEnemy.Rb.linearVelocity;
        vel.x = 0f;
        currentEnemy.Rb.linearVelocity = vel;
    }

    void SetAttackerActive(bool active)
    {
        if (attacker1 == null)
            return;
        if (attacker1.gameObject.activeSelf == active)
            return;
        attacker1.gameObject.SetActive(active);
    }
}
