using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 远程敌人：距离判断优先，GetClose / Shot / Move / Crouch / CrouchShoot / Jump 循环，带动态 Action 概率。
/// 射击类（Shot / CrouchShoot）进入后只开一枪，随后进入 Reload 冷却，再重新选择行为；Jump 为可开关精英能力。
/// 可选专注模式：MOVE 时原地停留，时长与 actionDuration 一致。
/// 可选 isPatrol：原地站岗，索敌开战，超出驻守点脱战半径后回位。
/// </summary>
public class RangedEnemy : Enemy
{
    /// <summary>
    /// 新像素精灵默认朝右，与旧 Metal Slug 朝左资源相反。
    /// </summary>
    protected override bool SpriteFacesRight => true;

    const float ProbabilityStep = 0.1f;
    const float CombatRangeArriveSlack = 0.12f;

    [Header("远程参数")]
    public float shootRange = 5f;
    public float actionDuration = 3f;
    [Tooltip("当前射击为单发，此字段暂未使用（保留以免破坏 Prefab 序列化）")]
    public float fireInterval = 0.5f;
    public float reloadDuration = 1f;
    public EnemyProjectile projectilePrefab;
    public Transform firePoint;

    [Header("行为权重（初始）")]
    [Tooltip("射击权重")]
    [Min(0f)] public float shotWeight = 0.7f;
    [Tooltip("移动权重")]
    [Min(0f)] public float moveWeight = 0.3f;
    [Tooltip("蹲伏权重（需开启蹲伏能力）")]
    [Min(0f)] public float crouchWeight = 0.2f;
    [Tooltip("蹲射权重（需开启蹲伏能力）")]
    [Min(0f)] public float crouchShootWeight = 0.3f;

    [Tooltip("跃起权重（需开启跃起能力）")]
    [Min(0f)] public float jumpWeight = 0.25f;

    [Header("精英能力")]
    [Tooltip("开启后，Crouch / CrouchShoot 才会进入权重掷骰")]
    public bool enableCrouchActions;
    [Tooltip("开启后，Jump 才会进入权重掷骰（手雷精英等）")]
    public bool enableJumpAction;

    [Header("射击预备")]
    [Tooltip("预备动作最长等待；Animator 的 ShotPrep 播完会提前开火")]
    public float shotPrepDuration = 0.9f;
    public string shotPrepStateName = "ShotPrep";
    [Tooltip("蹲射先等 CrouchStart 播完再进入蹲预备")]
    public float crouchStartDuration = 0.4f;
    public string crouchStartStateName = "CrouchStart";
    [Tooltip("蹲射预备状态名；播完会提前开火")]
    public string crouchShotPrepStateName = "CrouchShotPrep";
    [Tooltip("开火后停留在 CrouchShoot 的时长；动画播完会提前进 Reload")]
    public float crouchShootHoldDuration = 0.35f;
    public string crouchShootStateName = "CrouchShoot";

    [Header("专注模式")]
    [Tooltip("开启后不再靠近玩家：MOVE 原地停留，超出射程也不进入 GetClose")]
    public bool enableFocusMode;

    [Header("蹲姿")]
    [Tooltip("蹲下时的胶囊尺寸；脚底与站立对齐")]
    [SerializeField] Vector2 crouchColliderSize = new Vector2(1f, 1.2f);
    [Tooltip("蹲下时 FirePoint 的本地坐标")]
    [SerializeField] Vector2 crouchFirePointLocal = new Vector2(2.312f, 0.15f);

    [HideInInspector] public Dictionary<EnemyAction, float> actionProbabilities = new();
    [HideInInspector] public EnemyAction? lastAction;

    /// <summary>仅枪兵（基类）允许蹲伏进入权重池；手雷/火箭兵关闭。</summary>
    protected virtual bool AllowCrouchActions => true;

    CapsuleCollider2D bodyCollider;
    CircleCollider2D hurtTrigger;
    Vector2 standingColliderSize;
    Vector2 standingColliderOffset;
    Vector3 standingFirePointLocal;
    bool standingHurtTriggerEnabled;
    bool crouchPoseCached;
    bool crouchPoseActive;

    protected override void Awake()
    {
        base.Awake();
        patroState = new RangedIdleGuardState();
        returnState = new RangedReturnHomeState();
        getCloseState = new RangedGetCloseState();
        shotState = new RangedShotState();
        moveState = new RangedMoveState();
        crouchState = new RangedCrouchState();
        crouchShootState = new RangedCrouchShootState();
        reloadState = new RangedReloadState();

        if (normalSpeed <= 0f)
            normalSpeed = 2f;
        if (chaseSpeed <= 0f)
            chaseSpeed = 4f;

        CacheStandingPose();
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
        physicsCheck.bottomOffset = new Vector2(0f, -halfHeight + 0.1f);
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

    void ResetActionProbabilities()
    {
        actionProbabilities.Clear();

        var weights = new List<(EnemyAction action, float weight)>
        {
            (EnemyAction.Shot, shotWeight),
            (EnemyAction.Move, moveWeight)
        };

        if (enableCrouchActions && AllowCrouchActions)
        {
            weights.Add((EnemyAction.Crouch, crouchWeight));
            weights.Add((EnemyAction.CrouchShoot, crouchShootWeight));
        }

        if (enableJumpAction)
            weights.Add((EnemyAction.Jump, jumpWeight));

        float total = 0f;
        foreach (var (_, weight) in weights)
            total += Mathf.Max(0f, weight);

        if (total <= 0f)
        {
            actionProbabilities[EnemyAction.Shot] = 0.5f;
            actionProbabilities[EnemyAction.Move] = 0.5f;
            return;
        }

        foreach (var (action, weight) in weights)
            actionProbabilities[action] = Mathf.Max(0f, weight) / total;
    }

    protected override bool ShouldRunTimeCounter() => false;

    protected override bool ShouldAutoMove() => false;

    protected override void Update()
    {
        base.Update();
        if (isDead && crouchPoseActive)
            SetCrouchPose(false);
    }

    protected override void OnDisable()
    {
        SetCrouchPose(false);
        base.OnDisable();
    }

    /// <summary>
    /// 蹲下时缩小受击胶囊并下移开火点；站起恢复。脚底锚定，不改变站立贴地。
    /// </summary>
    public void SetCrouchPose(bool crouching)
    {
        CacheStandingPose();
        if (crouchPoseActive == crouching)
            return;

        if (crouching && !AllowCrouchActions)
            return;

        ApplyCrouchPose(crouching);
    }

    void CacheStandingPose()
    {
        if (crouchPoseCached)
            return;

        bodyCollider = GetComponent<CapsuleCollider2D>();
        if (bodyCollider != null)
        {
            standingColliderSize = bodyCollider.size;
            standingColliderOffset = bodyCollider.offset;
        }

        if (firePoint != null)
            standingFirePointLocal = firePoint.localPosition;

        var circles = GetComponents<CircleCollider2D>();
        for (int i = 0; i < circles.Length; i++)
        {
            if (circles[i] == null || !circles[i].isTrigger)
                continue;

            hurtTrigger = circles[i];
            standingHurtTriggerEnabled = hurtTrigger.enabled;
            break;
        }

        crouchPoseCached = true;
    }

    void ApplyCrouchPose(bool crouching)
    {
        crouchPoseActive = crouching;

        if (bodyCollider != null)
        {
            if (crouching)
            {
                float bottom = standingColliderOffset.y - standingColliderSize.y * 0.5f;
                Vector2 size = crouchColliderSize;
                if (size.x < 0.1f)
                    size.x = standingColliderSize.x;
                if (size.y < 0.1f)
                    size.y = Mathf.Max(0.2f, standingColliderSize.y * 0.5f);

                bodyCollider.size = size;
                bodyCollider.offset = new Vector2(
                    standingColliderOffset.x,
                    bottom + size.y * 0.5f);
            }
            else
            {
                bodyCollider.size = standingColliderSize;
                bodyCollider.offset = standingColliderOffset;
            }
        }

        if (firePoint != null)
        {
            firePoint.localPosition = crouching
                ? new Vector3(crouchFirePointLocal.x, crouchFirePointLocal.y, standingFirePointLocal.z)
                : standingFirePointLocal;
        }

        if (hurtTrigger != null)
            hurtTrigger.enabled = !crouching && standingHurtTriggerEnabled;

        if (physicsCheck == null)
            return;

        if (crouching)
            physicsCheck.RefreshOffsets();
        else
            ConfigurePhysicsCheck();
    }

    public override void ApplyEncounterFocusMode(bool enabled) => enableFocusMode = enabled;

    /// <summary>
    /// MOVE 时是否原地停留，且不进入 GetClose。枪兵/火箭兵专注模式使用。
    /// </summary>
    public virtual bool ShouldHoldPositionOnMove() => enableFocusMode;

    /// <summary>
    /// 每轮循环入口：巡逻闸门 → 距离判断 → GetClose 或 Action 判定
    /// </summary>
    public void EvaluateCycle()
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

        // 专注模式：不靠近玩家，原地射击/停留（与盾兵死守一致）
        if (!ShouldHoldPositionOnMove() && !IsWithinSlottedShootRange())
            SwitchState(NPCState.GetClose);
        else
            RollAndEnterAction();
    }

    /// <summary>
    /// 战斗射程判定距离。默认仅水平 X；子类可改为欧氏距离等。
    /// </summary>
    public virtual float GetCombatDistanceToPlayer()
    {
        return GetHorizontalDistanceToPlayer();
    }

    public override bool IsPlayerInCombatRange() => IsWithinSlottedShootRange();

    /// <summary>
    /// 已进入射击距离，或已走到同侧站位槽（避免 0.08 到位死区导致 GetClose 原地踏步）。
    /// </summary>
    public bool IsWithinSlottedShootRange()
    {
        float slotted = GetSlottedRange(shootRange);
        if (GetCombatDistanceToPlayer() <= slotted + CombatRangeArriveSlack)
            return true;

        return Mathf.Approximately(GetCombatSlotMoveDir(shootRange), 0f);
    }

    void RollAndEnterAction()
    {
        if (actionProbabilities == null || actionProbabilities.Count == 0)
            ResetActionProbabilities();

        float roll = Random.value;
        float cumulative = 0f;
        EnemyAction selected = EnemyAction.Move;

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
        EnemyAction.Shot => NPCState.Shot,
        EnemyAction.Crouch => NPCState.Crouch,
        EnemyAction.CrouchShoot => NPCState.CrouchShoot,
        EnemyAction.Jump => NPCState.Jump,
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
    /// 在 firePoint 发射一枚子弹
    /// </summary>
    public virtual void FireProjectile()
    {
        if (projectilePrefab == null || player == null)
            return;

        Vector3 spawnPos = firePoint != null ? firePoint.position : transform.position;
        float dir = Mathf.Sign(player.position.x - transform.position.x);
        if (dir == 0f)
            dir = faceDir.x;

        var projectile = Instantiate(projectilePrefab, spawnPos, Quaternion.identity);
        EnemySceneCleanup.PlaceInSourceScene(projectile.gameObject, this);
        projectile.Init(new Vector2(dir, 0f));
        FacePlayer();
    }

    protected virtual void OnDrawGizmosSelected()
    {
        DrawShootRangeGizmo();

        DrawPatrolGizmos();
    }

    protected virtual void DrawShootRangeGizmo()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(transform.position + Vector3.left * shootRange, transform.position + Vector3.right * shootRange);
    }
}
