using UnityEngine;

/// <summary>
/// 冲刺飞扑预留状态（NPCState.Skill）。
/// 计划流程：
/// 1. 蓄力前摇（pounceWindupDuration）：原地屈膝预警
/// 2. 锁定玩家起飞瞬间坐标，施加斜向上冲量做抛物线位移
/// 3. 飞行中持续伤害 Hitbox（后续实现）
/// 4. 落地硬直（pounceLandStunDuration）后回到 EvaluateCycle
/// 当前为空壳：进入后立即退回 GetClose，避免误开 enablePounce 时卡住。
/// </summary>
public class MeleePounceState : BaseState
{
    MeleeEnemy meleeEnemy;

    public override void OnEnter(Enemy enemy)
    {
        currentEnemy = enemy;
        meleeEnemy = enemy as MeleeEnemy;

        if (currentEnemy.anim != null)
        {
            currentEnemy.anim.SetBool("walk", false);
            currentEnemy.anim.SetBool("melee", false);
            currentEnemy.anim.SetBool("meleeWindup", false);
        }

        // 尚未实现飞扑位移：兜底回追击，避免死循环卡在 Skill。
        if (currentEnemy != null)
            currentEnemy.blockSeparation = true;
        if (meleeEnemy != null && !currentEnemy.isDead)
            meleeEnemy.SwitchState(NPCState.GetClose);
    }

    public override void LogicUpdate() { }

    public override void PhysicsUpdate()
    {
        if (currentEnemy == null || currentEnemy.isHurt || currentEnemy.isDead || currentEnemy.Rb == null)
            return;

        Vector2 vel = currentEnemy.Rb.linearVelocity;
        vel.x = 0f;
        currentEnemy.Rb.linearVelocity = vel;
    }

    public override void OnExit()
    {
        if (currentEnemy != null)
            currentEnemy.blockSeparation = false;
    }
}
