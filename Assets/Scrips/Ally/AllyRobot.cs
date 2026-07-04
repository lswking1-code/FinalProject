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
    enum AllyState { Idle, Chase, Attack, Return, Pulling }

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
    [Tooltip("牵引 Trigger 参数名")]
    public string pullTriggerName = "pull";

    [Header("牵引召回 (Ability2)")]
    [Tooltip("钩爪伸出速度（单位/秒）")]
    public float pullExtendSpeed = 12f;
    [Tooltip("钩爪收回 / 拖拽速度（单位/秒）")]
    public float pullSpeed = 8f;
    [Tooltip("到达落点的距离阈值")]
    public float pullArriveThreshold = 0.1f;
    [Tooltip("落点在机器人面向玩家一侧的水平距离")]
    public float pullLandingDistanceX = 0.8f;
    [Tooltip("落点相对机器人 Y 轴偏移")]
    public float pullLandingYOffset = 0f;
    [Tooltip("玩家与机器人超过此距离则拒绝拖拽（0 = 无限制）")]
    public float pullMaxRange = 15f;
    [Tooltip("每次拖拽后的冷却（秒）")]
    public float pullCooldown = 1f;
    [Tooltip("单次牵引消耗的 AbilityPower")]
    public float pullAbilityPowerCost = 5f;

    public bool IsPulling => currentState == AllyState.Pulling;

    // ──────────────────────────────────────────────
    //  内部引用与运行时变量
    // ──────────────────────────────────────────────
    Rigidbody2D rb;
    Animator anim;

    Vector3 spawnPoint;
    Transform currentTarget;
    float attackTimer;
    float pullCooldownTimer;

    Transform owner;
    PlayerMovement ownerMovement;
    Character ownerCharacter;
    Rigidbody2D ownerRb;
    AllyRobotPullVisual pullVisual;

    // ──────────────────────────────────────────────
    //  Unity 生命周期
    // ──────────────────────────────────────────────
    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        anim = GetComponent<Animator>();
        rb.constraints = RigidbodyConstraints2D.FreezeRotation;
        pullVisual = GetComponentInChildren<AllyRobotPullVisual>(true);
    }

    void Start()
    {
        pullVisual?.Initialize(this);
        spawnPoint = transform.position;
        attackTimer = 0f;
        FaceRight();
        SwitchState(AllyState.Idle);
    }

    void OnDestroy()
    {
        if (IsPulling)
        {
            pullVisual?.Cancel();
            EndPull();
        }
    }

    void Update()
    {
        attackTimer -= Time.deltaTime;
        pullCooldownTimer -= Time.deltaTime;

        switch (currentState)
        {
            case AllyState.Idle:    UpdateIdle();    break;
            case AllyState.Chase:   UpdateChase();   break;
            case AllyState.Attack:  UpdateAttack();  break;
            case AllyState.Return:  UpdateReturn();  break;
            case AllyState.Pulling: UpdatePulling(); break;
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
            case AllyState.Pulling:
                StopMoving();
                break;
            case AllyState.Idle:
            case AllyState.Attack:
                StopMoving();
                break;
        }
    }

    public void Initialize(Transform player)
    {
        owner = player;
        ownerMovement = player.GetComponent<PlayerMovement>();
        ownerCharacter = player.GetComponent<Character>();
        ownerRb = player.GetComponent<Rigidbody2D>();
    }

    public bool TryStartPull()
    {
        if (IsPulling || pullCooldownTimer > 0f)
            return false;

        if (owner == null || ownerMovement == null || ownerCharacter == null || ownerRb == null)
            return false;

        if (ownerMovement.IsActionLocked)
            return false;

        if (ownerCharacter.AbilityPower < pullAbilityPowerCost)
            return false;

        if (pullMaxRange > 0f
            && Vector2.Distance(owner.position, transform.position) > pullMaxRange)
            return false;

        Vector2 landing = ComputeLandingPoint();
        if (Vector2.Distance(ownerRb.position, landing) <= pullArriveThreshold)
            return false;

        ownerCharacter.DrainAbilityPower(pullAbilityPowerCost);

        FaceTarget(owner.position);
        anim.SetTrigger(pullTriggerName);

        if (pullVisual != null)
            pullVisual.Begin(owner, pullExtendSpeed, pullSpeed, 0f, pullArriveThreshold);
        else
            BeginPullWithoutVisual();

        SwitchState(AllyState.Pulling);
        pullCooldownTimer = pullCooldown;
        return true;
    }

    void BeginPullWithoutVisual()
    {
        if (ownerCharacter != null)
            ownerCharacter.SetForcedInvulnerable(true);
        if (ownerMovement != null)
            ownerMovement.BeginExternalControl();
    }

    public Vector2 GetPullLandingPoint() => ComputeLandingPoint();

    public void OnHookGrabbed()
    {
        if (ownerCharacter != null)
            ownerCharacter.SetForcedInvulnerable(true);
        if (ownerMovement != null && !ownerMovement.IsActionLocked)
            ownerMovement.BeginExternalControl();
    }

    public void OnHookRetractStep(Vector2 hookPos)
    {
        if (ownerRb != null)
            ownerRb.MovePosition(hookPos);
    }

    public void OnHookRetractComplete()
    {
        if (ownerRb != null)
            ownerRb.position = ComputeLandingPoint();
        ResumeStateAfterPull();
    }

    Vector2 ComputeLandingPoint()
    {
        float side = Mathf.Sign(owner.position.x - transform.position.x);
        if (side == 0f && ownerMovement != null)
            side = ownerMovement.FaceDirection;

        return (Vector2)transform.position
            + Vector2.right * side * pullLandingDistanceX
            + Vector2.up * pullLandingYOffset;
    }

    void UpdatePulling()
    {
        if (owner == null || ownerMovement == null || ownerCharacter == null)
        {
            pullVisual?.Cancel();
            anim.Play("Idle", 0, 0f);
            EndPull();
            SwitchState(AllyState.Idle);
            return;
        }

        if (pullVisual == null || !pullVisual.IsActive)
            UpdatePullingFallback();
    }

    void UpdatePullingFallback()
    {
        if (ownerMovement == null || ownerRb == null)
            return;

        if (!ownerMovement.IsActionLocked)
            BeginPullWithoutVisual();

        Vector2 landing = ComputeLandingPoint();
        ownerMovement.StepExternalMove(landing, pullSpeed);

        if (Vector2.Distance(ownerRb.position, landing) <= pullArriveThreshold)
        {
            ownerRb.position = landing;
            ResumeStateAfterPull();
        }
    }

    void EndPull()
    {
        if (ownerCharacter != null)
            ownerCharacter.SetForcedInvulnerable(false);

        if (ownerMovement != null && ownerMovement.IsActionLocked)
            ownerMovement.EndExternalControl();
    }

    void ResumeStateAfterPull()
    {
        if (TryAcquireTarget(out Transform target))
        {
            BeginCombat(target);
            return;
        }

        if (IsOutsideMaxChaseRange())
            SwitchState(AllyState.Return);
        else
            SwitchState(AllyState.Idle);
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
                FaceRight();
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
            case AllyState.Pulling:
                anim.SetBool(walkBoolName, false);
                StopMoving();
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

    void OnExitState(AllyState state)
    {
        if (state == AllyState.Pulling)
        {
            pullVisual?.Cancel();
            anim.Play("Idle", 0, 0f);
            EndPull();
        }
    }

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
    /// 与 PlayerMovement 一致：1 朝右，-1 朝左，通过 localScale.x 翻转。
    /// </summary>
    void FaceTarget(Vector3 targetPos)
    {
        float dx = targetPos.x - transform.position.x;
        if (dx > 0.01f)
            SetFacing(1f);
        else if (dx < -0.01f)
            SetFacing(-1f);
    }

    void FaceRight() => SetFacing(1f);

    void SetFacing(float faceDir)
    {
        Vector3 scale = transform.localScale;
        scale.x = Mathf.Abs(scale.x) * faceDir;
        transform.localScale = scale;
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
