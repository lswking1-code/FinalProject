using UnityEngine;

/// <summary>
/// 友军机器人 AI 控制器。
/// 行为：记录生成点 → 索敌 → 接近目标 → 进入攻击范围后原地攻击（CD）
///        → 仅当敌人离开攻击范围才重新追击
///        → 无目标/超出最大追踪范围时返回生成点。
/// 伤害输出依赖武器子物体上挂载的 Attack.cs（OnTriggerStay2D）。
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
public class AllyRobot : MonoBehaviour
{
    // ──────────────────────────────────────────────
    //  状态
    // ──────────────────────────────────────────────
    enum AllyState
    {
        Spawning,
        Idle,
        Chase,
        Attack,
        Return,
        Pulling,
        ComboAttacking,
        ComboDashWindup,
        ComboDashing
    }

    AllyState currentState;

    // ──────────────────────────────────────────────
    //  Inspector 参数
    // ──────────────────────────────────────────────
    [Header("移动")]
    public float moveSpeed = 3f;
    [Tooltip("Combo 冲锋冲刺阶段速度（单位/秒）")]
    public float dashSpeed = 12f;
    [Tooltip("冲刺开始后超过此时间仍未进入近战距离则退出冲刺")]
    public float dashTimeout = 1.5f;
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

    [Header("动画")]
    [Tooltip("手动拖入 Animator；留空则在 Awake 时尝试从自身获取")]
    [SerializeField] Animator anim;
    [Tooltip("行走 Bool 参数名")]
    public string walkBoolName = "walk";
    [Tooltip("攻击 Trigger 参数名")]
    public string attackTriggerName = "attack";
    [Tooltip("牵引 Trigger 参数名")]
    public string pullTriggerName = "pull";
    [Tooltip("连击协同攻击 Trigger 参数名")]
    public string comboAttackTriggerName = "comboAttack";
    [Tooltip("Combo 冲锋起步 Trigger 参数名")]
    public string dashStartTriggerName = "dashStart";
    [Tooltip("Combo 冲锋起步 Animator 状态名（用于检测动画结束）")]
    public string dashStartStateName = "DashAttack_start";
    [Tooltip("Combo 冲锋冲刺循环 Animator 状态名")]
    public string dashLoopStateName = "DashAttack_loop";
    [Tooltip("冲刺进行中 Bool 参数名（Animator 兜底退出用）")]
    public string dashActiveBoolName = "dashActive";
    [Tooltip("生成动画 Animator 状态名")]
    public string dispatchStateName = "Robot_Dispatch";

    [Header("事件监听")]
    [SerializeField] VoidEventSO robotComboEvent;

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

    public bool IsPulling => currentState == AllyState.Pulling;
    public float PullCooldown => Mathf.Max(0f, pullCooldown);
    public float PullCooldownRemaining => Mathf.Max(0f, pullCooldownTimer);
    public float PullCooldownNormalized =>
        PullCooldown > 0f
            ? Mathf.Clamp01(PullCooldownRemaining / PullCooldown)
            : 0f;

    bool IsBusyWithCombo =>
        currentState == AllyState.ComboAttacking
        || currentState == AllyState.ComboDashWindup
        || currentState == AllyState.ComboDashing;

    // ──────────────────────────────────────────────
    //  内部引用与运行时变量
    // ──────────────────────────────────────────────
    Rigidbody2D rb;

    Vector3 spawnPoint;
    Transform currentTarget;
    float attackTimer;
    float pullCooldownTimer;

    Transform owner;
    PlayerMovement ownerMovement;
    Character ownerCharacter;
    Rigidbody2D ownerRb;
    AllyRobotPullVisual pullVisual;
    bool comboAttackAnimSeen;
    bool comboDashWindupAnimSeen;
    float dashTimer;
    bool pendingRetarget;
    bool dispatchAnimSeen;

    // ──────────────────────────────────────────────
    //  Unity 生命周期
    // ──────────────────────────────────────────────
    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        if (anim == null)
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
        SwitchState(AllyState.Spawning);
    }

    void OnEnable()
    {
        if (robotComboEvent != null)
            robotComboEvent.OnEventRaised += ComboAttack;
    }

    void OnDisable()
    {
        if (robotComboEvent != null)
            robotComboEvent.OnEventRaised -= ComboAttack;
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
        pullCooldownTimer = Mathf.Max(0f, pullCooldownTimer - Time.deltaTime);

        switch (currentState)
        {
            case AllyState.Spawning:         UpdateSpawning();         break;
            case AllyState.Idle:             UpdateIdle();             break;
            case AllyState.Chase:            UpdateChase();            break;
            case AllyState.Attack:           UpdateAttack();           break;
            case AllyState.Return:           UpdateReturn();           break;
            case AllyState.Pulling:          UpdatePulling();          break;
            case AllyState.ComboAttacking:   UpdateComboAttacking();   break;
            case AllyState.ComboDashWindup:  UpdateComboDashWindup();  break;
            case AllyState.ComboDashing:     UpdateComboDashing();     break;
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
            case AllyState.ComboDashing:
                MoveTowardTargetDash();
                break;
            case AllyState.Spawning:
            case AllyState.Pulling:
            case AllyState.ComboAttacking:
            case AllyState.ComboDashWindup:
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

    /// <summary>
    /// 请求重新索敌。若正在牵引 / Combo，则延后到该动作结束后执行。
    /// </summary>
    public void RequestRetarget()
    {
        if (IsPulling || IsBusyWithCombo || currentState == AllyState.Spawning)
        {
            pendingRetarget = true;
            return;
        }

        PerformRetarget();
    }

    void PerformRetarget()
    {
        pendingRetarget = false;

        if (TryAcquireTarget(out Transform target))
        {
            BeginCombat(target);
            return;
        }

        currentTarget = null;
        if (IsOutsideMaxChaseRange())
            SwitchState(AllyState.Return);
        else
            SwitchState(AllyState.Idle);
    }

    public bool TryStartPull()
    {
        if (IsPulling || pullCooldownTimer > 0f)
            return false;

        if (currentState == AllyState.Spawning)
            return false;

        if (IsBusyWithCombo)
            return false;

        if (owner == null || ownerMovement == null || ownerRb == null)
            return false;

        if (ownerMovement.IsActionLocked)
            return false;

        if (pullMaxRange > 0f
            && Vector2.Distance(owner.position, transform.position) > pullMaxRange)
            return false;

        Vector2 landing = ComputeLandingPoint();
        if (Vector2.Distance(ownerRb.position, landing) <= pullArriveThreshold)
            return false;

        FaceTarget(owner.position);
        anim.SetTrigger(pullTriggerName);

        if (pullVisual != null)
            pullVisual.Begin(owner, pullExtendSpeed, pullSpeed, 0f, pullArriveThreshold);
        else
            BeginPullWithoutVisual();

        SwitchState(AllyState.Pulling);
        pullCooldownTimer = PullCooldown;
        return true;
    }

    public void ComboAttack()
    {
        if (IsPulling || IsBusyWithCombo || currentState == AllyState.Spawning)
            return;

        if (!TryAcquireTarget(out Transform target))
            return;

        currentTarget = target;

        if (IsInAttackRange(currentTarget))
        {
            BeginComboAttack();
            return;
        }

        BeginComboDashWindup();
    }

    void BeginComboAttack()
    {
        StopMoving();
        if (anim != null)
            anim.SetBool(walkBoolName, false);

        if (IsValidCombatTarget(currentTarget))
            FaceTarget(currentTarget.position);

        if (anim != null)
            anim.SetTrigger(comboAttackTriggerName);

        attackTimer = attackCooldown;
        SwitchState(AllyState.ComboAttacking);
    }

    void BeginComboDashWindup()
    {
        StopMoving();
        if (anim != null)
            anim.SetBool(walkBoolName, false);

        if (IsValidCombatTarget(currentTarget))
            FaceTarget(currentTarget.position);

        if (anim != null)
            anim.SetTrigger(dashStartTriggerName);

        SwitchState(AllyState.ComboDashWindup);
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
        // 牵引 / Combo 结束后统一重新索敌（含 TaggetArea 延后请求）
        PerformRetarget();
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
            case AllyState.Spawning:
                SetDashActive(false);
                if (anim != null)
                {
                    anim.SetBool(walkBoolName, false);
                    anim.Play(dispatchStateName, 0, 0f);
                }
                StopMoving();
                dispatchAnimSeen = false;
                break;
            case AllyState.Idle:
                SetDashActive(false);
                anim.SetBool(walkBoolName, false);
                FaceRight();
                break;
            case AllyState.Chase:
                SetDashActive(false);
                anim.SetBool(walkBoolName, true);
                break;
            case AllyState.Attack:
                SetDashActive(false);
                anim.SetBool(walkBoolName, false);
                StopMoving();
                break;
            case AllyState.Return:
                SetDashActive(false);
                anim.SetBool(walkBoolName, true);
                currentTarget = null;
                break;
            case AllyState.Pulling:
                SetDashActive(false);
                anim.SetBool(walkBoolName, false);
                StopMoving();
                break;
            case AllyState.ComboAttacking:
                SetDashActive(false);
                anim.SetBool(walkBoolName, false);
                StopMoving();
                comboAttackAnimSeen = false;
                break;
            case AllyState.ComboDashWindup:
                SetDashActive(true);
                anim.SetBool(walkBoolName, false);
                StopMoving();
                comboDashWindupAnimSeen = false;
                break;
            case AllyState.ComboDashing:
                SetDashActive(true);
                anim.SetBool(walkBoolName, false);
                dashTimer = dashTimeout;
                break;
        }
    }

    float GetDistXTo(Transform target)
    {
        if (target == null) return float.MaxValue;
        return Mathf.Abs(transform.position.x - target.position.x);
    }

    bool IsInAttackRange(Transform target) => GetDistXTo(target) <= attackDistance;

    /// <summary>
    /// 目标仍存在、处于激活状态，且未进入死亡流程。
    /// </summary>
    bool IsValidCombatTarget(Transform target)
    {
        if (target == null || !target.gameObject.activeInHierarchy)
            return false;

        Enemy enemy = target.GetComponent<Enemy>();
        return enemy == null || !enemy.isDead;
    }

    bool TryAcquireTarget(out Transform target)
    {
        target = FindClosestEnemy();
        return target != null;
    }

    void SetDashActive(bool active)
    {
        if (anim != null)
            anim.SetBool(dashActiveBoolName, active);
    }

    /// <summary>
    /// 统一退出 Combo 冲刺：停移、强制 Idle，再恢复常规 AI 状态。
    /// </summary>
    void ExitComboDash()
    {
        StopMoving();
        SetDashActive(false);
        if (anim != null)
            anim.Play("Idle", 0, 0f);
        ResumeStateAfterCombo();
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

    void UpdateSpawning()
    {
        if (anim == null)
        {
            SwitchState(AllyState.Idle);
            return;
        }

        var info = anim.GetCurrentAnimatorStateInfo(0);
        if (info.IsName(dispatchStateName))
        {
            dispatchAnimSeen = true;
            if (info.normalizedTime < 1f)
                return;
        }
        else if (!dispatchAnimSeen)
        {
            // 等待 Animator 进入 Dispatch 状态
            return;
        }

        anim.Play("Idle", 0, 0f);
        SwitchState(AllyState.Idle);
    }

    void UpdateIdle()
    {
        if (pendingRetarget)
        {
            PerformRetarget();
            return;
        }

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

        if (!IsValidCombatTarget(currentTarget))
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

        if (!IsValidCombatTarget(currentTarget))
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

    void UpdateComboAttacking()
    {
        StopMoving();

        if (anim == null)
        {
            ResumeStateAfterCombo();
            return;
        }

        var info = anim.GetCurrentAnimatorStateInfo(0);
        bool inCombo = info.IsName("ComboAttack");
        if (inCombo)
            comboAttackAnimSeen = true;

        if (comboAttackAnimSeen && (!inCombo || info.normalizedTime >= 1f))
            ResumeStateAfterCombo();
    }

    void UpdateComboDashWindup()
    {
        StopMoving();

        if (!IsValidCombatTarget(currentTarget))
        {
            if (!TryAcquireTarget(out currentTarget))
            {
                ExitComboDash();
                return;
            }
        }

        FaceTarget(currentTarget.position);

        if (anim == null)
        {
            StartComboDash();
            return;
        }

        var info = anim.GetCurrentAnimatorStateInfo(0);
        bool inWindup = info.IsName(dashStartStateName);
        if (inWindup)
            comboDashWindupAnimSeen = true;

        if (comboDashWindupAnimSeen && (!inWindup || info.normalizedTime >= 1f))
            StartComboDash();
    }

    void StartComboDash()
    {
        if (!IsValidCombatTarget(currentTarget))
        {
            if (!TryAcquireTarget(out currentTarget))
            {
                ExitComboDash();
                return;
            }
        }

        if (IsInAttackRange(currentTarget))
        {
            BeginComboAttack();
            return;
        }

        FaceTarget(currentTarget.position);
        if (anim != null)
        {
            int loopHash = Animator.StringToHash(dashLoopStateName);
            if (anim.HasState(0, loopHash))
                anim.Play(loopHash, 0, 0f);
        }

        SwitchState(AllyState.ComboDashing);
    }

    void UpdateComboDashing()
    {
        dashTimer -= Time.deltaTime;
        if (dashTimer <= 0f)
        {
            ExitComboDash();
            return;
        }

        if (!IsValidCombatTarget(currentTarget))
        {
            if (!TryAcquireTarget(out currentTarget))
            {
                ExitComboDash();
                return;
            }
        }

        FaceTarget(currentTarget.position);

        if (IsInAttackRange(currentTarget))
        {
            StopMoving();
            BeginComboAttack();
        }
    }

    void ResumeStateAfterCombo() => ResumeStateAfterPull();

    // ──────────────────────────────────────────────
    //  移动
    // ──────────────────────────────────────────────

    void MoveTowardTarget()
    {
        if (!IsValidCombatTarget(currentTarget)) return;

        // 已在攻击范围内则不移动
        if (IsInAttackRange(currentTarget))
        {
            StopMoving();
            return;
        }

        float dir = Mathf.Sign(currentTarget.position.x - transform.position.x);
        rb.linearVelocity = new Vector2(moveSpeed * dir, rb.linearVelocity.y);
    }

    void MoveTowardTargetDash()
    {
        if (!IsValidCombatTarget(currentTarget)) return;

        if (IsInAttackRange(currentTarget))
        {
            StopMoving();
            return;
        }

        float dir = Mathf.Sign(currentTarget.position.x - transform.position.x);
        rb.linearVelocity = new Vector2(dashSpeed * dir, rb.linearVelocity.y);
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
    /// 标记敌人优先；同优先级内取 X 轴距离最近且在 detectRangeX 范围内的目标。
    /// </summary>
    Transform FindClosestEnemy()
    {
        GameObject[] enemies = GameObject.FindGameObjectsWithTag("Enemy");

        Transform closestMarked = null;
        Transform closestUnmarked = null;
        float minMarkedDistX = float.MaxValue;
        float minUnmarkedDistX = float.MaxValue;

        foreach (var e in enemies)
        {
            if (!e.gameObject.activeInHierarchy) continue;

            Enemy enemy = e.GetComponent<Enemy>();
            if (enemy != null && enemy.isDead) continue;

            float distX = Mathf.Abs(transform.position.x - e.transform.position.x);
            if (distX > detectRangeX) continue;

            bool marked = enemy != null && enemy.isMarked;

            if (marked)
            {
                if (distX < minMarkedDistX)
                {
                    minMarkedDistX = distX;
                    closestMarked = e.transform;
                }
            }
            else if (distX < minUnmarkedDistX)
            {
                minUnmarkedDistX = distX;
                closestUnmarked = e.transform;
            }
        }

        return closestMarked != null ? closestMarked : closestUnmarked;
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
