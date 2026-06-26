using UnityEngine;

/// <summary>
/// 友军机器人 AI 控制器。
/// 行为：记录生成点 → 索敌 → 接近目标 → 进入攻击范围后原地攻击（CD）
///        → 仅当敌人离开攻击范围才重新追击
///        → 无目标/超出最大追踪范围时返回生成点。
/// 伤害输出依赖武器子物体上挂载的 Attack.cs（OnTriggerStay2D）。
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(Animator))]
public class AllyRobot : MonoBehaviour
{
    // ──────────────────────────────────────────────
    //  状态
    // ──────────────────────────────────────────────
    enum AllyState { Idle, Chase, Attack, Return }

    AllyState currentState;

    // ──────────────────────────────────────────────
    //  Inspector 参数
    // ──────────────────────────────────────────────
    [Header("移动")]
    public float moveSpeed = 3f;
    [Tooltip("到达目标点时判定为'已到达'的距离阈值")]
    public float arriveThreshold = 0.15f;

    [Header("索敌")]
    [Tooltip("以自身为中心的 X 轴单侧索敌半径")]
    public float detectRangeX = 6f;

    [Header("攻击")]
    [Tooltip("开始攻击的最大距离（X 轴）")]
    public float attackDistance = 1.2f;
    [Tooltip("每次攻击之间的冷却时间（秒）")]
    public float attackCooldown = 1.5f;

    [Header("最大追踪范围")]
    [Tooltip("以生成点为圆心，超过此距离强制返回")]
    public float maxChaseRange = 10f;

    [Header("动画参数名")]
    [Tooltip("行走 Bool 参数名")]
    public string walkBoolName = "walk";
    [Tooltip("攻击 Trigger 参数名")]
    public string attackTriggerName = "attack";

    // ──────────────────────────────────────────────
    //  内部引用与运行时变量
    // ──────────────────────────────────────────────
    Rigidbody2D rb;
    Animator anim;

    Vector3 spawnPoint;
    Transform currentTarget;
    float attackTimer;

    // ──────────────────────────────────────────────
    //  Unity 生命周期
    // ──────────────────────────────────────────────
    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        anim = GetComponent<Animator>();
        rb.constraints = RigidbodyConstraints2D.FreezeRotation;
    }

    void Start()
    {
        spawnPoint = transform.position;
        attackTimer = 0f;
        SwitchState(AllyState.Idle);
    }

    void Update()
    {
        attackTimer -= Time.deltaTime;

        switch (currentState)
        {
            case AllyState.Idle:   UpdateIdle();   break;
            case AllyState.Chase:  UpdateChase();  break;
            case AllyState.Attack: UpdateAttack(); break;
            case AllyState.Return: UpdateReturn(); break;
        }
    }

    void FixedUpdate()
    {
        switch (currentState)
        {
            case AllyState.Chase:
                MoveTowardTarget();
                break;
            case AllyState.Return:
                MoveTowardSpawn();
                break;
            case AllyState.Idle:
            case AllyState.Attack:
                StopMoving();
                break;
        }
    }

    // ──────────────────────────────────────────────
    //  状态机
    // ──────────────────────────────────────────────
    void SwitchState(AllyState next)
    {
        OnExitState(currentState);
        currentState = next;
        OnEnterState(currentState);
    }

    void OnEnterState(AllyState state)
    {
        switch (state)
        {
            case AllyState.Idle:
                anim.SetBool(walkBoolName, false);
                break;
            case AllyState.Chase:
                anim.SetBool(walkBoolName, true);
                break;
            case AllyState.Attack:
                anim.SetBool(walkBoolName, false);
                StopMoving();
                break;
            case AllyState.Return:
                anim.SetBool(walkBoolName, true);
                currentTarget = null;
                break;
        }
    }

    float GetDistXTo(Transform target)
    {
        if (target == null) return float.MaxValue;
        return Mathf.Abs(transform.position.x - target.position.x);
    }

    bool IsInAttackRange(Transform target) => GetDistXTo(target) <= attackDistance;

    bool TryAcquireTarget(out Transform target)
    {
        target = FindClosestEnemy();
        return target != null;
    }

    void BeginCombat(Transform target)
    {
        currentTarget = target;
        FaceTarget(target.position);
        StopMoving();

        if (IsInAttackRange(target))
            SwitchState(AllyState.Attack);
        else
            SwitchState(AllyState.Chase);
    }

    void OnExitState(AllyState state) { }

    // ──────────────────────────────────────────────
    //  各状态 Update 逻辑
    // ──────────────────────────────────────────────

    void UpdateIdle()
    {
        if (TryAcquireTarget(out Transform target))
            BeginCombat(target);
    }

    void UpdateChase()
    {
        if (IsOutsideMaxChaseRange())
        {
            SwitchState(AllyState.Return);
            return;
        }

        if (currentTarget == null || !currentTarget.gameObject.activeInHierarchy)
        {
            if (!TryAcquireTarget(out currentTarget))
            {
                SwitchState(AllyState.Return);
                return;
            }
        }

        // 进入攻击范围：立刻停下并切换攻击，不再继续靠近
        if (IsInAttackRange(currentTarget))
        {
            StopMoving();
            SwitchState(AllyState.Attack);
            return;
        }

        FaceTarget(currentTarget.position);
    }

    void UpdateAttack()
    {
        if (IsOutsideMaxChaseRange())
        {
            SwitchState(AllyState.Return);
            return;
        }

        if (currentTarget == null || !currentTarget.gameObject.activeInHierarchy)
        {
            if (!TryAcquireTarget(out currentTarget))
            {
                SwitchState(AllyState.Return);
                return;
            }
        }

        // 敌人离开攻击范围后才重新追击
        if (!IsInAttackRange(currentTarget))
        {
            SwitchState(AllyState.Chase);
            return;
        }

        StopMoving();
        FaceTarget(currentTarget.position);

        if (attackTimer <= 0f)
        {
            anim.SetTrigger(attackTriggerName);
            attackTimer = attackCooldown;
        }
    }

    void UpdateReturn()
    {
        if (TryAcquireTarget(out Transform target))
        {
            BeginCombat(target);
            return;
        }

        FaceTarget(spawnPoint);

        float distToSpawn = Mathf.Abs(transform.position.x - spawnPoint.x);
        if (distToSpawn <= arriveThreshold)
        {
            transform.position = new Vector3(spawnPoint.x, transform.position.y, transform.position.z);
            SwitchState(AllyState.Idle);
        }
    }

    // ──────────────────────────────────────────────
    //  移动
    // ──────────────────────────────────────────────

    void MoveTowardTarget()
    {
        if (currentTarget == null) return;

        // 已在攻击范围内则不移动
        if (IsInAttackRange(currentTarget))
        {
            StopMoving();
            return;
        }

        float dir = Mathf.Sign(currentTarget.position.x - transform.position.x);
        rb.linearVelocity = new Vector2(moveSpeed * dir, rb.linearVelocity.y);
    }

    void MoveTowardSpawn()
    {
        float distX = Mathf.Abs(transform.position.x - spawnPoint.x);
        if (distX <= arriveThreshold)
        {
            StopMoving();
            return;
        }

        float dir = Mathf.Sign(spawnPoint.x - transform.position.x);
        rb.linearVelocity = new Vector2(moveSpeed * dir, rb.linearVelocity.y);
    }

    void StopMoving()
    {
        rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
    }

    // ──────────────────────────────────────────────
    //  朝向
    // ──────────────────────────────────────────────

    /// <summary>
    /// 与现有 Enemy.FacePlayer 一致：目标在右侧 → localScale.x = -1，左侧 → 1。
    /// </summary>
    void FaceTarget(Vector3 targetPos)
    {
        float dx = targetPos.x - transform.position.x;
        if (dx > 0.01f)
            transform.localScale = new Vector3(-1f, 1f, 1f);
        else if (dx < -0.01f)
            transform.localScale = new Vector3(1f, 1f, 1f);
    }

    // ──────────────────────────────────────────────
    //  索敌
    // ──────────────────────────────────────────────

    /// <summary>
    /// 用 Tag "Enemy" 直接搜索，不依赖 Physics2D 仿真状态（兼容 simulated=false 的敌人）。
    /// 返回 X 轴距离最近且在 detectRangeX 范围内的目标；未找到则返回 null。
    /// </summary>
    Transform FindClosestEnemy()
    {
        GameObject[] enemies = GameObject.FindGameObjectsWithTag("Enemy");

        Transform closest = null;
        float minDistX = float.MaxValue;

        foreach (var e in enemies)
        {
            if (!e.gameObject.activeInHierarchy) continue;

            float distX = Mathf.Abs(transform.position.x - e.transform.position.x);
            if (distX > detectRangeX) continue;

            if (distX < minDistX)
            {
                minDistX = distX;
                closest = e.transform;
            }
        }

        return closest;
    }

    // ──────────────────────────────────────────────
    //  范围检测
    // ──────────────────────────────────────────────

    bool IsOutsideMaxChaseRange()
    {
        return Vector2.Distance(transform.position, spawnPoint) > maxChaseRange;
    }

    // ──────────────────────────────────────────────
    //  Gizmos 可视化
    // ──────────────────────────────────────────────

    void OnDrawGizmosSelected()
    {
        Vector3 origin = Application.isPlaying ? spawnPoint : transform.position;

        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, detectRangeX);

        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, attackDistance);

        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(origin, maxChaseRange);

        Gizmos.color = Color.green;
        Gizmos.DrawLine(origin + Vector3.left * 0.3f, origin + Vector3.right * 0.3f);
        Gizmos.DrawLine(origin + Vector3.up * 0.3f, origin + Vector3.down * 0.3f);
    }
}
