using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 远程敌人：距离判断优先，GetClose / Shot / Move / Crouch / CrouchShoot / Jump 循环，带动态 Action 概率。
/// 射击类结束后进入 Reload 冷却，再重新选择行为；Jump 为可开关精英能力。
/// 可选 isPatrol：原地站岗，索敌开战，离开所属 Bounds 后脱战回位。
/// </summary>
public class RangedEnemy : Enemy
{
    const float ProbabilityStep = 0.1f;

    [Header("远程参数")]
    public float shootRange = 5f;
    public float actionDuration = 3f;
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

    [HideInInspector] public Dictionary<EnemyAction, float> actionProbabilities = new();
    [HideInInspector] public EnemyAction? lastAction;

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
        if (isReturning)
            isReturning = false;

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

        if (enableCrouchActions)
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

    /// <summary>
    /// 每轮循环入口：巡逻闸门 → 距离判断 → GetClose 或 Action 判定
    /// </summary>
    public void EvaluateCycle()
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

        if (dist > shootRange)
            SwitchState(NPCState.GetClose);
        else
            RollAndEnterAction();
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
    /// 朝玩家水平移动
    /// </summary>
    public void MoveTowardPlayer()
    {
        if (player == null || isHurt || isDead || Rb == null)
            return;

        float dir = GetMoveDirTowardPlayer();
        ApplyHorizontalMove(dir);
        FacePlayer();
    }

    /// <summary>
    /// 沿指定水平方向移动
    /// </summary>
    public void MoveHorizontal(float direction)
    {
        if (isHurt || isDead || Rb == null)
            return;

        ApplyHorizontalMove(direction);
    }

    public float GetMoveDirTowardPlayer()
    {
        if (player == null)
            return faceDir.x;

        float dir = Mathf.Sign(player.position.x - transform.position.x);
        return dir == 0f ? faceDir.x : dir;
    }

    void ApplyHorizontalMove(float direction)
    {
        Rb.linearVelocity = new Vector2(currentSpeed * direction, Rb.linearVelocity.y);
    }

    /// <summary>
    /// 在 firePoint 发射一枚子弹
    /// </summary>
    public void FireProjectile()
    {
        if (projectilePrefab == null || player == null)
            return;

        Vector3 spawnPos = firePoint != null ? firePoint.position : transform.position;
        float dir = Mathf.Sign(player.position.x - transform.position.x);
        if (dir == 0f)
            dir = faceDir.x;

        var projectile = Instantiate(projectilePrefab, spawnPos, Quaternion.identity);
        projectile.Init(new Vector2(dir, 0f));
        FacePlayer();
    }

    /// <summary>
    /// 遇墙壁时转身，moveDir 为当前水平移动方向
    /// </summary>
    public bool TryFlipOnObstacle(float moveDir)
    {
        if (physicsCheck == null || !IsPhysicsCheckConfigured())
            return false;

        if ((physicsCheck.touchLeftWall && moveDir < 0f)
            || (physicsCheck.touchRightWall && moveDir > 0f))
        {
            transform.localScale = new Vector3(faceDir.x, 1, 1);
            return true;
        }

        return false;
    }

    bool IsPhysicsCheckConfigured()
    {
        return physicsCheck.checkRaduis > 0f && physicsCheck.groundLayer.value != 0;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(transform.position + Vector3.left * shootRange, transform.position + Vector3.right * shootRange);

        if (isPatrol && patrolDetectRange > 0f)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(transform.position, patrolDetectRange);
        }
    }
}
