using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// AE-74 机器人 BOSS：权重循环 火炮 / 近战冲刺 / 机枪 / 战术跳跃，招式后进入可接近的冷却。
/// 受击只闪红；金属受击与爆炸死亡音。
/// </summary>
public class AE74Enemy : Enemy
{
    const float ProbabilityStep = 0.1f;
    const int PlatformOverlapBufferSize = 16;

    protected override bool SpriteFacesRight => true;
    protected override bool CanChangeFacing => !facingLocked;
    protected override bool UseHurtStun => false;
    protected override bool UseMetalHitSfx => true;
    protected override bool UseExplodeDeathSfx => true;

    protected override void CacheSpriteRenderer()
    {
        RecacheSpriteRendererFromChild("Visual");
    }

    [Header("招式开关（精英拆分预留）")]
    public bool enableCannon = true;
    public bool enableMeleeDash = true;
    public bool enableMachineGun = true;
    public bool enableTacticalJump = true;

    [Header("行为权重（初始）")]
    [Min(0f)] public float cannonWeight = 0.25f;
    [Min(0f)] public float meleeDashWeight = 0.25f;
    [Min(0f)] public float machineGunWeight = 0.25f;
    [Min(0f)] public float tacticalJumpWeight = 0.25f;

    [Header("冷却 / 射程")]
    [Tooltip("每招结束后的冷却（秒）；超时无论是否在射程内都掷下一招")]
    public float actionCooldown = 1.2f;
    [Tooltip("冷却中超出此距离则 Run 接近")]
    public float engageRange = 8f;
    public float moveSpeed = 3.5f;

    [Header("招式 A · 直射火炮")]
    public EnemyMissile cannonMissilePrefab;
    public float cannonWindup = 0.6f;
    [Tooltip("仅当与玩家 |ΔY| 低于此值时火炮进入掷骰池")]
    public float cannonMaxYDelta = 1.6f;
    public Transform cannonFirePoint1;
    public Transform cannonFirePoint2;
    [Tooltip("两发直射导弹之间的间隔（秒）")]
    [Min(0f)] public float cannonBurstInterval = 0.2f;

    [Header("招式 B · 近战 / 冲刺")]
    public float meleeRange = 1.6f;
    [Tooltip("玩家进入此距离时提高近战冲刺权重")]
    public float meleeWeightRange = 3.5f;
    [Min(1f)] public float meleeCloseWeightMultiplier = 1.8f;
    public float dashSpeed = 10f;
    public float dashStartDuration = 0.4f;
    public float dashArriveDistance = 0.25f;
    public float dashTimeout = 1.6f;
    public float meleeHitboxFromNormalized = 0.45f;
    public int meleeDamage = 18;
    public EnemyHomingMissile dashMissilePrefab;
    public Transform missileFirePoint1;
    public Transform missileFirePoint2;
    public float dashMissileAscentDuration = 0.45f;
    public string meleeStateName = "RobotWhite_MeleeAttack";

    [Header("招式 C · 机枪扫射")]
    public Transform gunBase;
    public Transform firePoint;
    public EnemyProjectile projectilePrefab;
    [Tooltip("瞄准阶段时长（秒），期间炮口转向玩家")]
    public float mgAimDuration = 0.45f;
    [Tooltip("每波连射的子弹数")]
    public int mgBurstCount = 5;
    [Tooltip("一次机枪行动内瞄准→射击循环次数下限")]
    public int mgCycleMin = 3;
    [Tooltip("一次机枪行动内瞄准→射击循环次数上限")]
    public int mgCycleMax = 5;
    public float mgFireInterval = 0.16f;
    public float mgReloadStun = 2f;
    public float mgAimHeightOffset = 0f;

    [Header("招式 D · 战术跳跃")]
    public float jumpHeight = 4f;
    public float jumpTakeoffDuration = 0.25f;
    public float flySpeed = 7f;
    public float stompHoverHeight = 3.2f;
    public float stompFallSpeed = 18f;
    [Tooltip("空中瞄准阶段时长（秒），期间炮口转向玩家")]
    public float airAimDuration = 0.45f;
    [Tooltip("空中每波连射的子弹数")]
    public int airBurstCount = 5;
    [Tooltip("空中机枪瞄准→射击循环次数下限")]
    public int airCycleMin = 3;
    [Tooltip("空中机枪瞄准→射击循环次数上限")]
    public int airCycleMax = 5;
    [Tooltip("空中同一波内两发间隔")]
    public float airFireInterval = 0.16f;
    [Tooltip("空中每发的左右散射角（度），中间一发沿锁定方向")]
    public float airScatterAngle = 35f;
    [Tooltip("空中射击结束后、开始飞向落点/下砸前的最短悬停时间（秒）")]
    public float airPostShootHold = 1f;
    [Tooltip("飞向射击顶点超过此时长则放弃顶点，在当前位置进入空中射击")]
    [Min(0.05f)] public float apexFlyTimeout = 2f;
    [Tooltip("飞向落点或下砸超过此时长则放弃原目标，从当前位置正下方砸向最近地面/平台")]
    [Min(0.05f)] public float stompTimeout = 2f;
    public float heightBiasThreshold = 1.8f;
    public float heightBiasDuration = 2.2f;
    [Min(1f)] public float heightBiasWeightMultiplier = 1.8f;
    public float stompDamageRadius = 1.4f;
    public int stompDamage = 22;
    public float landImpactDuration = 0.45f;
    public string landEndStateName = "RobotWhite_LandAttack_End";
    public GameObject stompHitbox;
    public AE74Shockwave shockwavePrefab;
    public Transform shockwavePoint;
    public float shockwaveSpeed = 6f;
    public float shockwaveLifetime = 0.45f;
    public float landingRayStartOffsetY = 2f;
    public float landingRayDistance = 30f;

    [Header("Hitbox")]
    public GameObject meleeAttacker;

    [Header("单向平台")]
    [Tooltip("移动/冲刺时不把单向板当障碍；仍可站在板顶")]
    public bool passOneWayPlatformsWhileMoving = true;
    [Tooltip("脚底需高于平台顶面多少才算越过表面，避免贴边抖动")]
    [SerializeField] float oneWaySurfaceMargin = 0.05f;
    [Tooltip("冲刺时相对身体扩大的预扫描，避免高速撞板前未 Ignore")]
    [SerializeField] float oneWayDashScanPadding = 1.25f;

    [HideInInspector] public Dictionary<EnemyAction, float> actionProbabilities = new();
    [HideInInspector] public EnemyAction? lastAction;
    [HideInInspector] public Vector2 lockedFireDir = Vector2.right;

    GameObject boostVisual;
    CapsuleCollider2D bodyCollider;
    Attack meleeAttack;
    Attack stompAttack;
    Quaternion aimStartRotation;
    Quaternion gunRestLocalRotation = Quaternion.identity;
    Vector3 gunRestLocalScale = Vector3.one;
    bool gunRestCached;
    bool facingLocked;
    bool gunTracking;
    bool gunHoldLocked;
    bool gunApplyAim;
    Quaternion lockedGunRotation = Quaternion.identity;
    Quaternion pendingAimRotation = Quaternion.identity;
    float storedGravity = 1f;
    bool gravityStored;
    bool pendingForcedJump;
    bool halfHpJumpArmed;
    float heightBiasTimer;
    Collider2D stompTargetCollider;
    float stompLandingY;
    readonly HashSet<Collider2D> ignoredStompPlatforms = new();
    readonly HashSet<Collider2D> trackedMoveOneWayPlatforms = new();
    readonly HashSet<Collider2D> ignoredMoveOneWayPlatforms = new();
    readonly Collider2D[] platformOverlapBuffer = new Collider2D[PlatformOverlapBufferSize];
    ContactFilter2D platformFilter;
    LayerMask stompScanMask;

    public bool IsDashPassing { get; set; }

    protected override void Awake()
    {
        base.Awake();
        patroState = new AE74IdleGuardState();
        returnState = new AE74ReturnHomeState();
        skillState = new AE74CannonState();
        meleeAttackState = new AE74MeleeDashState();
        shotState = new AE74MachineGunState();
        jumpState = new AE74TacticalJumpState();
        reloadState = new AE74ActionCooldownState();

        if (normalSpeed <= 0f)
            normalSpeed = moveSpeed > 0f ? moveSpeed : 3.5f;
        if (chaseSpeed <= 0f)
            chaseSpeed = dashSpeed > 0f ? dashSpeed : 10f;

        CacheBody();
        CacheBoost();
        CacheHitboxes();
        ConfigureHitboxes();

        int ground = LayerMask.GetMask("Ground");
        int platform = LayerMask.GetMask("Platform");
        stompScanMask = ground | platform;
        platformFilter = new ContactFilter2D
        {
            useLayerMask = true,
            layerMask = stompScanMask,
            useTriggers = false,
        };
    }

    protected override void StartCombatCycle() => EvaluateCycle();

    void Start()
    {
        ConfigurePhysicsCheck();
    }

    void CacheBody()
    {
        bodyCollider = GetComponent<CapsuleCollider2D>();
        if (Rb != null && !gravityStored)
        {
            storedGravity = Rb.gravityScale;
            gravityStored = true;
        }
    }

    void CacheBoost()
    {
        if (boostVisual != null)
            return;

        Transform boost = transform.Find("Boost");
        if (boost != null)
            boostVisual = boost.gameObject;
    }

    void CacheHitboxes()
    {
        if (meleeAttacker == null)
        {
            Transform found = transform.Find("Attacker1");
            if (found != null)
                meleeAttacker = found.gameObject;
        }

        if (stompHitbox == null)
        {
            Transform found = transform.Find("StompHitbox");
            if (found != null)
                stompHitbox = found.gameObject;
        }

        if (gunBase == null)
        {
            Transform gun = transform.Find("Gun");
            if (gun != null)
                gunBase = gun;
        }

        if (firePoint == null && gunBase != null)
        {
            Transform fp = gunBase.Find("FirePoint");
            if (fp != null)
                firePoint = fp;
        }

        if (cannonFirePoint1 == null)
            cannonFirePoint1 = transform.Find("CannonFirePoint1");
        if (cannonFirePoint2 == null)
            cannonFirePoint2 = transform.Find("CannonFirePoint2");

        CacheGunRestPose();

        if (meleeAttacker != null)
            meleeAttack = meleeAttacker.GetComponent<Attack>();
        if (stompHitbox != null)
            stompAttack = stompHitbox.GetComponent<Attack>();
    }

    void ConfigureHitboxes()
    {
        ConfigureMeleeAttack(meleeAttack, meleeDamage);
        ConfigureMeleeAttack(stompAttack, stompDamage);
        SetMeleeAttackerActive(false);
        SetStompHitboxActive(false);
        SetBoostActive(false);
    }

    static void ConfigureMeleeAttack(Attack attack, int damage)
    {
        if (attack == null)
            return;

        attack.damage = damage;
        attack.attackType = AttackType.Melee;
        attack.requireTag = "Player";
    }

    void ConfigurePhysicsCheck()
    {
        if (physicsCheck == null)
            return;

        if (bodyCollider == null)
            bodyCollider = GetComponent<CapsuleCollider2D>();
        if (bodyCollider == null)
            return;

        float bottom = bodyCollider.offset.y - bodyCollider.size.y * 0.5f;
        physicsCheck.bottomOffset = new Vector2(0f, bottom + 0.1f);
        if (physicsCheck.checkRaduis <= 0f)
            physicsCheck.checkRaduis = 0.2f;
    }

    protected override void OnEnable()
    {
        RegisterSeparation();
        ResetActionProbabilities();
        lastAction = null;
        CacheHome();
        isReturning = false;
        RestoreGroundPhysics();
        RestoreStompPlatformIgnores();
        RestoreMoveOneWayPlatformIgnores();
        IsDashPassing = false;
        SetMeleeAttackerActive(false);
        SetStompHitboxActive(false);
        SetBoostActive(false);

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

    protected override void OnDisable()
    {
        RestoreStompPlatformIgnores();
        RestoreMoveOneWayPlatformIgnores();
        RestoreGroundPhysics();
        RestoreGunPose();
        SetBoostActive(false);
        SetMeleeAttackerActive(false);
        SetStompHitboxActive(false);
        IsDashPassing = false;
        base.OnDisable();
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

    protected override void FixedUpdate()
    {
        if (passOneWayPlatformsWhileMoving)
            UpdateMoveOneWayPlatformPass();
        else
            RestoreMoveOneWayPlatformIgnores();

        base.FixedUpdate();
    }

    public override bool IsPlayerInCombatRange()
    {
        EnsurePlayerReference();
        if (player == null || engageRange <= 0f)
            return false;

        return Vector2.Distance(transform.position, player.position) <= engageRange;
    }

    protected override void Update()
    {
        base.Update();
        TrackHeightBias();
        TrackHalfHealthJump();

        if (isDead)
        {
            facingLocked = false;
            gunTracking = false;
            gunHoldLocked = false;
            gunApplyAim = false;
            RestoreStompPlatformIgnores();
            RestoreMoveOneWayPlatformIgnores();
            SetBoostActive(false);
            SetMeleeAttackerActive(false);
            SetStompHitboxActive(false);
            IsDashPassing = false;
        }
    }

    void LateUpdate()
    {
        HoldShootLastFrame();

        if (isDead || gunBase == null)
            return;

        if (gunHoldLocked)
        {
            PrepareGunForWorldAim();
            gunBase.rotation = lockedGunRotation;
            return;
        }

        if (gunApplyAim)
        {
            PrepareGunForWorldAim();
            gunBase.rotation = pendingAimRotation;
            return;
        }

        if (!gunTracking)
            return;

        SnapGunToPlayer();
        LockFireDirection();
    }

    void TrackHeightBias()
    {
        EnsurePlayerReference();
        if (player == null || isDead)
        {
            heightBiasTimer = 0f;
            return;
        }

        float dy = Mathf.Abs(player.position.y - transform.position.y);
        if (dy >= heightBiasThreshold)
            heightBiasTimer += Time.deltaTime;
        else
            heightBiasTimer = 0f;
    }

    void TrackHalfHealthJump()
    {
        if (halfHpJumpArmed || character == null || character.maxHealth <= 0f)
            return;

        if (character.currentHealth <= character.maxHealth * 0.5f)
        {
            halfHpJumpArmed = true;
            pendingForcedJump = true;
        }
    }

    void ResetActionProbabilities()
    {
        actionProbabilities.Clear();
        var weights = BuildBaseWeights();
        NormalizeInto(actionProbabilities, weights);
    }

    List<(EnemyAction action, float weight)> BuildBaseWeights()
    {
        var weights = new List<(EnemyAction action, float weight)>();
        if (enableCannon)
            weights.Add((EnemyAction.Missile, cannonWeight));
        if (enableMeleeDash)
            weights.Add((EnemyAction.MeleeAttack, meleeDashWeight));
        if (enableMachineGun)
            weights.Add((EnemyAction.Shot, machineGunWeight));
        if (enableTacticalJump)
            weights.Add((EnemyAction.Jump, tacticalJumpWeight));
        return weights;
    }

    static void NormalizeInto(Dictionary<EnemyAction, float> dest, List<(EnemyAction action, float weight)> weights)
    {
        dest.Clear();
        float total = 0f;
        for (int i = 0; i < weights.Count; i++)
            total += Mathf.Max(0f, weights[i].weight);

        if (total <= 0f)
        {
            dest[EnemyAction.MeleeAttack] = 1f;
            return;
        }

        for (int i = 0; i < weights.Count; i++)
            dest[weights[i].action] = Mathf.Max(0f, weights[i].weight) / total;
    }

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

        RollAndEnterAction();
    }

    public void FinishActionAndRecover()
    {
        if (isDead)
            return;

        RestoreGroundPhysics();
        RestoreStompPlatformIgnores();
        RestoreGunPose();
        SetBoostActive(false);
        SetMeleeAttackerActive(false);
        SetStompHitboxActive(false);

        if (actionCooldown <= 0f)
        {
            EvaluateCycle();
            return;
        }

        SwitchState(NPCState.Reload);
    }

    public void ClearActionAnims()
    {
        SetAnimBool("walk", false);
        SetAnimBool("dashStart", false);
        SetAnimBool("dash", false);
        SetAnimBool("shoot", false);
        SetAnimBool("melee", false);
        SetAnimBool("jump", false);
        SetAnimBool("fly", false);
        SetAnimBool("airAttack", false);
        SetAnimBool("landStart", false);
        SetAnimBool("landEnd", false);
    }

    public void EnterIdlePose()
    {
        ClearActionAnims();
        RestoreGunPose();
        StopHorizontalMotion();
        currentSpeed = 0f;
        SetBoostActive(false);
        SetMeleeAttackerActive(false);
        SetStompHitboxActive(false);
    }

    void RollAndEnterAction()
    {
        if (pendingForcedJump && enableTacticalJump)
        {
            pendingForcedJump = false;
            OnActionEntered(EnemyAction.Jump);
            SwitchState(NPCState.Jump);
            return;
        }

        var adjusted = BuildRollWeights();
        if (adjusted.Count == 0)
        {
            FinishActionAndRecover();
            return;
        }

        float roll = Random.value;
        float cumulative = 0f;
        EnemyAction selected = adjusted[0].action;
        for (int i = 0; i < adjusted.Count; i++)
        {
            selected = adjusted[i].action;
            cumulative += adjusted[i].weight;
            if (roll <= cumulative)
                break;
        }

        SwitchState(ActionToState(selected));
    }

    List<(EnemyAction action, float weight)> BuildRollWeights()
    {
        if (actionProbabilities == null || actionProbabilities.Count == 0)
            ResetActionProbabilities();

        var raw = new List<(EnemyAction action, float weight)>();
        foreach (var pair in actionProbabilities)
        {
            if (!IsActionAvailable(pair.Key))
                continue;

            float weight = pair.Value;
            if (pair.Key == EnemyAction.MeleeAttack && IsPlayerInMeleeWeightRange())
                weight *= meleeCloseWeightMultiplier;
            if (pair.Key == EnemyAction.Jump && heightBiasTimer >= heightBiasDuration)
                weight *= heightBiasWeightMultiplier;

            raw.Add((pair.Key, weight));
        }

        var normalized = new Dictionary<EnemyAction, float>();
        NormalizeInto(normalized, raw);

        var result = new List<(EnemyAction action, float weight)>();
        foreach (var pair in normalized)
            result.Add((pair.Key, pair.Value));
        return result;
    }

    bool IsActionAvailable(EnemyAction action)
    {
        return action switch
        {
            EnemyAction.Missile => enableCannon && IsCannonYAllowed(),
            EnemyAction.MeleeAttack => enableMeleeDash,
            EnemyAction.Shot => enableMachineGun,
            EnemyAction.Jump => enableTacticalJump,
            _ => false
        };
    }

    public bool IsCannonYAllowed()
    {
        EnsurePlayerReference();
        if (player == null)
            return false;

        return Mathf.Abs(player.position.y - transform.position.y) < cannonMaxYDelta;
    }

    public bool IsPlayerInMeleeRange()
    {
        return Vector2.Distance(transform.position, GetCombatAimPoint()) <= meleeRange;
    }

    public Vector2 GetCombatAimPoint()
    {
        EnsurePlayerReference();
        if (player == null)
            return transform.position;

        Vector2 aim = player.position;
        Collider2D body = player.GetComponent<CapsuleCollider2D>();
        if (body == null)
            body = player.GetComponent<Collider2D>();
        if (body != null)
            aim = body.bounds.center;
        return aim;
    }

    public bool IsPlayerInMeleeWeightRange()
    {
        return GetHorizontalDistanceToPlayer() <= meleeWeightRange;
    }

    static NPCState ActionToState(EnemyAction action) => action switch
    {
        EnemyAction.Missile => NPCState.Skill,
        EnemyAction.MeleeAttack => NPCState.MeleeAttack,
        EnemyAction.Shot => NPCState.Shot,
        EnemyAction.Jump => NPCState.Jump,
        _ => NPCState.Reload
    };

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

    public int RollMachineGunBurstCount()
    {
        return Mathf.Max(1, mgBurstCount);
    }

    public int RollMachineGunCycleCount()
    {
        int min = Mathf.Max(1, mgCycleMin);
        int max = Mathf.Max(min, mgCycleMax);
        return Random.Range(min, max + 1);
    }

    public int RollAirBurstCount()
    {
        return Mathf.Max(1, airBurstCount);
    }

    public int RollAirCycleCount()
    {
        int min = Mathf.Max(1, airCycleMin);
        int max = Mathf.Max(min, airCycleMax);
        return Random.Range(min, max + 1);
    }

    public void StopHorizontalMotion()
    {
        if (Rb == null)
            return;

        Rb.linearVelocity = new Vector2(0f, Rb.linearVelocity.y);
    }

    public void StopAllMotion()
    {
        if (Rb == null)
            return;

        Rb.linearVelocity = Vector2.zero;
    }

    public bool IsWallInDirection(float moveDir)
    {
        if (Mathf.Approximately(moveDir, 0f))
            return false;

        if (physicsCheck != null
            && ((moveDir < 0f && physicsCheck.touchLeftWall)
                || (moveDir > 0f && physicsCheck.touchRightWall)))
            return true;

        return IsOutboundAirWallInDirection(moveDir);
    }

    bool IsOutboundAirWallInDirection(float moveDir)
    {
        var body = GetComponent<Collider2D>();
        if (body == null)
            return false;

        Bounds bounds = body.bounds;
        float probeWidth = 0.18f;
        float probeHeight = Mathf.Max(0.4f, bounds.size.y * 0.8f);
        float centerX = moveDir > 0f
            ? bounds.max.x + probeWidth * 0.5f
            : bounds.min.x - probeWidth * 0.5f;

        return AirWallRegistry.IsOutboundAirWallAhead(
            new Vector2(centerX, bounds.center.y),
            new Vector2(probeWidth, probeHeight),
            new Vector2(moveDir, 0f));
    }

    public override void MoveTowardSpawnTarget()
    {
        float dx = spawnTargetPosition.x - transform.position.x;
        if (Mathf.Abs(dx) <= returnArriveDistance)
            return;

        float dir = Mathf.Sign(dx);
        if (dir == 0f)
            return;

        currentSpeed = GetSpawnApproachSpeed();
        if (IsWallInDirection(dir) || IsLedgeBlocking(dir))
        {
            StopHorizontalMotion();
            return;
        }

        FaceDirection(dir);
        MoveHorizontal(dir);
    }

    public void SetBoostActive(bool active)
    {
        CacheBoost();
        if (boostVisual == null)
            return;
        if (boostVisual.activeSelf == active)
            return;

        boostVisual.SetActive(active);
    }

    public void SetMeleeAttackerActive(bool active)
    {
        if (meleeAttacker == null)
            return;
        if (meleeAttacker.activeSelf == active)
            return;

        meleeAttacker.SetActive(active);
        if (!active)
            return;

        if (meleeAttack == null)
            meleeAttack = meleeAttacker.GetComponent<Attack>();
        meleeAttack?.NotifyHitboxEnabled();
    }

    public void PlayMeleeAnim()
    {
        SetAnimBool("dashStart", false);
        SetAnimBool("dash", false);
        SetAnimBool("melee", true);
        if (anim != null && !string.IsNullOrEmpty(meleeStateName))
            anim.Play(meleeStateName, 0, 0f);
    }

    public void SetStompHitboxActive(bool active)
    {
        if (stompHitbox == null)
            return;
        if (stompHitbox.activeSelf == active)
            return;

        stompHitbox.SetActive(active);
    }

    public void SyncMeleeHitbox(float normalizedTime)
    {
        bool active = normalizedTime >= meleeHitboxFromNormalized && normalizedTime < 1f;
        SetMeleeAttackerActive(active);
    }

    public void SetFacingLocked(bool locked)
    {
        facingLocked = locked;
    }

    public void PlayShootOnceAndHold()
    {
        SetAnimBool("shoot", true);
    }

    void HoldShootLastFrame()
    {
        if (anim == null || isDead)
            return;
        if (!anim.GetBool("shoot"))
            return;

        var info = anim.GetCurrentAnimatorStateInfo(0);
        if (!info.IsName("RobotWhite_Shoot") || info.normalizedTime < 1f)
            return;

        anim.Play(info.fullPathHash, 0, 1f);
    }

    void CacheGunRestPose()
    {
        if (gunBase == null || gunRestCached)
            return;

        gunRestLocalRotation = gunBase.localRotation;
        gunRestLocalScale = gunBase.localScale;
        if (Mathf.Approximately(gunRestLocalScale.x, 0f))
            gunRestLocalScale.x = 1f;
        gunRestCached = true;
    }

    /// <summary>
    /// 抵消身体翻转，让炮管世界 X 保持正向，才能像装甲车一样用世界旋转瞄准。
    /// </summary>
    public void PrepareGunForWorldAim()
    {
        if (gunBase == null)
            return;

        CacheGunRestPose();
        float parentX = gunBase.parent != null ? gunBase.parent.lossyScale.x : 1f;
        if (Mathf.Approximately(parentX, 0f))
            parentX = 1f;

        Vector3 scale = gunBase.localScale;
        scale.x = Mathf.Abs(gunRestLocalScale.x) * Mathf.Sign(parentX);
        scale.y = gunRestLocalScale.y;
        scale.z = gunRestLocalScale.z;
        gunBase.localScale = scale;
    }

    public void SetGunTracking(bool active)
    {
        gunTracking = active;
        if (active)
        {
            gunHoldLocked = false;
            gunApplyAim = false;
        }
        if (!active)
            return;

        SnapGunToPlayer();
        LockFireDirection();
    }

    public void HoldLockedGun()
    {
        gunTracking = false;
        gunApplyAim = false;
        gunHoldLocked = gunBase != null;
        if (gunBase != null)
            lockedGunRotation = gunBase.rotation;
    }

    public void RestoreGunPose()
    {
        gunTracking = false;
        gunHoldLocked = false;
        gunApplyAim = false;
        if (gunBase == null)
            return;

        CacheGunRestPose();
        gunBase.localRotation = gunRestLocalRotation;
        gunBase.localScale = gunRestLocalScale;
    }

    public void BeginGunAim()
    {
        PrepareGunForWorldAim();
        if (gunBase != null)
            aimStartRotation = gunBase.rotation;
    }

    public void AimGunAtPlayer(float t)
    {
        if (gunBase == null)
            return;

        PrepareGunForWorldAim();
        pendingAimRotation = Quaternion.Slerp(aimStartRotation, ComputeAimRotation(), Mathf.Clamp01(t));
        gunBase.rotation = pendingAimRotation;
        gunApplyAim = true;
        gunHoldLocked = false;
    }

    public void SnapGunToPlayer()
    {
        if (gunBase == null)
            return;

        PrepareGunForWorldAim();
        pendingAimRotation = ComputeAimRotation();
        gunBase.rotation = pendingAimRotation;
        gunApplyAim = true;
    }

    public void LockFireDirection()
    {
        Vector2 dir = GetCurrentBarrelDirection();
        if (dir.sqrMagnitude < 0.0001f)
        {
            EnsurePlayerReference();
            if (player != null && firePoint != null)
                dir = GetPlayerAimPoint() - (Vector2)firePoint.position;
            else
                dir = new Vector2(faceDir.x, 0f);
        }

        if (dir.sqrMagnitude < 0.0001f)
            dir = Vector2.right;

        lockedFireDir = dir.normalized;
    }

    Vector2 GetCurrentBarrelDirection()
    {
        if (firePoint != null && gunBase != null)
        {
            Vector2 delta = (Vector2)firePoint.position - (Vector2)gunBase.position;
            if (delta.sqrMagnitude > 0.0001f)
                return delta;
        }

        return gunBase != null ? (Vector2)gunBase.right : new Vector2(faceDir.x, 0f);
    }

    Quaternion ComputeAimRotation()
    {
        if (gunBase == null)
            return Quaternion.identity;

        Vector2 origin = gunBase.position;
        Vector2 targetPos = GetPlayerAimPoint();
        Vector2 toTarget = targetPos - origin;
        if (toTarget.sqrMagnitude < 0.0001f)
            return gunBase.rotation;

        Vector2 localBarrel = GetLocalBarrelOffset();
        float barrelAngle = Mathf.Atan2(localBarrel.y, localBarrel.x) * Mathf.Rad2Deg;
        float targetAngle = Mathf.Atan2(toTarget.y, toTarget.x) * Mathf.Rad2Deg;
        return Quaternion.Euler(0f, 0f, targetAngle - barrelAngle);
    }

    Vector2 GetPlayerAimPoint()
    {
        EnsurePlayerReference();
        if (player == null)
            return (Vector2)transform.position + new Vector2(faceDir.x, 0f);

        Vector2 aim = player.position;
        Collider2D body = player.GetComponent<CapsuleCollider2D>();
        if (body == null)
            body = player.GetComponent<Collider2D>();
        if (body != null)
            aim = body.bounds.center;

        aim.y += mgAimHeightOffset;
        return aim;
    }

    Vector2 GetLocalBarrelOffset()
    {
        if (gunBase == null || firePoint == null)
            return Vector2.right;

        if (firePoint.parent == gunBase)
        {
            Vector3 local = firePoint.localPosition;
            if (local.sqrMagnitude > 0.0001f)
                return local;
        }

        Vector3 world = gunBase.InverseTransformPoint(firePoint.position);
        if (world.sqrMagnitude > 0.0001f)
            return world;

        return Vector2.right;
    }

    public void FireLockedProjectile()
    {
        FireProjectileInDirection(lockedFireDir);
    }

    public void FireProjectileInDirection(Vector2 dir)
    {
        if (projectilePrefab == null)
            return;

        if (dir.sqrMagnitude < 0.0001f)
            dir = new Vector2(faceDir.x, 0f);
        dir.Normalize();

        Vector3 spawnPos = firePoint != null ? firePoint.position : transform.position;
        var projectile = Instantiate(projectilePrefab, spawnPos, Quaternion.identity);
        EnemySceneCleanup.PlaceInSourceScene(projectile.gameObject, this);
        projectile.Init(dir);
        projectile.transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg);
        IgnoreSelfCollision(projectile.GetComponent<Collider2D>());
        PlayShootSfx();
    }

    public void GetCannonFirePoints(out Transform first, out Transform second)
    {
        Transform fallback = cannonFirePoint1 != null
            ? cannonFirePoint1
            : (cannonFirePoint2 != null ? cannonFirePoint2 : firePoint);
        first = cannonFirePoint1 != null ? cannonFirePoint1 : fallback;
        second = cannonFirePoint2 != null ? cannonFirePoint2 : fallback;
    }

    public void FireCannonMissileFrom(Transform point)
    {
        if (cannonMissilePrefab == null)
            return;

        Vector2 dir = new Vector2(Mathf.Approximately(faceDir.x, 0f) ? 1f : Mathf.Sign(faceDir.x), 0f);
        Transform spawn = point != null ? point : firePoint;
        Vector3 spawnPos = spawn != null
            ? spawn.position
            : transform.position + (Vector3)(dir * 0.8f);
        var missile = Instantiate(cannonMissilePrefab, spawnPos, Quaternion.identity);
        EnemySceneCleanup.PlaceInSourceScene(missile.gameObject, this);
        missile.Init(dir, GetComponent<Collider2D>());
        PlayRocketLaunchSfx();
    }

    public void FireDashHomingMissiles()
    {
        if (dashMissilePrefab == null)
            return;

        EnsurePlayerReference();
        FireOneHoming(missileFirePoint1);
        FireOneHoming(missileFirePoint2 != null ? missileFirePoint2 : missileFirePoint1);
    }

    void FireOneHoming(Transform point)
    {
        Vector3 spawnPos = point != null ? point.position : transform.position + Vector3.up;
        var missile = Instantiate(dashMissilePrefab, spawnPos, Quaternion.identity);
        EnemySceneCleanup.PlaceInSourceScene(missile.gameObject, this);
        missile.Init(GetComponent<Collider2D>(), player, dashMissileAscentDuration);
        PlayRocketLaunchSfx();
    }

    public void FireAirScatter()
    {
        Vector2 forward = lockedFireDir.sqrMagnitude > 0.0001f
            ? lockedFireDir.normalized
            : GetCurrentBarrelDirection();
        if (forward.sqrMagnitude < 0.0001f)
            forward = new Vector2(faceDir.x, -0.6f);
        forward.Normalize();
        float baseAngle = Mathf.Atan2(forward.y, forward.x) * Mathf.Rad2Deg;
        FireProjectileInDirection(AngleToDir(baseAngle));
        FireProjectileInDirection(AngleToDir(baseAngle + airScatterAngle));
        FireProjectileInDirection(AngleToDir(baseAngle - airScatterAngle));
    }

    static Vector2 AngleToDir(float degrees)
    {
        float rad = degrees * Mathf.Deg2Rad;
        return new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));
    }

    void IgnoreSelfCollision(Collider2D other)
    {
        if (other == null)
            return;

        var selfCols = GetComponentsInChildren<Collider2D>(true);
        for (int i = 0; i < selfCols.Length; i++)
        {
            if (selfCols[i] != null && selfCols[i] != other)
                Physics2D.IgnoreCollision(other, selfCols[i], true);
        }
    }

    public void SetFlightPhysics(bool flying)
    {
        if (Rb == null)
            return;

        if (!gravityStored)
        {
            storedGravity = Rb.gravityScale;
            gravityStored = true;
        }

        if (flying)
        {
            Rb.gravityScale = 0f;
            Rb.linearVelocity = Vector2.zero;
        }
        else
        {
            Rb.gravityScale = storedGravity;
        }
    }

    public void RestoreGroundPhysics()
    {
        SetFlightPhysics(false);
    }

    public void MoveKinematicToward(Vector2 target, float speed)
    {
        if (Rb == null)
        {
            transform.position = Vector2.MoveTowards(transform.position, target, speed * Time.deltaTime);
            return;
        }

        Vector2 next = Vector2.MoveTowards(Rb.position, target, speed * Time.deltaTime);
        Rb.MovePosition(next);
        Rb.linearVelocity = Vector2.zero;
    }

    public bool HasArrivedAt(Vector2 target, float threshold)
    {
        return Vector2.Distance(transform.position, target) <= threshold;
    }

    public Vector2 ComputeDiagonalJumpTarget()
    {
        EnsurePlayerReference();
        float signX = 1f;
        if (player != null)
        {
            float dx = player.position.x - transform.position.x;
            signX = Mathf.Approximately(dx, 0f) ? (Mathf.Approximately(faceDir.x, 0f) ? 1f : Mathf.Sign(faceDir.x)) : Mathf.Sign(dx);
        }

        return new Vector2(transform.position.x + signX * jumpHeight, transform.position.y + jumpHeight);
    }

    public bool TryLockStompTarget(out Vector2 hoverPoint)
    {
        hoverPoint = transform.position;
        EnsurePlayerReference();
        float landingX = player != null ? player.position.x : transform.position.x;
        float startY = (player != null ? player.position.y : transform.position.y) + landingRayStartOffsetY;
        bool locked = LockStompLandingFromRay(new Vector2(landingX, startY), transform.position.y);
        hoverPoint = new Vector2(landingX, stompLandingY + stompHoverHeight);
        return locked;
    }

    public bool TryLockStompLandingBelowSelf()
    {
        float feetY = GetFeetY();
        return LockStompLandingFromRay(new Vector2(transform.position.x, feetY + 0.05f), feetY);
    }

    bool LockStompLandingFromRay(Vector2 origin, float fallbackY)
    {
        RaycastHit2D hit = Physics2D.Raycast(origin, Vector2.down, landingRayDistance, GetLandingRayMask());
        if (hit.collider == null)
        {
            stompTargetCollider = null;
            stompLandingY = fallbackY;
            return false;
        }

        stompTargetCollider = hit.collider;
        stompLandingY = hit.point.y;
        return true;
    }

    LayerMask GetLandingRayMask()
    {
        if (physicsCheck != null && physicsCheck.groundLayer.value != 0)
            return physicsCheck.groundLayer;

        return stompScanMask.value != 0
            ? stompScanMask
            : (LayerMask)(LayerMask.GetMask("Ground") | LayerMask.GetMask("Platform"));
    }

    public float GetStompLandingY() => stompLandingY;

    public Collider2D GetStompTargetCollider() => stompTargetCollider;

    public float GetFeetY()
    {
        if (bodyCollider == null)
            bodyCollider = GetComponent<CapsuleCollider2D>();
        if (bodyCollider != null)
            return bodyCollider.bounds.min.y;

        return transform.position.y;
    }

    public void BeginStompFall()
    {
        SetFlightPhysics(true);
        if (Rb != null)
            Rb.linearVelocity = new Vector2(0f, -Mathf.Abs(stompFallSpeed));
    }

    public void SnapOntoLanding()
    {
        float feet = GetFeetY();
        float delta = stompLandingY - feet;
        Vector3 pos = transform.position;
        pos.y += delta;
        transform.position = pos;
        StopAllMotion();
    }

    public bool HasReachedStompLanding()
    {
        if (GetFeetY() <= stompLandingY + 0.06f)
            return true;

        if (stompTargetCollider == null)
            return GetFeetY() <= stompLandingY + 0.06f;

        if (bodyCollider == null)
            bodyCollider = GetComponent<CapsuleCollider2D>();
        if (bodyCollider != null && bodyCollider.IsTouching(stompTargetCollider))
            return true;

        return false;
    }

    public void UpdateDashPlatformPassThrough()
    {
        UpdatePlatformPassThrough(ignoreLandingTarget: true);
    }

    void UpdatePlatformPassThrough(bool ignoreLandingTarget)
    {
        if (bodyCollider == null)
            bodyCollider = GetComponent<CapsuleCollider2D>();
        if (bodyCollider == null)
            return;

        platformFilter.layerMask = stompScanMask;
        Vector2 size = (Vector2)bodyCollider.bounds.size + new Vector2(1.25f, 1.25f);
        int count = Physics2D.OverlapBox(
            bodyCollider.bounds.center,
            size,
            0f,
            platformFilter,
            platformOverlapBuffer);

        var keep = new HashSet<Collider2D>();
        for (int i = 0; i < count; i++)
        {
            Collider2D platform = platformOverlapBuffer[i];
            if (!ShouldIgnorePlatform(platform, ignoreLandingTarget))
                continue;

            keep.Add(platform);
            ignoredStompPlatforms.Add(platform);
            Physics2D.IgnoreCollision(bodyCollider, platform, true);
        }

        if (ignoredStompPlatforms.Count == 0)
            return;

        var stale = new List<Collider2D>();
        foreach (var platform in ignoredStompPlatforms)
        {
            if (platform == null || keep.Contains(platform))
                continue;
            stale.Add(platform);
        }

        for (int i = 0; i < stale.Count; i++)
        {
            if (stale[i] != null && !ignoredMoveOneWayPlatforms.Contains(stale[i]))
                Physics2D.IgnoreCollision(bodyCollider, stale[i], false);
            ignoredStompPlatforms.Remove(stale[i]);
        }
    }

    public void UpdateStompPlatformPassThrough()
    {
        UpdatePlatformPassThrough(ignoreLandingTarget: false);
    }

    bool ShouldIgnorePlatform(Collider2D platform, bool ignoreLandingTarget)
    {
        if (platform == null || platform == bodyCollider)
            return false;
        if (!ignoreLandingTarget && platform == stompTargetCollider)
            return false;
        if (platform.transform != null && platform.transform.IsChildOf(transform))
            return false;

        bool isPlatformLayer = platform.gameObject.layer == LayerMask.NameToLayer("Platform");
        bool isOneWay = FallingPlatform.IsOneWayPlatformCollider(platform);
        if (isPlatformLayer || isOneWay)
            return true;

        if (ignoreLandingTarget)
            return false;

        // Ground 层只忽略落点上方的「地板」，避免把墙体一起穿透
        if (platform.bounds.max.y <= stompLandingY + 0.05f)
            return false;

        return platform.bounds.size.x >= platform.bounds.size.y * 0.75f;
    }

    public void RestoreStompPlatformIgnores()
    {
        if (bodyCollider == null)
            bodyCollider = GetComponent<CapsuleCollider2D>();

        foreach (var platform in ignoredStompPlatforms)
        {
            if (platform != null && bodyCollider != null && !ignoredMoveOneWayPlatforms.Contains(platform))
                Physics2D.IgnoreCollision(bodyCollider, platform, false);
        }

        ignoredStompPlatforms.Clear();
        stompTargetCollider = null;
    }

    void UpdateMoveOneWayPlatformPass()
    {
        if (bodyCollider == null)
            bodyCollider = GetComponent<CapsuleCollider2D>();
        if (bodyCollider == null || Rb == null)
            return;

        platformFilter.layerMask = stompScanMask;
        float feet = bodyCollider.bounds.min.y;
        Vector2 feetPos = new Vector2(bodyCollider.bounds.center.x, feet);
        float vy = Rb.linearVelocity.y;
        bool forcePass = IsDashPassing;
        int count = CollectNearbyMoveOneWayPlatforms(forcePass);

        var activeThisFrame = new HashSet<Collider2D>();
        for (int i = 0; i < count; i++)
        {
            Collider2D col = platformOverlapBuffer[i];
            if (col == null || col == bodyCollider)
                continue;
            if (col.transform != null && col.transform.IsChildOf(transform))
                continue;
            if (!FallingPlatform.IsOneWayPlatformCollider(col))
                continue;
            if (IsLayeredSlope(col))
                continue;

            activeThisFrame.Add(col);
            bool shouldCollide = !forcePass && ShouldCollideWithOneWay(col, feet, feetPos, vy);
            // 践踏已锁定落点时，中间的单向板必须继续穿透，避免下落站板卡住
            if (stompTargetCollider != null && col != stompTargetCollider)
                shouldCollide = false;
            SetMoveOneWayIgnored(col, !shouldCollide);
            trackedMoveOneWayPlatforms.Add(col);
        }

        if (trackedMoveOneWayPlatforms.Count == 0)
            return;

        var stale = new List<Collider2D>();
        foreach (Collider2D tracked in trackedMoveOneWayPlatforms)
        {
            if (tracked == null)
            {
                stale.Add(tracked);
                ignoredMoveOneWayPlatforms.Remove(tracked);
                continue;
            }

            if (activeThisFrame.Contains(tracked))
                continue;

            SetMoveOneWayIgnored(tracked, false);
            stale.Add(tracked);
        }

        for (int i = 0; i < stale.Count; i++)
            trackedMoveOneWayPlatforms.Remove(stale[i]);
    }

    int CollectNearbyMoveOneWayPlatforms(bool forcePass)
    {
        if (!forcePass || oneWayDashScanPadding <= 0.001f)
            return bodyCollider.Overlap(platformFilter, platformOverlapBuffer);

        Vector2 size = (Vector2)bodyCollider.bounds.size + Vector2.one * (oneWayDashScanPadding * 2f);
        return Physics2D.OverlapBox(
            bodyCollider.bounds.center,
            size,
            0f,
            platformFilter,
            platformOverlapBuffer);
    }

    bool ShouldCollideWithOneWay(Collider2D platform, float feet, Vector2 feetPos, float vy)
    {
        var slope = platform.GetComponent<SlopeOneWayPlatform>();
        if (slope != null)
        {
            float slopeMargin = slope.SurfaceMargin;
            float standMargin = slope.StandMargin;
            float signedDist = slope.GetSignedDistanceToSurface(feetPos);

            if (signedDist < -(slopeMargin + standMargin))
                return false;
            if (signedDist >= -standMargin)
                return true;
            if (vy > 0.15f)
                return false;
            return true;
        }

        float platformTop = platform.bounds.max.y;
        float platformBottom = platform.bounds.min.y;
        float margin = Mathf.Max(0.01f, oneWaySurfaceMargin);

        if (IsInsideDescendingPlatform(platform, feet, platformTop, margin))
            return false;
        if (feet < platformBottom - margin)
            return false;
        if (vy > 0f && feet < platformTop - margin)
            return false;
        if (vy <= 0f && feet >= platformBottom - margin)
            return true;

        return feet >= platformTop - margin;
    }

    static bool IsInsideDescendingPlatform(Collider2D platform, float feetY, float platformTop, float margin)
    {
        var provider = platform.GetComponent<IPlatformVelocityProvider>()
            ?? platform.GetComponentInParent<IPlatformVelocityProvider>();
        if (provider == null || provider.PlatformVelocity.y >= -0.01f)
            return false;
        return feetY < platformTop - margin;
    }

    static bool IsLayeredSlope(Collider2D col)
    {
        if (col == null)
            return false;
        return col.GetComponent<SlopePathSegment>() != null
            || col.GetComponentInParent<SlopePathSegment>() != null;
    }

    void SetMoveOneWayIgnored(Collider2D platform, bool ignore)
    {
        if (bodyCollider == null || platform == null)
            return;

        if (ignore)
        {
            Physics2D.IgnoreCollision(bodyCollider, platform, true);
            ignoredMoveOneWayPlatforms.Add(platform);
            return;
        }

        if (!ignoredStompPlatforms.Contains(platform))
            Physics2D.IgnoreCollision(bodyCollider, platform, false);
        ignoredMoveOneWayPlatforms.Remove(platform);
    }

    void RestoreMoveOneWayPlatformIgnores()
    {
        if (bodyCollider == null)
            bodyCollider = GetComponent<CapsuleCollider2D>();

        foreach (var platform in trackedMoveOneWayPlatforms)
        {
            if (platform != null && bodyCollider != null && !ignoredStompPlatforms.Contains(platform))
                Physics2D.IgnoreCollision(bodyCollider, platform, false);
        }

        trackedMoveOneWayPlatforms.Clear();
        ignoredMoveOneWayPlatforms.Clear();
    }

    public void SpawnShockwaves()
    {
        if (shockwavePrefab == null)
            return;

        Vector3 origin = shockwavePoint != null
            ? shockwavePoint.position
            : new Vector3(transform.position.x, GetFeetY(), transform.position.z);

        SpawnOneShockwave(origin, Vector2.left);
        SpawnOneShockwave(origin, Vector2.right);
    }

    void SpawnOneShockwave(Vector3 origin, Vector2 dir)
    {
        var wave = Instantiate(shockwavePrefab, origin, Quaternion.identity);
        EnemySceneCleanup.PlaceInSourceScene(wave.gameObject, this);
        wave.Init(dir, shockwaveSpeed, shockwaveLifetime, GetComponent<Collider2D>());
    }
}
