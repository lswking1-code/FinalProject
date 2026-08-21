using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 近战敌人：进入 idealRange 后按权重在 MeleeAttack / Move 间循环；可选巡逻脱战回位。
/// GetClose / Move 停在理想距离；MeleeAttack 才会再贴近 meleeRange 出刀。
/// 冲刺飞扑通过 enablePounce / CanPounce / Skill 状态预留，当前 CanPounce 恒为 false。
/// </summary>
public class MeleeEnemy : Enemy
{
    /// <summary>
    /// 新像素精灵默认朝右，与旧 Metal Slug 朝左资源相反。
    /// </summary>
    protected override bool SpriteFacesRight => true;

    const float ProbabilityStep = 0.1f;

    [Header("近战参数")]
    [Tooltip("近战出刀的水平距离；仅 MeleeAttack 才会贴近到此距离")]
    public float meleeRange = 0.8f;
    [Tooltip("GetClose 停下并开始 Move 的水平理想距离，应大于 meleeRange（类似远程的 shootRange）")]
    public float idealRange = 5f;
    [Tooltip("Move 时相对 idealRange 的容差，避免贴边来回抖")]
    [Min(0f)] public float idealRangeSlack = 0.5f;
    [Tooltip("挥刀前摇时长")]
    public float windupDuration = 0.3f;
    [Tooltip("挥刀后摇时长，期间无法移动与攻击")]
    public float recoveryDuration = 0.6f;
    [Tooltip("Move 等走位 Action 持续时间（秒）")]
    public float actionDuration = 2f;

    [Header("行为权重（初始）")]
    [Tooltip("近战攻击权重")]
    [Min(0f)] public float meleeAttackWeight = 0.7f;
    [Tooltip("移动权重")]
    [Min(0f)] public float moveWeight = 0.3f;

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
    [HideInInspector] public Dictionary<EnemyAction, float> actionProbabilities = new();
    [HideInInspector] public EnemyAction? lastAction;

    protected override void Awake()
    {
        base.Awake();
        patroState = new MeleeIdleGuardState();
        returnState = new MeleeReturnHomeState();
        getCloseState = new MeleeGetCloseState();
        meleeAttackState = new MeleeAttackState();
        moveState = new MeleeMoveState();
        skillState = new MeleePounceState();

        if (normalSpeed <= 0f)
            normalSpeed = 2f;
        if (chaseSpeed <= 0f)
            chaseSpeed = 4f;
    }

    protected override void StartCombatCycle() => EvaluateCycle();

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
        physicsCheck.bottomOffset = new Vector2(coll.offset.x, -halfHeight + 0.1f);
    }

    protected override void OnEnable()
    {
        RegisterSeparation();
        ResetActionProbabilities();
        lastAction = null;
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
        if (isApproachingSpawnTarget)
            return;

        if (isReturning)
            isReturning = false;

        EnterPatrolCombat();
        EvaluateCycle();
    }

    protected override bool ShouldRunTimeCounter() => false;

    protected override bool ShouldAutoMove() => false;

    /// <summary>靠近状态停下的水平距离（盾兵有盾时用 holdRange）。</summary>
    public virtual float GetApproachStopRange() => GetIdealRange();

    /// <summary>Move / GetClose 使用的理想站位距离，至少不小于 meleeRange。</summary>
    public float GetIdealRange() => Mathf.Max(meleeRange, idealRange);

    public float GetMoveDirAwayFromPlayer() => -GetMoveDirTowardPlayer();

    void ResetActionProbabilities()
    {
        actionProbabilities.Clear();

        var weights = new List<(EnemyAction action, float weight)>
        {
            (EnemyAction.MeleeAttack, meleeAttackWeight),
            (EnemyAction.Move, moveWeight)
        };

        float total = 0f;
        foreach (var (_, weight) in weights)
            total += Mathf.Max(0f, weight);

        if (total <= 0f)
        {
            actionProbabilities[EnemyAction.MeleeAttack] = 0.5f;
            actionProbabilities[EnemyAction.Move] = 0.5f;
            return;
        }

        foreach (var (action, weight) in weights)
            actionProbabilities[action] = Mathf.Max(0f, weight) / total;
    }

    /// <summary>
    /// 每轮循环：巡逻闸门 → 飞扑预留 → 超出理想距离则 GetClose，否则按权重选择 MeleeAttack / Move
    /// </summary>
    public virtual void EvaluateCycle()
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

        float dist = GetHorizontalDistanceToPlayer();
        float stopRange = GetSlottedRange(GetApproachStopRange());

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

        RollAndEnterAction();
    }

    void RollAndEnterAction()
    {
        if (actionProbabilities == null || actionProbabilities.Count == 0)
            ResetActionProbabilities();

        float roll = Random.value;
        float cumulative = 0f;
        EnemyAction selected = EnemyAction.MeleeAttack;

        foreach (var pair in actionProbabilities)
        {
            selected = pair.Key;
            cumulative += pair.Value;
            if (roll <= cumulative)
                break;
        }

        SwitchState(ActionToState(selected));
    }

    static NPCState ActionToState(EnemyAction action) => action switch
    {
        EnemyAction.MeleeAttack => NPCState.MeleeAttack,
        _ => NPCState.Move
    };

    /// <summary>
    /// 进入权重 Action 时更新下次触发概率
    /// </summary>
    public void OnActionEntered(EnemyAction action)
    {
        if (lastAction.HasValue && lastAction.Value == action)
        {
            if (actionProbabilities.TryGetValue(action, out float current))
            {
                actionProbabilities[action] = Mathf.Max(0f, current - ProbabilityStep);
                NormalizeProbabilities();
            }
        }
        else
            ResetActionProbabilities();

        lastAction = action;
    }

    void NormalizeProbabilities()
    {
        float total = 0f;
        foreach (var value in actionProbabilities.Values)
            total += value;

        if (total <= 0f)
        {
            ResetActionProbabilities();
            return;
        }

        var keys = new List<EnemyAction>(actionProbabilities.Keys);
        foreach (var key in keys)
            actionProbabilities[key] /= total;
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
        float ideal = GetIdealRange();
        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(
            transform.position + Vector3.left * ideal,
            transform.position + Vector3.right * ideal);

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
