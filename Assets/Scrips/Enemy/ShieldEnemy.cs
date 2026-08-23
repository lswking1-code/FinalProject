using UnityEngine;

/// <summary>
/// 盾兵：有盾时举盾对峙；非巡逻生成时立刻靠近 holdRange，之后玩家离开再延迟追击。
/// 专注模式开启时有盾原地死守。isPatrol 只负责站岗索敌与 Bounds 脱战。
/// 破盾后行为与近战敌人一致。
/// </summary>
public class ShieldEnemy : MeleeEnemy
{
    [Header("盾兵参数")]
    [Tooltip("举盾停步的水平理想距离")]
    public float holdRange = 1.5f;
    [Tooltip("玩家离开理想距离后，持续多久才再次追击")]
    public float reapproachDelay = 1.5f;
    [Tooltip("再次追击的触发距离；应 ≥ holdRange，略大可避免贴边抖动；≤0 则等同 holdRange")]
    public float reapproachRange = 2.2f;
    [Tooltip("未面向玩家时，延迟多久后转身")]
    public float faceTurnDelay = 0.35f;

    [Header("专注模式")]
    [Tooltip("开启后有盾时原地举盾，不因玩家离开理想距离而追击。与 isPatrol 独立：isPatrol 只负责站岗索敌与 Bounds 脱战。")]
    public bool enableFocusMode;

    [Header("动画")]
    [Tooltip("破盾后切换到的近战 Animator Controller")]
    public RuntimeAnimatorController meleeAnimatorController;

    EnemyShieldAbsorb shieldAbsorb;
    ShieldDropVisual shieldDropVisual;
    float leaveIdealTimer;
    bool hasHeldAtIdealRange;

    public bool HasShield => shieldAbsorb != null;

    /// <summary>离开理想距离后触发再追的水平距离。</summary>
    public float GetReapproachRange()
    {
        float range = reapproachRange > 0f ? reapproachRange : holdRange;
        return Mathf.Max(holdRange, range);
    }

    public override void ApplyEncounterFocusMode(bool enabled) => enableFocusMode = enabled;

    /// <summary>
    /// 玩家持续超出再追距离达到 reapproachDelay 后返回 true。
    /// 回到范围内则清零计时。
    /// </summary>
    public bool TickReapproachDelay()
    {
        float dist = GetHorizontalDistanceToPlayer();
        float range = GetSlottedRange(GetReapproachRange());

        if (dist <= range)
        {
            leaveIdealTimer = 0f;
            return false;
        }

        leaveIdealTimer += Time.deltaTime;
        if (leaveIdealTimer < reapproachDelay)
            return false;

        leaveIdealTimer = 0f;
        return true;
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
        hasHeldAtIdealRange = false;
        leaveIdealTimer = 0f;
        base.OnEnable();
    }

    void CacheShield()
    {
        shieldAbsorb = GetComponentInChildren<EnemyShieldAbsorb>(true);
        shieldDropVisual = GetComponentInChildren<ShieldDropVisual>(true);
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
        leaveIdealTimer = 0f;
        if (physicsCheck != null)
            physicsCheck.RefreshLedgeColliders();
        shieldDropVisual?.Drop();
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

    const float ShieldedMoveSpeedScale = 0.5f;

    public override float GetApproachStopRange()
    {
        return HasShield ? holdRange : base.GetApproachStopRange();
    }

    public override float GetMoveSpeedScale()
    {
        if (isApproachingSpawnTarget)
            return 1f;
        return HasShield ? ShieldedMoveSpeedScale : 1f;
    }

    /// <summary>
    /// 有盾：专注模式则原地举盾。非巡逻首次接敌立刻靠近；到达 holdRange 后举盾，
    /// 玩家再离开则等待延迟后追击。isPatrol 只做站岗/脱战闸门。无盾：走近战 EvaluateCycle。
    /// </summary>
    public override void EvaluateCycle()
    {
        if (isDead || isApproachingSpawnTarget)
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

        if (enableFocusMode)
        {
            SwitchToSkillIfNeeded();
            return;
        }

        if (GetHorizontalDistanceToPlayer() <= GetSlottedRange(holdRange))
        {
            hasHeldAtIdealRange = true;
            SwitchToSkillIfNeeded();
            return;
        }

        if (CurrentState == getCloseState)
            return;

        if (!hasHeldAtIdealRange && !isPatrol)
        {
            SwitchToGetCloseIfNeeded();
            return;
        }

        SwitchToSkillIfNeeded();
    }

    void SwitchToSkillIfNeeded()
    {
        if (CurrentState == skillState)
            return;

        SwitchState(NPCState.Skill);
    }

    void SwitchToGetCloseIfNeeded()
    {
        if (CurrentState == getCloseState)
            return;

        SwitchState(NPCState.GetClose);
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
