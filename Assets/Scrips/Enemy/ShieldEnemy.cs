using UnityEngine;

/// <summary>
/// 盾兵：有盾时举盾对峙（驻守原地 / 非驻守靠近 holdRange 后停下）；
/// 玩家离开理想距离一段时间后再次追击；破盾后行为与近战敌人一致。
/// </summary>
public class ShieldEnemy : MeleeEnemy
{
    [Header("盾兵参数")]
    [Tooltip("非驻守模式下，举盾停步的水平理想距离")]
    public float holdRange = 1.5f;
    [Tooltip("玩家离开理想距离后，持续多久才再次追击")]
    public float reapproachDelay = 1.5f;
    [Tooltip("再次追击的触发距离；应 ≥ holdRange，略大可避免贴边抖动；≤0 则等同 holdRange")]
    public float reapproachRange = 2.2f;
    [Tooltip("未面向玩家时，延迟多久后转身")]
    public float faceTurnDelay = 0.35f;

    [Header("动画")]
    [Tooltip("破盾后切换到的近战 Animator Controller")]
    public RuntimeAnimatorController meleeAnimatorController;

    EnemyShieldAbsorb shieldAbsorb;

    public bool HasShield => shieldAbsorb != null;

    /// <summary>离开理想距离后触发再追的水平距离。</summary>
    public float GetReapproachRange()
    {
        float range = reapproachRange > 0f ? reapproachRange : holdRange;
        return Mathf.Max(holdRange, range);
    }

    protected override void Awake()
    {
        base.Awake();
        skillState = new ShieldHoldState();
        CacheShield();
        DisableShieldOverlaySprite();
        RecacheSpriteRendererFromChild("Sprite");
    }

    protected override void OnEnable()
    {
        base.OnEnable();
    }

    void CacheShield()
    {
        shieldAbsorb = GetComponentInChildren<EnemyShieldAbsorb>(true);
    }

    void DisableShieldOverlaySprite()
    {
        if (shieldAbsorb == null)
            return;

        var overlay = shieldAbsorb.GetComponent<SpriteRenderer>();
        if (overlay != null)
            overlay.enabled = false;
    }

    /// <summary>护盾销毁时由 EnemyShieldAbsorb 调用。</summary>
    public void NotifyShieldBroken()
    {
        shieldAbsorb = null;
        SwitchToMeleeAnimator();
        if (!isDead)
            EvaluateCycle();
    }

    void SwitchToMeleeAnimator()
    {
        if (anim == null || meleeAnimatorController == null)
            return;

        if (anim.runtimeAnimatorController == meleeAnimatorController)
            return;

        anim.runtimeAnimatorController = meleeAnimatorController;
        anim.Rebind();
        anim.Update(0f);
        RecacheAnimBoolNames();
    }

    /// <summary>
    /// 盾牌受击：播 shieldHurt（Shurt），不进入 isHurt 硬直、不打断举盾 AI。
    /// 本体受击仍走 OnTakeDamage 的 hurt。
    /// </summary>
    public void PlayShieldHitAnim()
    {
        if (isDead || anim == null)
            return;

        anim.SetTrigger("shieldHurt");
    }

    public override float GetApproachStopRange()
    {
        return HasShield ? holdRange : base.GetApproachStopRange();
    }

    /// <summary>
    /// 有盾：驻守则举盾原地；非驻守超出 holdRange 则靠近，进入后举盾；离开太远一段时间后再追。
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

        if (GetHorizontalDistanceToPlayer() > GetSlottedRange(holdRange))
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

        float reapproach = GetReapproachRange();
        if (reapproach > holdRange + 0.01f)
        {
            Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.8f);
            Gizmos.DrawLine(
                transform.position + Vector3.left * reapproach,
                transform.position + Vector3.right * reapproach);
        }
    }
}
