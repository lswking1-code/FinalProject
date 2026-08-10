using UnityEngine;

/// <summary>
/// 盾兵：有盾时仅举盾对峙（驻守原地 / 非驻守靠近 holdRange 后停下）；
/// 破盾后行为与近战敌人一致。
/// </summary>
public class ShieldEnemy : MeleeEnemy
{
    [Header("盾兵参数")]
    [Tooltip("非驻守模式下，举盾停步的水平距离")]
    public float holdRange = 1.5f;
    [Tooltip("未面向玩家时，延迟多久后转身")]
    public float faceTurnDelay = 0.35f;

    EnemyShieldAbsorb shieldAbsorb;

    /// <summary>非驻守有盾时：是否已完成唯一一次靠近。</summary>
    bool hasCompletedInitialApproach;

    public bool HasShield => shieldAbsorb != null;

    protected override void Awake()
    {
        base.Awake();
        skillState = new ShieldHoldState();
        CacheShield();
    }

    protected override void OnEnable()
    {
        hasCompletedInitialApproach = false;
        base.OnEnable();
    }

    void CacheShield()
    {
        shieldAbsorb = GetComponentInChildren<EnemyShieldAbsorb>(true);
    }

    /// <summary>护盾销毁时由 EnemyShieldAbsorb 调用。</summary>
    public void NotifyShieldBroken()
    {
        shieldAbsorb = null;
        if (!isDead)
            EvaluateCycle();
    }

    /// <summary>进入举盾对峙时标记首次靠近已完成（非驻守此后不再追）。</summary>
    public void MarkInitialApproachCompleted()
    {
        hasCompletedInitialApproach = true;
    }

    public override float GetApproachStopRange()
    {
        return HasShield ? holdRange : meleeRange;
    }

    /// <summary>
    /// 有盾：驻守则举盾原地；非驻守仅第一次靠近 holdRange 后举盾，之后原地对峙至破盾。
    /// 无盾：走近战 EvaluateCycle。
    /// </summary>
    public override void EvaluateCycle()
    {
        if (isDead)
            return;

        EnsurePlayerReference();

        if (isPatrol)
        {
            if (isReturning)
                return;

            if (!isAggro)
            {
                SwitchState(NPCState.Patrol);
                return;
            }

            if (!IsPlayerInsideHomeBounds())
            {
                BeginReturnHome();
                return;
            }
        }

        if (!HasShield)
        {
            base.EvaluateCycle();
            return;
        }

        if (isPatrol)
        {
            SwitchState(NPCState.Skill);
            return;
        }

        // 非驻守：仅首次允许靠近；完成后一直举盾，不再追玩家
        if (!hasCompletedInitialApproach && GetHorizontalDistanceToPlayer() > holdRange)
        {
            SwitchState(NPCState.GetClose);
            return;
        }

        SwitchState(NPCState.Skill);
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(
            transform.position + Vector3.left * holdRange,
            transform.position + Vector3.right * holdRange);
    }
}
