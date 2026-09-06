using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 近战敌人：进入 idealRange 后按权重在 MeleeAttack / Move 间循环；可选巡逻脱战回位。
/// GetClose / Move 停在理想距离；MeleeAttack 才会再贴近 meleeRange 出刀。
/// enableDash 开启后，MeleeAttack 的 CloseIn 用冲刺取代普通奔跑。
/// </summary>
public class MeleeEnemy : Enemy
{
    /// <summary>
    /// 新像素精灵默认朝右，与旧 Metal Slug 朝左资源相反。
    /// </summary>
    protected override bool SpriteFacesRight => true;

    const float ProbabilityStep = 0.1f;
    /// <summary>与站位槽 0.08 到位阈值对齐并略放宽，避免 GetClose 停步后无法进入攻击。</summary>
    const float ApproachArriveSlack = 0.12f;

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

    [Header("冲刺（进阶）")]
    [Tooltip("开启后，MeleeAttack 贴近出刀距离时用冲刺取代普通奔跑")]
    public bool enableDash;
    [Tooltip("冲刺水平速度，应明显高于 chaseSpeed")]
    public float dashSpeed = 12f;
    [Tooltip("冲刺超时：卡住或到不了锁定点时强制进入前摇，避免死循环")]
    public float dashTimeout = 1.5f;

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

    protected override void OnPatrolAggroFromDamage()
    {
        if (isReturning || isApproachingSpawnTarget)
            return;

        EnterPatrolCombat();
        EvaluateCycle();
    }

    protected override bool ShouldRunTimeCounter() => false;

    protected override bool ShouldAutoMove() => false;

    /// <summary>靠近状态停下的水平距离（盾兵有盾时用 holdRange）。</summary>
    public virtual float GetApproachStopRange() => GetIdealRange();

    public override bool IsPlayerInCombatRange() => IsWithinApproachRange();

    /// <summary>
    /// 已进入靠近停步距离，或已走到同侧站位槽（避免 0.08 到位死区导致 GetClose 原地踏步）。
    /// </summary>
    public bool IsWithinApproachRange()
    {
        float slotted = GetSlottedRange(GetApproachStopRange());
        if (GetHorizontalDistanceToPlayer() <= slotted + ApproachArriveSlack)
            return true;

        return Mathf.Approximately(GetCombatSlotMoveDir(GetApproachStopRange()), 0f);
    }

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
    /// 每轮循环：巡逻闸门 → 超出理想距离则 GetClose，否则按权重选择 MeleeAttack / Move
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

            if (ShouldBeginPatrolReturn())
            {
                BeginReturnHome();
                return;
            }
        }

        if (!IsWithinApproachRange())
        {
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

        DrawPatrolGizmos();
    }
}
