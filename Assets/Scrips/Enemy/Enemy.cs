using System.Collections;
using System.Collections.Generic;
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
    [Tooltip("仅进场走到 targetPoint 时生效；巡逻/追击不受影响")]
    public float spawnApproachSpeedScale = 1.5f;
    [HideInInspector] public float currentSpeed;
    [Tooltip("为 true 时镭射光束在此敌人处截断")]
    public bool blocksLaser;

    public Vector3 faceDir;
    /// <summary>
    /// 精灵默认朝右时为 true（新像素图）；旧 Metal Slug 资源朝左则为 false。
    /// </summary>
    protected virtual bool SpriteFacesRight => false;

    /// <summary>
    /// 为 false 时 ApplyFacing 不翻转 localScale（装甲车等永不转向）。
    /// </summary>
    protected virtual bool CanChangeFacing => true;
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
    [Tooltip("受击动画计数窗口时长（秒）；<=0 关闭保护")]
    public float hurtAnimWindow = 1f;
    [Tooltip("窗口内最多播放的 hurt 动画次数；<=0 关闭保护")]
    public int maxHurtAnimsPerWindow = 2;

    [Header("检测")]
    public Vector2 centerOffset;
    public Vector2 checkSize;
    public float checkDistance;
    public LayerMask attackLayer;

    [Header("间隔")]
    [Tooltip("关闭后不再做软间隔与站位槽")]
    public bool enableSeparation = true;
    [Tooltip("软间隔检测半径")]
    public float separationRadius = 1.15f;
    [Tooltip("软间隔推力强度")]
    public float separationStrength = 2.5f;
    [Tooltip("间隔修正的最大水平速度")]
    public float maxSeparationSpeed = 2f;
    [Tooltip("同侧战斗站位槽间距，叠加在 idealRange / shootRange / holdRange 上")]
    public float combatSlotSpacing = 0.75f;
    [Tooltip("参与站位槽分配的同伴水平范围，避免远处敌人把停点排得过远")]
    public float combatSlotGroupRadius = 8f;
    /// <summary>近战贴脸、飞扑、举盾等临时关闭间隔。</summary>
    [HideInInspector] public bool blockSeparation;
    /// <summary>本帧水平移动意图；贴边把速度清零后仍用来判断是否该被同伴往内侧挤开。</summary>
    float lastMoveIntentX;

    [Header("巡逻站岗")]
    [Tooltip("开启后原地 Idle，索敌范围内发现玩家才开战；自身超出驻守点脱战半径后回位")]
    public bool isPatrol;
    [Tooltip("全向索敌半径")]
    public float patrolDetectRange = 6f;
    [Tooltip("相对驻守点的脱战半径；敌人自身超出后停止追击并回位。<=0 关闭")]
    public float patrolLeashRange = 8f;
    [Tooltip("回位移速倍率（相对 normalSpeed，无则 chaseSpeed）")]
    public float returnHomeSpeedScale = 1.5f;
    [Tooltip("回位时位移不足判定卡住的时长（秒）；<=0 关闭")]
    public float returnStuckTimeout = 2f;
    [Tooltip("回位卡住判定的位移阈值")]
    public float returnStuckMoveThreshold = 0.08f;
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
    [HideInInspector] public bool isApproachingSpawnTarget;
    protected Vector3 spawnTargetPosition;

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

    [Header("濒死窗口")]
    [Tooltip("地面致死后等待该时长再播死亡动画；期间仍可受击")]
    [SerializeField] float groundDeathDelay = 0.4f;

    [Header("死亡闪烁")]
    [Tooltip("死亡动画结束前开始透明度闪烁的时长")]
    [SerializeField] float deathFlashDuration = 0.25f;
    [Tooltip("死亡闪烁切换间隔")]
    [SerializeField] float deathFlashInterval = 0.05f;

    [HideInInspector] public Transform player;
    [HideInInspector] public Vector3 homePosition;
    [HideInInspector] public Collider2D homeBounds;

    Vector3 returnStuckLastPos;
    float returnStuckTimer;

    bool ammoDropped;
    bool healthDropped;
    bool deathAnimStarted;
    float deathDelayTimer;
    HashSet<string> animBoolNames;

    const float MinAirDeathTime = 0.12f;
    const float LandedUpwardSpeedMax = 0.1f;
    const float DeathFlashFallbackClipLength = 0.75f;
    const string DieStateName = "Die";
    static readonly string[] CombatAnimBools =
    {
        "walk", "shoot", "shootDown", "shotPrep", "crouch", "reload",
        "melee", "meleeWindup", "throw", "jump", "fall", "land", "run",
        "missile", "ramWindup", "ram",
    };

    protected Character character;

    /// <summary>飞行敌人等可关闭濒死窗口，致死立刻播死亡动画。</summary>
    protected virtual bool UseDeathDelay => true;

    /// <summary>地面敌人在死亡动画结束前做透明度闪烁；飞行敌人可关闭。</summary>
    protected virtual bool UseDeathVanishFlash => true;

    /// <summary>存活或濒死窗口内可被攻击；死亡动画开始后不可。</summary>
    public bool IsHittable => !deathAnimStarted;

    private BaseState currentState;
    protected BaseState CurrentState => currentState;
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
    protected BaseState approachTargetState;
    protected BaseState departState;

    SpriteRenderer spriteRenderer;
    Color spriteOriginalColor = Color.white;
    Vector3 spriteOriginalLocalPos;
    Coroutine hurtRoutine;
    Coroutine noStunFlashRoutine;
    Coroutine deathFlashRoutine;
    float hurtAnimWindowExpireTime;
    int hurtAnimCountInWindow;

    public Rigidbody2D Rb => rb;

    bool persistDeath = true;
    bool removedBySave;

    /// <summary>场景预置敌人才写入死亡存档；Instantiate 出的 Clone 不记。</summary>
    public bool ShouldPersistDeath => persistDeath && !removedBySave && !name.EndsWith("(Clone)");

    /// <summary>刷怪器 / 触发器生成后调用，避免把遭遇战敌人写进关卡进度。</summary>
    public void MarkAsRuntimeSpawned()
    {
        persistDeath = false;
        var persist = GetComponent<EnemyDeathPersist>();
        if (persist != null)
            Destroy(persist);
    }

    /// <summary>读档时移除已击杀的场景敌人，不走 OnDie，避免重复掉落。</summary>
    public void RemoveBecauseSavedDead()
    {
        if (removedBySave)
            return;

        removedBySave = true;
        persistDeath = false;
        isDead = true;
        enabled = false;

        if (rb != null)
        {
            rb.linearVelocity = Vector2.zero;
            rb.simulated = false;
        }

        var renderers = GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] != null)
                renderers[i].enabled = false;
        }

        var colliders = GetComponentsInChildren<Collider2D>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            if (colliders[i] != null)
                colliders[i].enabled = false;
        }

        Destroy(gameObject);
    }

    protected virtual void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        anim = GetComponent<Animator>();
        physicsCheck = GetComponent<PhysicsCheck>();
        character = GetComponent<Character>();
        CacheSpriteRenderer();
        CacheAnimBoolNames();

        rb.constraints = RigidbodyConstraints2D.FreezeRotation;
        currentSpeed = normalSpeed;

        EnsurePlayerReference();
        CacheHome();

        approachTargetState = new EnemyApproachTargetState();

        if (ShouldPersistDeath && GetComponent<EnemyDeathPersist>() == null)
            gameObject.AddComponent<EnemyDeathPersist>();

        EnemySeparation.Register(this);
    }

    protected void CacheSpriteRenderer()
    {
        RecacheSpriteRendererFromChild("Sprite");
    }

    /// <summary>
    /// 优先绑定名为 childName 的身体 Sprite，避免盾/占位渲染器抢走闪白目标。
    /// </summary>
    protected void RecacheSpriteRendererFromChild(string childName)
    {
        spriteRenderer = null;
        if (!string.IsNullOrEmpty(childName))
        {
            Transform child = transform.Find(childName);
            if (child != null)
                spriteRenderer = child.GetComponent<SpriteRenderer>();
        }

        if (spriteRenderer == null)
            spriteRenderer = GetComponentInChildren<SpriteRenderer>();

        if (spriteRenderer == null)
            return;

        spriteOriginalColor = spriteRenderer.color;
        spriteOriginalLocalPos = spriteRenderer.transform.localPosition;
    }

    protected void RecacheAnimBoolNames()
    {
        CacheAnimBoolNames();
    }

    void CacheAnimBoolNames()
    {
        animBoolNames = new HashSet<string>();
        if (anim == null)
            return;

        foreach (var parameter in anim.parameters)
        {
            if (parameter.type == AnimatorControllerParameterType.Bool)
                animBoolNames.Add(parameter.name);
        }
    }

    /// <summary>仅当当前 Animator Controller 存在该 Bool 时设置，避免 Parameter does not exist 刷屏。</summary>
    public void SetAnimBool(string name, bool value)
    {
        if (anim == null || string.IsNullOrEmpty(name))
            return;
        if (animBoolNames == null || !animBoolNames.Contains(name))
            return;

        anim.SetBool(name, value);
    }

    public bool IsNamedAnimFinished(string stateName)
    {
        if (anim == null || anim.runtimeAnimatorController == null || string.IsNullOrEmpty(stateName))
            return false;

        AnimatorStateInfo info = anim.GetCurrentAnimatorStateInfo(0);
        return info.IsName(stateName) && info.normalizedTime >= 1f;
    }

    public bool IsNamedAnimPlaying(string stateName)
    {
        if (anim == null || anim.runtimeAnimatorController == null || string.IsNullOrEmpty(stateName))
            return false;

        return anim.GetCurrentAnimatorStateInfo(0).IsName(stateName);
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
    /// 玩家是否在该类战斗射程内。默认无射程；远程/近战/飞行子类覆盖。
    /// </summary>
    public virtual bool IsPlayerInCombatRange()
    {
        return false;
    }

    /// <summary>
    /// 受击是否足以拉入战斗：玩家须在索敌范围或该类射程内。
    /// </summary>
    public bool CanAggroFromDamage()
    {
        return IsPlayerInPatrolRange() || IsPlayerInCombatRange();
    }

    /// <summary>
    /// 玩家是否仍在敌人所属 Bounds 内。未绑定 Bounds 时视为始终在内。
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
    /// 敌人自身是否超出驻守点脱战半径。半径 &lt;= 0 时不按此条件脱战。
    /// </summary>
    public bool IsOutsidePatrolLeash()
    {
        if (patrolLeashRange <= 0f)
            return false;

        return Vector2.Distance(transform.position, homePosition) > patrolLeashRange;
    }

    /// <summary>
    /// 驻守开战中且自身已超出脱战半径，应停止追击并回位。
    /// </summary>
    public bool ShouldBeginPatrolReturn()
    {
        return isPatrol && isAggro && !isDead && !isReturning && !isApproachingSpawnTarget
            && IsOutsidePatrolLeash();
    }

    /// <summary>回位移速（normal/chase × returnHomeSpeedScale）。</summary>
    public float GetReturnHomeSpeed()
    {
        float baseSpeed = normalSpeed > 0f ? normalSpeed : chaseSpeed;
        float scale = returnHomeSpeedScale > 0f ? returnHomeSpeedScale : 1f;
        return baseSpeed * scale;
    }

    protected void ApplyReturnHomeStart(bool clearVerticalVelocity)
    {
        isAggro = false;
        isReturning = true;
        wait = false;
        isHurt = false;
        StopHurtVisualRoutines();
        RestoreHurtVisuals();
        ResetReturnStuckTracking();
        SetReturnInvulnerable(true);
        currentSpeed = GetReturnHomeSpeed();

        if (character != null)
            character.RestoreFullHealth();

        if (rb != null)
            rb.linearVelocity = clearVerticalVelocity
                ? Vector2.zero
                : new Vector2(0f, rb.linearVelocity.y);
    }

    protected void ApplyReturnHomeEnd()
    {
        isAggro = false;
        isReturning = false;
        ResetReturnStuckTracking();
        SetReturnInvulnerable(false);
    }

    void SetReturnInvulnerable(bool value)
    {
        if (character != null)
            character.SetForcedInvulnerable(value);
    }

    void ResetReturnStuckTracking()
    {
        returnStuckLastPos = transform.position;
        returnStuckTimer = 0f;
    }

    bool TickReturnStuck()
    {
        if (returnStuckTimeout <= 0f)
            return false;

        float moved = Vector2.Distance(transform.position, returnStuckLastPos);
        if (moved < Mathf.Max(0f, returnStuckMoveThreshold))
        {
            returnStuckTimer += Time.deltaTime;
            return returnStuckTimer >= returnStuckTimeout;
        }

        returnStuckTimer = 0f;
        returnStuckLastPos = transform.position;
        return false;
    }

    /// <summary>
    /// 脱战：回满血、无敌，并以加速进入回位状态。
    /// </summary>
    public virtual void BeginReturnHome()
    {
        if (isDead || isReturning || isApproachingSpawnTarget)
            return;

        ApplyReturnHomeStart(clearVerticalVelocity: false);

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
        ApplyReturnHomeEnd();
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
        if (!isPatrol || isDead || isAggro || isReturning)
            return;

        isAggro = true;
    }

    /// <summary>
    /// 遭遇刷怪条目覆盖专注模式。默认无效果；远程与盾兵子类写入 enableFocusMode。
    /// </summary>
    public virtual void ApplyEncounterFocusMode(bool enabled)
    {
    }

    /// <summary>
    /// 生成后先走到目标点；已在点上则直接进入战斗/巡逻。
    /// </summary>
    public void BeginSpawnApproach(Vector3 worldPosition)
    {
        if (isDead)
            return;

        spawnTargetPosition = worldPosition;
        isApproachingSpawnTarget = true;
        isReturning = false;
        isAggro = false;
        SetReturnInvulnerable(false);

        if (HasReachedSpawnTarget())
        {
            FinishSpawnApproach();
            return;
        }

        SwitchState(NPCState.ApproachTarget);
    }

    public virtual bool HasReachedSpawnTarget()
    {
        return Mathf.Abs(spawnTargetPosition.x - transform.position.x) <= returnArriveDistance;
    }

    /// <summary>进场走到 targetPoint 时的移速（normal/chase × spawnApproachSpeedScale）。</summary>
    public float GetSpawnApproachSpeed()
    {
        float baseSpeed = normalSpeed > 0f ? normalSpeed : chaseSpeed;
        float scale = spawnApproachSpeedScale > 0f ? spawnApproachSpeedScale : 1f;
        return baseSpeed * scale;
    }

    public virtual void MoveTowardSpawnTarget()
    {
        float dx = spawnTargetPosition.x - transform.position.x;
        if (Mathf.Abs(dx) <= returnArriveDistance)
            return;

        float dir = Mathf.Sign(dx);
        if (dir == 0f)
            return;

        currentSpeed = GetSpawnApproachSpeed();
        MoveHorizontal(dir);
        TryFlipOnObstacle(dir);
        FaceDirection(dir);
    }

    /// <summary>
    /// 抵达目标点：把出生点改到此处，再进入原本的开战/巡逻逻辑。
    /// </summary>
    public void FinishSpawnApproach()
    {
        if (isDead)
            return;

        isApproachingSpawnTarget = false;
        SnapToSpawnTarget();
        CacheHome();
        OnSpawnApproachFinished();
        EnterPostSpawnBehavior();
    }

    protected virtual void SnapToSpawnTarget()
    {
        Vector3 pos = transform.position;
        pos.x = spawnTargetPosition.x;
        transform.position = pos;

        if (rb != null)
            rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
    }

    protected virtual void OnSpawnApproachFinished()
    {
    }

    /// <summary>
    /// 遭遇覆盖专注/目标点后，重新进入巡逻或战斗循环。
    /// </summary>
    public virtual void EnterPostSpawnBehavior()
    {
        if (isDead)
            return;

        if (isPatrol)
        {
            isAggro = false;
            SwitchState(NPCState.Patrol);
            return;
        }

        isAggro = true;
        StartCombatCycle();
    }

    /// <summary>非巡逻开战入口。近战/远程/飞行/装甲车子类覆盖为 EvaluateCycle。</summary>
    protected virtual void StartCombatCycle()
    {
    }

    protected virtual void OnEnable()
    {
        EnemySeparation.Register(this);
        blockSeparation = false;
        currentState = GetInitialState();
        currentState.OnEnter(this);
    }

    protected virtual BaseState GetInitialState() => patroState;

    protected virtual void Update()
    {
        EnsurePlayerReference();
        faceDir = new Vector3(GetFacingFromScale(), 0, 0);

        if (ShouldBeginPatrolReturn())
            BeginReturnHome();

        currentState.LogicUpdate();

        if (isReturning && !isDead && TickReturnStuck())
            FinishPatrolReset();

        UpdateDeathDelay();

        if (ShouldRunTimeCounter())
            TimeCounter();
    }

    protected virtual void FixedUpdate()
    {
        if (ShouldAutoMove())
            Move();

        currentState.PhysicsUpdate();
        ApplyPostMoveSeparation();
    }

    protected virtual bool ShouldRunTimeCounter() => true;

    protected virtual bool ShouldAutoMove() => !isHurt && !isDead && !wait;

    protected virtual void OnDisable()
    {
        SetReturnInvulnerable(false);
        currentState?.OnExit();
        EnemySeparation.Unregister(this);
    }

    protected virtual void OnDestroy()
    {
        EnemySeparation.Unregister(this);
    }

    /// <summary>子类 OnEnable 未调用 base 时仍需登记间隔。</summary>
    protected void RegisterSeparation()
    {
        EnemySeparation.Register(this);
        blockSeparation = false;
    }

    /// <summary>
    /// 按当前朝向与速度移动；贴地且前方无地面时停步，避免自动巡逻坠崖。
    /// </summary>
    public virtual void Move()
    {
        float dir = faceDir.x;
        lastMoveIntentX = dir;
        if (!Mathf.Approximately(dir, 0f) && IsLedgeBlocking(dir))
        {
            rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
            return;
        }

        rb.linearVelocity = new Vector2(currentSpeed * dir * GetMoveSpeedScale() * Time.deltaTime, rb.linearVelocity.y);
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
                ApplyFacing(-faceDir.x);
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

        ApplyFacing(player.position.x - transform.position.x);
    }

    /// <summary>
    /// 按水平移动方向更新朝向（与 FacePlayer 的 scale 约定一致）。
    /// </summary>
    public void FaceDirection(float direction)
    {
        ApplyFacing(direction);
    }

    /// <summary>
    /// 按世界水平方向设置朝向。正值朝右，负值朝左。
    /// </summary>
    public void ApplyFacing(float worldDirX)
    {
        if (!CanChangeFacing)
            return;

        float dir = Mathf.Sign(worldDirX);
        if (Mathf.Approximately(dir, 0f))
            return;

        float scaleX = SpriteFacesRight ? dir : -dir;
        Vector3 scale = transform.localScale;
        transform.localScale = new Vector3(scaleX, scale.y, scale.z);
    }

    float GetFacingFromScale()
    {
        float sx = transform.localScale.x;
        if (Mathf.Approximately(sx, 0f))
            sx = 1f;
        return (SpriteFacesRight ? 1f : -1f) * Mathf.Sign(sx);
    }

    /// <summary>
    /// 移动方向前方是否仍有地面；PhysicsCheck 未配置时视为有地面。
    /// 单向平台上、或已离开实心地面过久时视为始终有地面，允许走下平台。
    /// </summary>
    public bool HasGroundAhead(float moveDir)
    {
        if (physicsCheck == null || !IsPhysicsCheckConfigured())
            return true;
        if (!physicsCheck.ShouldRespectLedge)
            return true;

        return physicsCheck.HasGroundAhead(moveDir);
    }

    /// <summary>
    /// 贴地移动时前方是否为悬崖。单向平台上不拦截；刚离开实心地面的短窗口内仍拦截。
    /// </summary>
    public bool IsLedgeBlocking(float moveDir)
    {
        if (physicsCheck == null || !IsPhysicsCheckConfigured())
            return false;
        if (Mathf.Approximately(moveDir, 0f) || !physicsCheck.ShouldRespectLedge)
            return false;

        return !physicsCheck.HasGroundAhead(moveDir);
    }

    /// <summary>
    /// 遇墙壁或实心地面边缘时转身。单向平台上不因边缘转身。
    /// 仅因悬崖且反方向也无地面时不转身，避免边缘抖动。
    /// </summary>
    public bool TryFlipOnObstacleOrLedge(float moveDir)
    {
        if (physicsCheck == null || !IsPhysicsCheckConfigured())
            return false;

        bool hitWall = (physicsCheck.touchLeftWall && moveDir < 0f)
            || (physicsCheck.touchRightWall && moveDir > 0f);
        bool noGroundAhead = physicsCheck.ShouldRespectLedge && !physicsCheck.HasGroundAhead(moveDir);

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
    /// 朝同侧战斗站位槽移动；已到位则停步并面向玩家。
    /// </summary>
    public void MoveTowardCombatSlot(float baseRange)
    {
        if (player == null || isHurt || isDead || rb == null)
            return;

        float dir = GetCombatSlotMoveDir(baseRange);
        if (Mathf.Approximately(dir, 0f))
        {
            ApplyHorizontalMove(0f);
            FacePlayer();
            return;
        }

        ApplyHorizontalMove(dir);
        FacePlayer();
    }

    public float GetCombatSlotMoveDir(float baseRange)
    {
        if (player == null)
            return GetMoveDirTowardPlayer();

        float slotted = GetSlottedRange(baseRange);
        int side = EnemySeparation.GetCombatSide(this);
        float desiredX = player.position.x + side * slotted;
        float dx = desiredX - transform.position.x;
        if (Mathf.Abs(dx) <= 0.08f)
            return 0f;

        return Mathf.Sign(dx);
    }

    public float GetSlottedRange(float baseRange)
    {
        if (!enableSeparation)
            return baseRange;
        return EnemySeparation.GetSlottedRange(this, baseRange);
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
        lastMoveIntentX = direction;
        if (rb == null)
            return;

        // 追击 / 回位 / 走位共用：贴地且前方无地面则清零水平速度，防止坠崖
        if (!Mathf.Approximately(direction, 0f) && IsLedgeBlocking(direction))
        {
            rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
            return;
        }

        rb.linearVelocity = new Vector2(currentSpeed * GetMoveSpeedScale() * direction, rb.linearVelocity.y);
    }

    /// <summary>移动速度额外倍率。盾兵持盾时可覆盖为减速。</summary>
    public virtual float GetMoveSpeedScale() => 1f;

    public virtual bool ShouldApplySeparation =>
        enableSeparation && !blockSeparation && !isHurt && !isDead && !isApproachingSpawnTarget && IsHittable;

    public virtual float GetSeparationScale() => isReturning ? 0.35f : 1f;

    protected virtual void ApplyPostMoveSeparation()
    {
        if (rb == null || !ShouldApplySeparation)
            return;

        float vx = MixGroundSeparation(rb.linearVelocity.x);
        rb.linearVelocity = new Vector2(vx, rb.linearVelocity.y);
    }

    protected float MixGroundSeparation(float currentVx)
    {
        float correction = EnemySeparation.ComputeGroundCorrectionX(this) * GetSeparationScale();
        correction = Mathf.Clamp(correction, -maxSeparationSpeed, maxSeparationSpeed);

        if (correction > 0.01f && IsSeparationBlocked(1f))
            correction = 0f;
        else if (correction < -0.01f && IsSeparationBlocked(-1f))
            correction = 0f;

        bool wantedMove = Mathf.Abs(lastMoveIntentX) > 0.01f;
        bool jammedAtEdge = wantedMove && IsLedgeBlocking(lastMoveIntentX);

        // 攻击 / 就位等真正站定时不推；贴边停步时仍允许被往内侧挤开
        if (Mathf.Abs(currentVx) <= 0.05f && !jammedAtEdge)
            return currentVx;

        if (Mathf.Abs(correction) <= 0.01f)
            return currentVx;

        if (jammedAtEdge)
            return correction;

        float vx = currentVx + correction;
        // 只减速让后排，不给前排加速，避免把领头人顶进悬崖 / 台阶
        if (currentVx > 0.05f)
            vx = Mathf.Clamp(vx, 0f, currentVx);
        else if (currentVx < -0.05f)
            vx = Mathf.Clamp(vx, currentVx, 0f);

        return vx;
    }

    protected bool IsSeparationBlocked(float direction)
    {
        if (Mathf.Approximately(direction, 0f))
            return false;
        if (IsLedgeBlocking(direction))
            return true;
        if (physicsCheck == null)
            return false;
        if (direction > 0f && physicsCheck.touchRightWall)
            return true;
        if (direction < 0f && physicsCheck.touchLeftWall)
            return true;
        return false;
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
            ApplyFacing(-moveDir);
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
            NPCState.Ram => skillState,
            NPCState.ApproachTarget => approachTargetState,
            NPCState.Depart => departState,
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
    /// 为 false 时受击不播 hurt、不进 isHurt 硬直（装甲车等只闪红）。
    /// </summary>
    protected virtual bool UseHurtStun => true;

    /// <summary>
    /// 受到伤害时调用，转向攻击者并触发闪红与抖动硬直（推动由 Attack 负责）
    /// </summary>
    public virtual void OnTakeDamage(Transform attackTrans)
    {
        if (isReturning)
            return;

        attacker = attackTrans;

        bool outOfAggroRange = isPatrol && !isAggro && !CanAggroFromDamage();

        if (!outOfAggroRange)
            ApplyFacing(attackTrans.position.x - transform.position.x);

        if (outOfAggroRange)
            PlayHitFeedbackNoStun();
        else if (UseHurtStun && (isDead || TryConsumeHurtAnim()))
            PlayFullHurtReaction();
        else
            PlayCombatFlashNoStun();

        if (isPatrol && !isAggro && !isDead && !isReturning && !isApproachingSpawnTarget && CanAggroFromDamage())
            OnPatrolAggroFromDamage();
    }

    bool TryConsumeHurtAnim()
    {
        if (hurtAnimWindow <= 0f || maxHurtAnimsPerWindow <= 0)
            return true;

        if (Time.time >= hurtAnimWindowExpireTime)
        {
            hurtAnimCountInWindow = 0;
            hurtAnimWindowExpireTime = Time.time + hurtAnimWindow;
        }

        if (hurtAnimCountInWindow >= maxHurtAnimsPerWindow)
            return false;

        hurtAnimCountInWindow++;
        return true;
    }

    void PlayFullHurtReaction()
    {
        isHurt = true;
        if (anim != null)
            anim.SetTrigger("hurt");

        if (rb != null)
            rb.linearVelocity = new Vector2(0, rb.linearVelocity.y);

        StopHurtVisualRoutines();
        RestoreHurtVisuals();
        hurtRoutine = StartCoroutine(OnHurt());
    }

    /// <summary>
    /// 窗口内超限：只闪红抖动，不播 hurt、不进 isHurt。已在硬直中则不打断。
    /// </summary>
    void PlayCombatFlashNoStun()
    {
        if (hurtRoutine != null || isHurt)
            return;

        if (noStunFlashRoutine != null)
            StopCoroutine(noStunFlashRoutine);
        RestoreHurtVisuals();
        noStunFlashRoutine = StartCoroutine(CombatFlashNoStun());
    }

    void StopHurtVisualRoutines()
    {
        if (hurtRoutine != null)
        {
            StopCoroutine(hurtRoutine);
            hurtRoutine = null;
        }
        if (noStunFlashRoutine != null)
        {
            StopCoroutine(noStunFlashRoutine);
            noStunFlashRoutine = null;
        }
    }

    /// <summary>
    /// 陷阱受击反馈：仅闪红/轻抖，不设 isHurt、不打断 AI。
    /// </summary>
    public void PlayHitFeedbackNoStun()
    {
        if (isDead)
            return;

        if (noStunFlashRoutine != null)
            StopCoroutine(noStunFlashRoutine);
        RestoreHurtVisuals();
        noStunFlashRoutine = StartCoroutine(HitFeedbackNoStun());
    }

    IEnumerator HitFeedbackNoStun()
    {
        yield return FlashAndShake(
            Mathf.Min(0.2f, Mathf.Max(0.05f, hurtDuration)),
            hurtShakeIntensity * 0.5f);
        noStunFlashRoutine = null;
    }

    IEnumerator CombatFlashNoStun()
    {
        yield return FlashAndShake(Mathf.Max(0.05f, hurtDuration), hurtShakeIntensity);
        noStunFlashRoutine = null;
    }

    /// <summary>
    /// 巡逻待机时受伤拉仇恨。子类可覆盖以进入战斗循环。
    /// </summary>
    protected virtual void OnPatrolAggroFromDamage()
    {
        if (isReturning || isApproachingSpawnTarget || isDead)
            return;

        EnterPatrolCombat();
    }

    /// <summary>
    /// 受伤硬直：闪红 + 精灵抖动，结束后恢复（推动由 Attack.enableKnockback 施加）
    /// </summary>
    private IEnumerator OnHurt()
    {
        yield return FlashAndShake(Mathf.Max(0.05f, hurtDuration), hurtShakeIntensity);
        isHurt = false;
        hurtRoutine = null;
    }

    IEnumerator FlashAndShake(float duration, float shakeIntensity)
    {
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

            if (spriteRenderer != null && shakeIntensity > 0f)
            {
                Vector2 offset = Random.insideUnitCircle * shakeIntensity;
                spriteRenderer.transform.localPosition = spriteOriginalLocalPos + (Vector3)offset;
            }

            yield return null;
        }

        RestoreHurtVisuals();
    }

    void RestoreHurtVisuals()
    {
        if (spriteRenderer == null)
            return;

        spriteRenderer.color = spriteOriginalColor;
        spriteRenderer.transform.localPosition = spriteOriginalLocalPos;
    }

    /// <summary>
    /// 死亡处理：逻辑死亡立刻生效；地面等短倒计时、空中等落地后再播死亡动画。
    /// </summary>
    public void OnDie()
    {
        isDead = true;
        isReturning = false;
        SetReturnInvulnerable(false);
        if (ShouldPersistDeath)
            EnemyDeathProgress.MarkKilled(EnemyDeathPersist.BuildProgressKey(this));
        TryDropAmmo();
        TryDropHealth();
        ClearCombatAnimatorBools();

        bool skipDelay = !UseDeathDelay
            || (character != null && character.ConsumeSkipDeathDelay());
        if (skipDelay)
        {
            PlayDeathAnim();
            return;
        }

        if (character != null)
            character.allowHitsWhileDead = true;

        bool grounded = physicsCheck != null
            && (physicsCheck.isSolidGround || physicsCheck.isGround);
        bool movingVertically = rb != null
            && Mathf.Abs(rb.linearVelocity.y) > LandedUpwardSpeedMax;
        bool airborne = !grounded || movingVertically;
        deathDelayTimer = airborne ? MinAirDeathTime : Mathf.Max(0f, groundDeathDelay);
    }

    void UpdateDeathDelay()
    {
        if (!isDead || deathAnimStarted)
            return;

        deathDelayTimer -= Time.deltaTime;
        if (deathDelayTimer <= 0f && IsLandedForDeathAnim())
            PlayDeathAnim();
    }

    bool IsLandedForDeathAnim()
    {
        if (physicsCheck == null)
            return true;

        float vy = rb != null ? rb.linearVelocity.y : 0f;
        if (vy > LandedUpwardSpeedMax)
            return false;

        if (physicsCheck.isSolidGround || physicsCheck.isGround)
            return true;

        // 刚体休眠后 OnCollisionStay 不再刷新接地；竖直速度已静止则视为落地/卡住
        return Mathf.Abs(vy) <= LandedUpwardSpeedMax;
    }

    /// <summary>立刻切死亡动画并停止受击（DeathZone 等可外部调用）。</summary>
    public void PlayDeathAnim()
    {
        if (deathAnimStarted)
            return;

        deathAnimStarted = true;
        isDead = true;

        if (character != null)
        {
            character.allowHitsWhileDead = false;
            character.ConsumeSkipDeathDelay();
        }

        gameObject.layer = 2;
        SetAnimBool("dead", true);

        StopHurtVisualRoutines();
        RestoreHurtVisuals();
        isHurt = false;

        if (UseDeathVanishFlash)
            deathFlashRoutine = StartCoroutine(DeathVanishFlash());
    }

    void ClearCombatAnimatorBools()
    {
        if (anim == null || animBoolNames == null)
            return;

        for (int i = 0; i < CombatAnimBools.Length; i++)
        {
            string name = CombatAnimBools[i];
            if (animBoolNames.Contains(name))
                anim.SetBool(name, false);
        }
    }

    /// <summary>
    /// 由生成器覆盖本实例死亡掉落；未勾选的种类会关闭，避免预制体默认值泄漏。
    /// </summary>
    public void ApplyDropOverride(bool dropAmmo, GameObject ammoPrefab, bool dropHealth, GameObject healthPrefab)
    {
        dropAmmoOnDeath = dropAmmo;
        ammoDropPrefab = dropAmmo ? ammoPrefab : null;
        dropHealthOnDeath = dropHealth;
        healthDropPrefab = dropHealth ? healthPrefab : null;
    }

    void TryDropAmmo()
    {
        if (ammoDropped || !dropAmmoOnDeath || ammoDropPrefab == null)
            return;

        ammoDropped = true;
        var instance = Instantiate(ammoDropPrefab, transform.position + ammoDropOffset, Quaternion.identity);
        PickupDelay.Arm(instance);
        EnemySceneCleanup.PlaceInSourceScene(instance, this);
    }

    void TryDropHealth()
    {
        if (healthDropped || !dropHealthOnDeath || healthDropPrefab == null)
            return;

        healthDropped = true;
        var instance = Instantiate(healthDropPrefab, transform.position + healthDropOffset, Quaternion.identity);
        PickupDelay.Arm(instance);
        EnemySceneCleanup.PlaceInSourceScene(instance, this);
    }

    /// <summary>
    /// 死亡动画结束后销毁对象（由动画事件调用）
    /// </summary>
    public void DestroyAfterAnimation()
    {
        StopDeathFlash();
        Destroy(gameObject);
    }

    IEnumerator DeathVanishFlash()
    {
        float duration = Mathf.Max(0f, deathFlashDuration);
        float interval = Mathf.Max(0.01f, deathFlashInterval);
        float fallbackWait = Mathf.Max(0f, DeathFlashFallbackClipLength - duration);

        if (anim != null)
        {
            float waited = 0f;
            const float maxWaitForDie = 0.5f;
            while (waited < maxWaitForDie && !IsInDieState())
            {
                waited += Time.deltaTime;
                yield return null;
            }

            if (IsInDieState())
            {
                while (GetDieRemainingTime() > duration)
                    yield return null;
            }
            else
            {
                yield return new WaitForSeconds(fallbackWait);
            }
        }
        else
        {
            yield return new WaitForSeconds(fallbackWait);
        }

        bool visible = false;
        float flashTimer = 0f;
        SetDeathFlashVisible(false);
        while (true)
        {
            flashTimer += Time.deltaTime;
            if (flashTimer >= interval)
            {
                flashTimer = 0f;
                visible = !visible;
                SetDeathFlashVisible(visible);
            }

            yield return null;
        }
    }

    bool IsInDieState()
    {
        return anim != null && anim.GetCurrentAnimatorStateInfo(0).IsName(DieStateName);
    }

    float GetDieRemainingTime()
    {
        if (anim == null)
            return 0f;

        AnimatorStateInfo info = anim.GetCurrentAnimatorStateInfo(0);
        return info.length * (1f - info.normalizedTime);
    }

    void SetDeathFlashVisible(bool visible)
    {
        if (spriteRenderer == null)
            return;

        Color c = spriteOriginalColor;
        c.a = visible ? spriteOriginalColor.a : 0f;
        spriteRenderer.color = c;
    }

    void StopDeathFlash()
    {
        if (deathFlashRoutine == null)
            return;

        StopCoroutine(deathFlashRoutine);
        deathFlashRoutine = null;
    }

    #endregion

    private void OnDrawGizmosSelected()
    {
        Gizmos.DrawWireSphere(
            transform.position + new Vector3(checkDistance * faceDir.x, 0)
            + (Vector3)centerOffset + new Vector3(checkDistance * -transform.localScale.x, 0),
            0.2f);

        DrawPatrolGizmos();

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

    protected void DrawPatrolGizmos()
    {
        if (!isPatrol)
            return;

        if (patrolDetectRange > 0f)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(transform.position, patrolDetectRange);
        }

        if (patrolLeashRange > 0f)
        {
            Vector3 origin = Application.isPlaying ? homePosition : transform.position;
            Gizmos.color = new Color(1f, 0.55f, 0.1f, 0.9f);
            Gizmos.DrawWireSphere(origin, patrolLeashRange);
        }

        if (homeBounds != null)
        {
            Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.35f);
            var b = homeBounds.bounds;
            Gizmos.DrawWireCube(b.center, b.size);
        }
    }
}
