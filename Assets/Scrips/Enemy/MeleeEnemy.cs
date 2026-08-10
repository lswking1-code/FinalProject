using UnityEngine;

/// <summary>
/// 近战敌人：追击进入 meleeRange 后前摇 → 挥刀动画 → 后摇；可选巡逻脱战回位。
/// 冲刺飞扑通过 enablePounce / CanPounce / Skill 状态预留，当前 CanPounce 恒为 false。
/// </summary>
public class MeleeEnemy : Enemy
{
    [Header("近战参数")]
    [Tooltip("进入近战攻击的水平距离")]
    public float meleeRange = 0.8f;
    [Tooltip("挥刀前摇时长")]
    public float windupDuration = 0.3f;
    [Tooltip("挥刀后摇时长，期间无法移动与攻击")]
    public float recoveryDuration = 0.6f;

    [Header("冲刺飞扑（预留）")]
    [Tooltip("开启后才会尝试进入飞扑判定（当前 CanPounce 仍返回 false）")]
    public bool enablePounce;
    [Tooltip("飞扑触发最小水平距离")]
    public float pounceMinRange = 3f;
    [Tooltip("飞扑触发最大水平距离")]
    public float pounceMaxRange = 6f;
    [Tooltip("飞扑冷却")]
    public float pounceCooldown = 3f;
    [Tooltip("飞扑蓄力前摇")]
    public float pounceWindupDuration = 0.4f;
    [Tooltip("落地硬直")]
    public float pounceLandStunDuration = 1.1f;

    [HideInInspector] public float lastPounceTime = -999f;

    protected override void Awake()
    {
        base.Awake();
        patroState = new MeleeIdleGuardState();
        returnState = new MeleeReturnHomeState();
        getCloseState = new MeleeGetCloseState();
        meleeAttackState = new MeleeAttackState();
        skillState = new MeleePounceState();

        if (normalSpeed <= 0f)
            normalSpeed = 2f;
        if (chaseSpeed <= 0f)
            chaseSpeed = 4f;
    }

    void Start()
    {
        ConfigurePhysicsCheck();
    }

    void ConfigurePhysicsCheck()
    {
        if (physicsCheck == null)
            return;

        var coll = GetComponent<CapsuleCollider2D>();
        if (coll == null)
            return;

        float halfHeight = coll.size.y * 0.5f + coll.offset.y;
        physicsCheck.bottomOffset = new Vector2(0f, -halfHeight + 0.1f);
    }

    protected override void OnEnable()
    {
        CacheHome();
        isReturning = false;

        if (isPatrol)
        {
            isAggro = false;
            SwitchState(NPCState.Patrol);
        }
        else
        {
            isAggro = true;
            EvaluateCycle();
        }
    }

    protected override void Update()
    {
        if (isPatrol && isAggro && !isDead && !isReturning && !IsPlayerInsideHomeBounds())
            BeginReturnHome();

        base.Update();
    }

    protected override void OnPatrolAggroFromDamage()
    {
        if (isReturning)
            isReturning = false;

        EnterPatrolCombat();
        EvaluateCycle();
    }

    protected override bool ShouldRunTimeCounter() => false;

    protected override bool ShouldAutoMove() => false;

    /// <summary>靠近状态停下的水平距离（盾兵有盾时用 holdRange）。</summary>
    public virtual float GetApproachStopRange() => meleeRange;

    /// <summary>
    /// 每轮循环：巡逻闸门 → 飞扑预留 → GetClose 或 MeleeAttack
    /// </summary>
    public virtual void EvaluateCycle()
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

        float dist = GetHorizontalDistanceToPlayer();
        float stopRange = GetApproachStopRange();

        if (dist > stopRange)
        {
            // 冲刺飞扑预留：enablePounce 且 CanPounce() 时进入 Skill
            if (enablePounce && CanPounce())
            {
                SwitchState(NPCState.Skill);
                return;
            }

            SwitchState(NPCState.GetClose);
            return;
        }

        SwitchState(NPCState.MeleeAttack);
    }

    /// <summary>
    /// 飞扑触发判定。实现飞扑位移前恒返回 false；下方保留距离/CD 骨架供后续启用。
    /// </summary>
    public bool CanPounce()
    {
        // 实现 MeleePounceState 真实流程前保持关闭。
        if (!enablePounce || isHurt || isDead || isReturning)
            return false;

        float dist = GetHorizontalDistanceToPlayer();
        if (dist < pounceMinRange || dist > pounceMaxRange)
            return false;

        if (Time.time < lastPounceTime + pounceCooldown)
            return false;

        // TODO: 飞扑位移落地后改为 return true
        return false;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawLine(
            transform.position + Vector3.left * meleeRange,
            transform.position + Vector3.right * meleeRange);

        if (enablePounce)
        {
            Gizmos.color = new Color(1f, 0.5f, 0f, 0.8f);
            Gizmos.DrawLine(
                transform.position + Vector3.left * pounceMinRange,
                transform.position + Vector3.right * pounceMinRange);
            Gizmos.DrawLine(
                transform.position + Vector3.left * pounceMaxRange,
                transform.position + Vector3.right * pounceMaxRange);
        }

        if (isPatrol && patrolDetectRange > 0f)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(transform.position, patrolDetectRange);
        }
    }
}
