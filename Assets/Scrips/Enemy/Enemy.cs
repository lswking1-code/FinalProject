using System.Collections;
using UnityEngine;

/// <summary>
/// 敌人基类，管理移动、状态机切换、受伤与死亡逻辑。
/// 子类需在 Awake 中初始化对应状态实例。
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
public class Enemy : MonoBehaviour
{
    Rigidbody2D rb;

    [HideInInspector] public Animator anim;
    [HideInInspector] public PhysicsCheck physicsCheck;

    [Header("基本参数")]
    public float normalSpeed;
    public float chaseSpeed;
    [HideInInspector] public float currentSpeed;
    [Tooltip("为 true 时镭射光束在此敌人处截断")]
    public bool blocksLaser;

    public Vector3 faceDir;
    [System.Obsolete("已废弃，改用 Attack.enableKnockback / knockbackForce")]
    [Tooltip("已废弃，改用 Attack.enableKnockback")]
    public float hurtForce;
    public Transform attacker;

    [Header("受击反馈")]
    [Tooltip("硬直时长（闪红 + 抖动）")]
    public float hurtDuration = 0.35f;
    [Tooltip("闪红切换间隔")]
    public float hurtFlashInterval = 0.05f;
    public Color hurtFlashColor = new Color(1f, 0.25f, 0.25f, 1f);
    [Tooltip("精灵局部抖动幅度")]
    public float hurtShakeIntensity = 0.08f;

    [Header("检测")]
    public Vector2 centerOffset;
    public Vector2 checkSize;
    public float checkDistance;
    public LayerMask attackLayer;

    [Header("巡逻站岗")]
    [Tooltip("开启后原地 Idle，索敌范围内发现玩家才开战；玩家离开所属 Bounds 后脱战回位")]
    public bool isPatrol;
    [Tooltip("全向索敌半径")]
    public float patrolDetectRange = 6f;
    [Tooltip("回位抵达判定距离")]
    public float returnArriveDistance = 0.15f;

    [Header("计时器")]
    public float waitTime;
    public float waitTimeCounter;
    public bool wait;
    public float lostTime;
    public float lostTimeCounter;

    [Header("状态")]
    public bool isHurt;
    public bool isDead;
    [HideInInspector] public bool isMarked;
    [HideInInspector] public bool isAggro;
    [HideInInspector] public bool isReturning;

    [Header("死亡掉落")]
    [Tooltip("开启后死亡时掉落弹药包")]
    public bool dropAmmoOnDeath;
    [Tooltip("掉落的弹药包 Prefab（BulletBoxS/M/L）")]
    public GameObject ammoDropPrefab;
    [Tooltip("相对敌人当前位置的掉落偏移")]
    public Vector3 ammoDropOffset;
    [Tooltip("开启后死亡时掉落回血包")]
    public bool dropHealthOnDeath;
    [Tooltip("掉落的回血包 Prefab（HealthPack）")]
    public GameObject healthDropPrefab;
    [Tooltip("相对敌人当前位置的回血包掉落偏移")]
    public Vector3 healthDropOffset;

    [HideInInspector] public Transform player;
    [HideInInspector] public Vector3 homePosition;
    [HideInInspector] public Collider2D homeBounds;

    bool ammoDropped;
    bool healthDropped;

    protected Character character;

    private BaseState currentState;
    protected BaseState patroState;
    protected BaseState chaseState;
    protected BaseState getCloseState;
    protected BaseState shotState;
    protected BaseState moveState;
    protected BaseState crouchState;
    protected BaseState crouchShootState;
    protected BaseState reloadState;
    protected BaseState jumpState;
    protected BaseState returnState;
    protected BaseState meleeAttackState;
    protected BaseState skillState;

    SpriteRenderer spriteRenderer;
    Color spriteOriginalColor = Color.white;
    Vector3 spriteOriginalLocalPos;
    Coroutine hurtRoutine;

    public Rigidbody2D Rb => rb;

    protected virtual void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        anim = GetComponent<Animator>();
        physicsCheck = GetComponent<PhysicsCheck>();
        character = GetComponent<Character>();
        CacheSpriteRenderer();

        rb.constraints = RigidbodyConstraints2D.FreezeRotation;
        currentSpeed = normalSpeed;

        EnsurePlayerReference();
        CacheHome();
    }

    void CacheSpriteRenderer()
    {
        spriteRenderer = GetComponentInChildren<SpriteRenderer>();
        if (spriteRenderer == null)
            return;

        spriteOriginalColor = spriteRenderer.color;
        spriteOriginalLocalPos = spriteRenderer.transform.localPosition;
    }

    /// <summary>
    /// 缓存玩家引用。玩家在 Persistent 场景中可能晚于敌人 Awake 才激活。
    /// </summary>
    public void EnsurePlayerReference()
    {
        if (player != null)
            return;

        var playerObj = GameObject.FindGameObjectWithTag("Player");
        if (playerObj != null)
            player = playerObj.transform;
    }

    /// <summary>
    /// 记录出生点，并绑定包含出生点的场景 Bounds。
    /// </summary>
    public void CacheHome()
    {
        homePosition = transform.position;
        homeBounds = FindContainingBounds(homePosition);

        if (isPatrol && homeBounds == null)
            Debug.LogWarning($"[Enemy] {name}: isPatrol 开启但未找到包含出生点的 Bounds，Bounds 脱战将不会触发。", this);
    }

    static Collider2D FindContainingBounds(Vector3 worldPos)
    {
        var objs = GameObject.FindGameObjectsWithTag("Bounds");
        if (objs == null || objs.Length == 0)
            return null;

        Vector2 point = worldPos;
        Collider2D fallback = null;

        foreach (var go in objs)
        {
            if (go == null || !go.activeInHierarchy)
                continue;

            var col = go.GetComponent<Collider2D>();
            if (col == null || !col.enabled)
                continue;

            if (col.OverlapPoint(point))
                return col;

            if (fallback == null && col.bounds.Contains(point))
                fallback = col;
        }

        return fallback;
    }

    /// <summary>
    /// 玩家是否在全向索敌范围内。
    /// </summary>
    public bool IsPlayerInPatrolRange()
    {
        EnsurePlayerReference();
        if (player == null || patrolDetectRange <= 0f)
            return false;

        return Vector2.Distance(transform.position, player.position) <= patrolDetectRange;
    }

    /// <summary>
    /// 玩家是否仍在敌人所属 Bounds 内。未绑定 Bounds 时视为始终在内（不脱战）。
    /// </summary>
    public bool IsPlayerInsideHomeBounds()
    {
        if (homeBounds == null)
            return true;

        EnsurePlayerReference();
        if (player == null)
            return false;

        Vector2 point = player.position;
        if (homeBounds.OverlapPoint(point))
            return true;

        return homeBounds.bounds.Contains(point);
    }

    /// <summary>
    /// 脱战：回满血并进入回位状态。
    /// </summary>
    public virtual void BeginReturnHome()
    {
        if (isDead || isReturning)
            return;

        isAggro = false;
        isReturning = true;
        wait = false;

        if (character != null)
            character.RestoreFullHealth();

        if (rb != null)
            rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);

        if (returnState != null)
            SwitchState(NPCState.Return);
        else
            FinishPatrolReset();
    }

    /// <summary>
    /// 抵达出生点后重新进入站岗 Idle。
    /// </summary>
    public virtual void FinishPatrolReset()
    {
        isAggro = false;
        isReturning = false;
        transform.position = homePosition;

        if (rb != null)
            rb.linearVelocity = Vector2.zero;

        if (character != null)
            character.RestoreFullHealth();

        SwitchState(NPCState.Patrol);
    }

    /// <summary>
    /// 巡逻模式下被拉入战斗（发现玩家或受伤）。
    /// </summary>
    public virtual void EnterPatrolCombat()
    {
        if (!isPatrol || isDead || isAggro)
            return;

        isAggro = true;
    }

    protected virtual void OnEnable()
    {
        currentState = GetInitialState();
        currentState.OnEnter(this);
    }

    protected virtual BaseState GetInitialState() => patroState;

    protected virtual void Update()
    {
        EnsurePlayerReference();
        faceDir = new Vector3(-transform.localScale.x, 0, 0);

        currentState.LogicUpdate();

        if (ShouldRunTimeCounter())
            TimeCounter();
    }

    protected virtual void FixedUpdate()
    {
        if (ShouldAutoMove())
            Move();

        currentState.PhysicsUpdate();
    }

    protected virtual bool ShouldRunTimeCounter() => true;

    protected virtual bool ShouldAutoMove() => !isHurt && !isDead && !wait;

    protected virtual void OnDisable()
    {
        currentState?.OnExit();
    }

    /// <summary>
    /// 按当前朝向与速度移动
    /// </summary>
    public virtual void Move()
    {
        rb.linearVelocity = new Vector2(currentSpeed * faceDir.x * Time.deltaTime, rb.linearVelocity.y);
    }

    /// <summary>
    /// 更新等待转身与丢失目标计时
    /// </summary>
    public void TimeCounter()
    {
        if (wait)
        {
            waitTimeCounter -= Time.deltaTime;
            if (waitTimeCounter <= 0)
            {
                wait = false;
                waitTimeCounter = waitTime;
                transform.localScale = new Vector3(faceDir.x, 1, 1);
            }
        }

        if (!FoundPlayer() && lostTimeCounter > 0)
            lostTimeCounter -= Time.deltaTime;
        else if (FoundPlayer())
            lostTimeCounter = lostTime;
    }

    /// <summary>
    /// 使用 BoxCast 检测前方是否存在玩家
    /// </summary>
    public bool FoundPlayer()
    {
        return Physics2D.BoxCast(
            transform.position + (Vector3)centerOffset,
            checkSize, 0, faceDir, checkDistance, attackLayer);
    }

    /// <summary>
    /// 与玩家的水平距离
    /// </summary>
    public float GetHorizontalDistanceToPlayer()
    {
        if (player == null)
            return float.MaxValue;

        return Mathf.Abs(transform.position.x - player.position.x);
    }

    /// <summary>
    /// 面向玩家
    /// </summary>
    public void FacePlayer()
    {
        if (player == null)
            return;

        float dx = player.position.x - transform.position.x;
        if (dx > 0)
            transform.localScale = new Vector3(-1, 1, 1);
        else if (dx < 0)
            transform.localScale = new Vector3(1, 1, 1);
    }

    /// <summary>
    /// 按水平移动方向更新朝向（与 FacePlayer 的 scale 约定一致）。
    /// </summary>
    public void FaceDirection(float direction)
    {
        float dir = Mathf.Sign(direction);
        if (Mathf.Approximately(dir, 0f))
            return;

        transform.localScale = new Vector3(dir > 0f ? -1f : 1f, 1f, 1f);
    }

    /// <summary>
    /// 移动方向前方是否仍有地面；PhysicsCheck 未配置时视为有地面。
    /// </summary>
    public bool HasGroundAhead(float moveDir)
    {
        if (physicsCheck == null || !IsPhysicsCheckConfigured())
            return true;

        return physicsCheck.HasGroundAhead(moveDir);
    }

    /// <summary>
    /// 遇墙壁或平台边缘时转身。仅因悬崖且反方向也无地面时不转身，避免边缘抖动。
    /// </summary>
    public bool TryFlipOnObstacleOrLedge(float moveDir)
    {
        if (physicsCheck == null || !IsPhysicsCheckConfigured())
            return false;

        bool hitWall = (physicsCheck.touchLeftWall && moveDir < 0f)
            || (physicsCheck.touchRightWall && moveDir > 0f);
        bool noGroundAhead = !physicsCheck.HasGroundAhead(moveDir);

        if (!hitWall && !noGroundAhead)
            return false;

        if (!hitWall && noGroundAhead && !physicsCheck.HasGroundAhead(-moveDir))
            return false;

        FaceDirection(-moveDir);
        return true;
    }

    /// <summary>
    /// 朝玩家水平移动
    /// </summary>
    public void MoveTowardPlayer()
    {
        if (player == null || isHurt || isDead || rb == null)
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
        if (isHurt || isDead || rb == null)
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

    protected void ApplyHorizontalMove(float direction)
    {
        if (rb == null)
            return;

        rb.linearVelocity = new Vector2(currentSpeed * direction, rb.linearVelocity.y);
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

    protected bool IsPhysicsCheckConfigured()
    {
        return physicsCheck != null
            && physicsCheck.checkRaduis > 0f
            && physicsCheck.groundLayer.value != 0;
    }

    /// <summary>
    /// 切换 AI 状态
    /// </summary>
    public virtual void SwitchState(NPCState state)
    {
        var newState = state switch
        {
            NPCState.Patrol => patroState,
            NPCState.Chase => chaseState,
            NPCState.GetClose => getCloseState,
            NPCState.Shot => shotState,
            NPCState.Move => moveState,
            NPCState.Crouch => crouchState,
            NPCState.CrouchShoot => crouchShootState,
            NPCState.Reload => reloadState,
            NPCState.Jump => jumpState,
            NPCState.Return => returnState,
            NPCState.MeleeAttack => meleeAttackState,
            NPCState.Skill => skillState,
            _ => null
        };

        if (newState == null)
            return;

        currentState?.OnExit();
        currentState = newState;
        currentState.OnEnter(this);
    }

    #region 事件执行方法

    /// <summary>
    /// 受到伤害时调用，转向攻击者并触发闪红与抖动硬直（推动由 Attack 负责）
    /// </summary>
    public void OnTakeDamage(Transform attackTrans)
    {
        attacker = attackTrans;

        if (attackTrans.position.x - transform.position.x > 0)
            transform.localScale = new Vector3(-1, 1, 1);
        if (attackTrans.position.x - transform.position.x < 0)
            transform.localScale = new Vector3(1, 1, 1);

        isHurt = true;
        anim.SetTrigger("hurt");

        rb.linearVelocity = new Vector2(0, rb.linearVelocity.y);

        if (hurtRoutine != null)
            StopCoroutine(hurtRoutine);
        RestoreHurtVisuals();
        hurtRoutine = StartCoroutine(OnHurt());

        if (isPatrol && !isAggro && !isDead)
            OnPatrolAggroFromDamage();
    }

    /// <summary>
    /// 巡逻待机时受伤拉仇恨。子类可覆盖以进入战斗循环。
    /// </summary>
    protected virtual void OnPatrolAggroFromDamage()
    {
        EnterPatrolCombat();
    }

    /// <summary>
    /// 受伤硬直：闪红 + 精灵抖动，结束后恢复（推动由 Attack.enableKnockback 施加）
    /// </summary>
    private IEnumerator OnHurt()
    {
        float duration = Mathf.Max(0.05f, hurtDuration);
        float elapsed = 0f;
        float flashTimer = 0f;
        bool flashOn = true;

        if (spriteRenderer != null)
            spriteRenderer.color = hurtFlashColor;

        while (elapsed < duration)
        {
            float dt = Time.deltaTime;
            elapsed += dt;
            flashTimer += dt;

            if (spriteRenderer != null && flashTimer >= hurtFlashInterval)
            {
                flashTimer = 0f;
                flashOn = !flashOn;
                spriteRenderer.color = flashOn ? hurtFlashColor : spriteOriginalColor;
            }

            if (spriteRenderer != null && hurtShakeIntensity > 0f)
            {
                Vector2 offset = Random.insideUnitCircle * hurtShakeIntensity;
                spriteRenderer.transform.localPosition = spriteOriginalLocalPos + (Vector3)offset;
            }

            yield return null;
        }

        RestoreHurtVisuals();
        isHurt = false;
        hurtRoutine = null;
    }

    void RestoreHurtVisuals()
    {
        if (spriteRenderer == null)
            return;

        spriteRenderer.color = spriteOriginalColor;
        spriteRenderer.transform.localPosition = spriteOriginalLocalPos;
    }

    /// <summary>
    /// 死亡处理，切换图层并播放死亡动画
    /// </summary>
    public void OnDie()
    {
        gameObject.layer = 2;
        anim.SetBool("dead", true);
        isDead = true;

        if (hurtRoutine != null)
        {
            StopCoroutine(hurtRoutine);
            hurtRoutine = null;
        }
        RestoreHurtVisuals();
        isHurt = false;

        TryDropAmmo();
        TryDropHealth();
    }

    void TryDropAmmo()
    {
        if (ammoDropped || !dropAmmoOnDeath || ammoDropPrefab == null)
            return;

        ammoDropped = true;
        Instantiate(ammoDropPrefab, transform.position + ammoDropOffset, Quaternion.identity);
    }

    void TryDropHealth()
    {
        if (healthDropped || !dropHealthOnDeath || healthDropPrefab == null)
            return;

        healthDropped = true;
        Instantiate(healthDropPrefab, transform.position + healthDropOffset, Quaternion.identity);
    }

    /// <summary>
    /// 死亡动画结束后销毁对象（由动画事件调用）
    /// </summary>
    public void DestroyAfterAnimation()
    {
        Destroy(gameObject);
    }

    #endregion

    private void OnDrawGizmosSelected()
    {
        Gizmos.DrawWireSphere(
            transform.position + new Vector3(checkDistance * faceDir.x, 0)
            + (Vector3)centerOffset + new Vector3(checkDistance * -transform.localScale.x, 0),
            0.2f);

        if (isPatrol && patrolDetectRange > 0f)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(transform.position, patrolDetectRange);
        }

        if (homeBounds != null)
        {
            Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.35f);
            var b = homeBounds.bounds;
            Gizmos.DrawWireCube(b.center, b.size);
        }

        if (dropAmmoOnDeath)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(transform.position + ammoDropOffset, 0.15f);
        }

        if (dropHealthOnDeath)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(transform.position + healthDropOffset, 0.15f);
        }
    }
}
