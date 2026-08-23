using System.Collections;
using System.Collections.Generic;
using FMOD.Studio;
using FMODUnity;
using UnityEngine;

/// <summary>
/// 机器人部署模式：短按跟随 / 长按驻守。
/// </summary>
public enum RobotDeployMode
{
    Follow,
    Stationed
}

/// <summary>
/// 友军机器人 AI 控制器。
/// 行为：记录回归锚点（跟随点或生成点）→ 索敌（以自身为中心）→ 接近目标 → 进入攻击范围后原地攻击（CD）
///        → 仅当敌人离开攻击范围才重新追击
///        → 无目标/超出最大追踪范围时返回锚点。
/// 跟随模式：无敌人时弱跟随身后锚点；遇矮障可自动跳跃。
/// 伤害输出依赖武器子物体上挂载的 Attack.cs（OnTriggerStay2D）。
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
public class AllyRobot : MonoBehaviour
{
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
        ComboDashing,
        ManualMove,
        Recalling
    }

    enum RobotAirPhase
    {
        Ground = 0,
        Jump = 1,
        Fall = 2
    }

    AllyState currentState;

    [Header("移动")]
    public float moveSpeed = 3f;
    [Tooltip("Combo 冲锋冲刺阶段速度（单位/秒）")]
    public float dashSpeed = 12f;
    [Tooltip("冲刺开始后超过此时间仍未进入近战距离则退出冲刺")]
    public float dashTimeout = 1.5f;
    [Tooltip("到达目标点时判定为'已到达'的距离阈值")]
    public float arriveThreshold = 0.15f;
    [Tooltip("跟随模式：距锚点超过此距离才开始回跟")]
    public float followArriveDistance = 0.35f;
    [Tooltip("跟随点为空时，相对玩家身后的水平偏移")]
    public float followOffsetX = 1.2f;

    [Header("索敌")]
    [Tooltip("以自身为中心的 X 轴单侧索敌半径")]
    public float detectRangeX = 6f;
    [Tooltip("以自身为中心的 Y 轴单侧索敌半径（与空中敌相同，过滤不同高度平台上的目标）")]
    public float detectRangeY = 6f;

    [Header("攻击")]
    [Tooltip("开始攻击的最大距离（X 轴）")]
    public float attackDistance = 1.2f;
    [Tooltip("停刀/到位的最大 Y 距离；|ΔY| 超出则与空中敌相同：不追击/近战，仅连携与激光可响应；同时作为地面连携/Blast 是否改竖直冲锋的阈值。应明显小于 attackDistance，避免偏低打空")]
    [SerializeField] float airAttackDistanceY = 0.65f;
    [Tooltip("Combo 时是否发起冲刺的判定距离（X 轴）。目标超出则冲刺，否则直接近战连击")]
    public float dashDecideDistance = 2.5f;
    [Tooltip("每次攻击之间的冷却时间（秒）")]
    public float attackCooldown = 1.5f;
    [Header("攻击前冲（Animation Event）")]
    [Tooltip("BeginAttackLunge 默认水平速度（单位/秒）")]
    public float attackLungeSpeed = 3.5f;
    [Tooltip("BeginAttackLunge 无参或 float<=0 时使用的默认时长（秒）")]
    public float attackLungeDuration = 0.15f;

    [Header("Blast 三连击")]
    [Tooltip("Blast 每段开始时的 X 轴单侧索敌半径")]
    [SerializeField] float blastComboDetectRangeX = 6f;
    [Tooltip("Blast 每段开始时的 Y 轴单侧索敌半径（含空中敌）")]
    [SerializeField] float blastComboDetectRangeY = 6f;
    [Tooltip("段内前冲补距上限；目标超出 attackDistance 时最多额外冲这么远")]
    [SerializeField] float blastComboExtraLungeMax = 1.5f;

    [Header("连携空中滞空")]
    [Tooltip("空中连携攻击时的重力倍率（相对正常 gravityScale）")]
    [SerializeField] float comboAirHangGravityScale = 0.2f;
    [Tooltip("空中连携滞空时长（秒）；连携结束或落地提前解除，Blast 每段会刷新")]
    [SerializeField] float comboAirHangDuration = 0.55f;
    [Tooltip("对空中敌人冲刺/连携/爆裂时显示的推进特效子物体；为空则按名称 Boost 查找")]
    [SerializeField] GameObject boostVisual;

    [Header("贯穿激光（持续弹触发）")]
    [SerializeField] int laserDamage = 10;
    [SerializeField] float laserRange = 10f;
    [SerializeField] float laserVisualDuration = 0.12f;
    [SerializeField] float laserWidth = 0.08f;
    [SerializeField] Color laserColor = new Color(0.3f, 1f, 1f, 1f);
    [SerializeField] LayerMask laserHitMask = ~0;
    [Tooltip("可替换的激光视觉 Prefab（需挂 AllyRobotPierceLaserVisual）；为空时回退 LineRenderer")]
    [SerializeField] GameObject pierceLaserVisualPrefab;
    [Tooltip("激光伤害源 Tag；设为 Electric 可穿透护盾直击盾兵")]
    [SerializeField] string laserAttackTag = "Electric";
    [Tooltip("瞄准时最小水平距离；近距垫高 |dx| 避免高度差把角度推过 11.25° 误锁斜向")]
    [SerializeField] float laserMinAimHorizontal = 2f;

    // 16 向：每 22.5° 一档
    static readonly Vector2[] LaserDirs16 =
    {
        new Vector2(1f, 0f),
        new Vector2(1f, Mathf.Tan(22.5f * Mathf.Deg2Rad)).normalized,
        new Vector2(1f, 1f).normalized,
        new Vector2(Mathf.Tan(22.5f * Mathf.Deg2Rad), 1f).normalized,
        new Vector2(0f, 1f),
        new Vector2(-Mathf.Tan(22.5f * Mathf.Deg2Rad), 1f).normalized,
        new Vector2(-1f, 1f).normalized,
        new Vector2(-1f, Mathf.Tan(22.5f * Mathf.Deg2Rad)).normalized,
        new Vector2(-1f, 0f),
        new Vector2(-1f, -Mathf.Tan(22.5f * Mathf.Deg2Rad)).normalized,
        new Vector2(-1f, -1f).normalized,
        new Vector2(-Mathf.Tan(22.5f * Mathf.Deg2Rad), -1f).normalized,
        new Vector2(0f, -1f),
        new Vector2(Mathf.Tan(22.5f * Mathf.Deg2Rad), -1f).normalized,
        new Vector2(1f, -1f).normalized,
        new Vector2(1f, -Mathf.Tan(22.5f * Mathf.Deg2Rad)).normalized,
    };

    [Header("最大追踪范围")]
    [Tooltip("以回归锚点为圆心，超过此距离强制返回")]
    public float maxChaseRange = 10f;

    [Header("自动跳跃")]
    [SerializeField] PhysicsCheck physicsCheck;
    [Tooltip("起跳高度（仅用于计算起跳初速度，不参与是否可越过判定）")]
    public float jumpHeight = 2.2f;
    [Tooltip("可自动越过的障碍顶面相对脚底的最大高度")]
    public float maxAutoJumpHeight = 1.4f;
    [Tooltip("低于此高度的凸起不触发跳跃")]
    public float minObstacleHeight = 0.25f;
    [Tooltip("前方障碍水平探测距离")]
    public float jumpProbeDistance = 0.55f;
    [Tooltip("水平探测原点相对脚底的高度（必须 > 0，贴地会误判地面为障碍）")]
    public float jumpProbeHeight = 0.45f;
    [Tooltip("水平探测原点相对身体前缘再向前的额外偏移")]
    public float jumpProbeForwardPadding = 0.05f;
    [Tooltip("起跳所需头顶净空；与 jumpHeight 解耦，避免把 jumpHeight 调大后误拦跳跃")]
    public float jumpCeilingClearance = 0.6f;
    [Tooltip("两次自动跳跃之间的最短间隔")]
    public float jumpCooldown = 0.35f;
    [Tooltip("落地后短暂停稳时长（秒），0 则等 Land 动画播完")]
    public float landDuration = 0.2f;
    [SerializeField] LayerMask jumpObstacleMask;

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
    [Tooltip("连击协同攻击 Animator 状态名（强制打断近战用）")]
    public string comboAttackStateName = "ComboAttack";
    [Tooltip("冲刺终结攻击 Trigger 参数名")]
    public string dashAttackTriggerName = "dashAttack";
    [Tooltip("冲刺终结攻击 Animator 状态名（用于检测动画结束）")]
    public string dashAttackStateName = "DashAttack";
    [Tooltip("Blast 终结攻击 Trigger 参数名")]
    public string blastAttackTriggerName = "blastAttack";
    [Tooltip("Blast 三段连击 Animator 状态名（下标 0/1/2）")]
    public string[] blastAttackStateNames = { "BlastAttack1", "BlastAttack2", "BlastAttack3" };
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
    [Tooltip("收回动画 Animator 状态名")]
    public string recallStateName = "RobotRecall";
    [Tooltip("AirPhase Int 参数名（0 Ground / 1 Jump / 2 Fall）")]
    public string airPhaseParamName = "AirPhase";
    public string jumpStateName = "Jump";
    public string fallStateName = "Fall";
    public string landStateName = "Land";

    [Header("事件监听")]
    [SerializeField] VoidEventSO robotComboEvent;
    [SerializeField] VoidEventSO robotBlastComboEvent;

    [Header("音效")]
    [SerializeField] EventReference attackEvent;
    [Tooltip("FMOD Attack 事件上的标签参数名")]
    [SerializeField] string attackTypeParam = "AttackType";
    [SerializeField] EventReference dashEvent;
    [SerializeField] EventReference laserEvent;

    [Header("牵引召回 (Ability2 短按)")]
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
    [Tooltip("牵引结束后玩家无敌残留时长（秒）")]
    public float pullInvulnerableLinger = 0.5f;
    [Tooltip("收回时玩家距落点几乎不再接近超过此时长则自动放下（秒，0 = 关闭）")]
    [SerializeField] float pullStuckTimeout = 1.5f;
    [Tooltip("每帧向落点靠近少于此值（世界单位）视为无进展")]
    [SerializeField] float pullStuckProgressEpsilon = 0.02f;

    public bool IsPulling => pullInProgress;
    public bool IsPlayerHooked => playerHooked;
    public bool IsManualMoving =>
        currentState == AllyState.ManualMove || pendingStationOnLand;
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

    public bool IsComboDashing => currentState == AllyState.ComboDashing;

    public bool IsRecalling => currentState == AllyState.Recalling;

    bool IsAirborneBusy =>
        airPhase != RobotAirPhase.Ground || isLanding;

    /// <summary>
    /// 这些状态下不播 Jump/Fall/Land，避免打断专用动画（冲刺、牵引、生成）。
    /// </summary>
    bool SuppressAirAnim =>
        currentState == AllyState.Spawning
        || currentState == AllyState.Recalling
        || IsPulling
        || IsBusyWithCombo;

    const float ManualMoveInputThreshold = 0.5f;

    Vector3 HomeAnchor
    {
        get
        {
            if (deployMode == RobotDeployMode.Follow)
                return ResolveFollowAnchor();
            return spawnPoint;
        }
    }

    Rigidbody2D rb;
    CapsuleCollider2D bodyCollider;
    RobotOneWayPlatformPass oneWayPlatformPass;

    Vector3 spawnPoint;
    Transform currentTarget;
    float attackTimer;
    float pullCooldownTimer;
    bool pullInProgress;
    /// <summary>忙碌态并行钩锁：不切 Pulling、不播机器人 pull 动画、结束后不改当前状态。</summary>
    bool pullOverlayMode;
    bool playerHooked;
    float pullStuckTimer;
    float lastPullRemaining = -1f;

    Transform owner;
    PlayerMovement ownerMovement;
    Character ownerCharacter;
    Rigidbody2D ownerRb;
    AllyRobotPullVisual pullVisual;
    bool comboAttackAnimSeen;
    bool comboDashWindupAnimSeen;
    float dashTimer;
    bool pendingBlastFinisher;
    int blastComboStep = -1;
    const int BlastComboHitCount = 3;
    bool attackLungeActive;
    float attackLungeTimer;
    float attackLungeFaceDir = 1f;
    float attackLungeCurrentSpeed;
    bool pendingRetarget;
    bool dispatchAnimSeen;
    bool recallAnimSeen;
    LineRenderer laserLine;
    Coroutine laserVisualRoutine;
    Attack laserAttackSource;
    EventInstance dashInstance;

    RobotDeployMode deployMode = RobotDeployMode.Stationed;
    Transform followPoint;
    bool idleFollowing;
    float idleFollowDir;

    Vector2 manualMoveInput;
    bool manualJumpHeldPrev;
    bool pendingStationOnLand;

    RobotAirPhase airPhase = RobotAirPhase.Ground;
    bool leftGround;
    bool isLanding;
    float landTimer;
    float jumpCooldownTimer;
    float airTimer;
    float normalGravityScale = 1f;
    float comboAirHangTimer;
    bool comboAirHanging;
    /// <summary>本次连携曾以空中敌为目标时锁存 Boost，目标死亡也不关，直到连携结束。</summary>
    bool airComboBoostLatched;
    /// <summary>本次连携冲锋因 Y 差距或越崖启用二维冲锋后锁存，避免贴近后切回水平受重力下落抖动。</summary>
    bool verticalComboDashLatched;
    /// <summary>
    /// 自动跳起后，在障碍仍被探测到前禁止再次自动跳，避免贴墙连跳。
    /// </summary>
    bool suppressAutoJumpUntilObstacleClears;
    bool wasGrounded;
    bool airStateInitialized;
    float slopeDetachTimer;
    const float MinAirTime = 0.05f;
    const float DescendVelocityThreshold = 0.01f;
    const float MicroAirHitchMaxTime = 0.25f;
    const float MicroAirHitchMaxFallSpeed = 8f;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        bodyCollider = GetComponent<CapsuleCollider2D>();
        oneWayPlatformPass = GetComponent<RobotOneWayPlatformPass>();
        if (anim == null)
            anim = GetComponent<Animator>();
        if (physicsCheck == null)
            physicsCheck = GetComponent<PhysicsCheck>();
        rb.constraints = RigidbodyConstraints2D.FreezeRotation;
        normalGravityScale = rb.gravityScale;
        pullVisual = GetComponentInChildren<AllyRobotPullVisual>(true);
        ResolveBoostVisual();

        if (jumpObstacleMask.value == 0 && physicsCheck != null)
            jumpObstacleMask = physicsCheck.groundLayer;
    }

    void ResolveBoostVisual()
    {
        if (boostVisual == null)
        {
            Transform boostTf = transform.Find("Boost");
            if (boostTf != null)
                boostVisual = boostTf.gameObject;
        }

        SetBoostActive(false);
    }

    void Start()
    {
        pullVisual?.Initialize(this);
        spawnPoint = transform.position;
        attackTimer = 0f;
        FaceRight();
        SetAirPhase(RobotAirPhase.Ground, forcePlay: false);
        SwitchState(AllyState.Spawning);
    }

    void OnEnable()
    {
        if (robotComboEvent != null)
            robotComboEvent.OnEventRaised += ComboAttack;
        if (robotBlastComboEvent != null)
            robotBlastComboEvent.OnEventRaised += BlastCombo;
    }

    void OnDisable()
    {
        if (robotComboEvent != null)
            robotComboEvent.OnEventRaised -= ComboAttack;
        if (robotBlastComboEvent != null)
            robotBlastComboEvent.OnEventRaised -= BlastCombo;
        airComboBoostLatched = false;
        verticalComboDashLatched = false;
        SetBoostActive(false);
        StopDashSfx();
    }

    void OnDestroy()
    {
        if (laserVisualRoutine != null)
            StopCoroutine(laserVisualRoutine);

        if (pullInProgress)
        {
            pullVisual?.Cancel();
            pullInProgress = false;
            pullOverlayMode = false;
            if (playerHooked)
                EndPull();
            ClearPullHookState();
        }
    }

    void Update()
    {
        attackTimer -= Time.deltaTime;
        pullCooldownTimer = Mathf.Max(0f, pullCooldownTimer - Time.deltaTime);
        jumpCooldownTimer = Mathf.Max(0f, jumpCooldownTimer - Time.deltaTime);
        idleFollowing = false;

        if (currentState != AllyState.Recalling)
            UpdateAirAndLanding();

        // 并行钩锁（Blast 等忙碌态）需在原状态 Update 之外单独推进
        if (pullInProgress && currentState != AllyState.Pulling)
            UpdatePulling();

        switch (currentState)
        {
            case AllyState.Spawning:         UpdateSpawning();         break;
            case AllyState.Recalling:        UpdateRecalling();        break;
            case AllyState.Idle:             UpdateIdle();             break;
            case AllyState.Chase:            UpdateChase();            break;
            case AllyState.Attack:           UpdateAttack();           break;
            case AllyState.Return:           UpdateReturn();           break;
            case AllyState.Pulling:          UpdatePulling();          break;
            case AllyState.ComboAttacking:   UpdateComboAttacking();   break;
            case AllyState.ComboDashWindup:  UpdateComboDashWindup();  break;
            case AllyState.ComboDashing:     UpdateComboDashing();     break;
            case AllyState.ManualMove:       UpdateManualMove();       break;
        }

        UpdateComboBoostVisual();
    }

    void FixedUpdate()
    {
        // 单向平台 Ignore 须在接地检测与物理步进前完成。
        if (oneWayPlatformPass != null)
            oneWayPlatformPass.UpdateCollisions();

        // 跳跃判定在 FixedUpdate；同步刷新接地，避免只靠 Update 的一帧延迟。
        if (physicsCheck != null)
            physicsCheck.Check();

        if (slopeDetachTimer > 0f)
            slopeDetachTimer -= Time.fixedDeltaTime;

        UpdateComboAirHang();
        UpdateSlopeGravity();

        if (isLanding)
        {
            EndAttackLunge();
            StopMoving();
            return;
        }

        bool movingHorizontally = false;
        float moveDir = 0f;

        if (attackLungeActive)
        {
            attackLungeTimer -= Time.fixedDeltaTime;
            if (attackLungeTimer <= 0f)
            {
                EndAttackLunge();
            }
            else
            {
                movingHorizontally = true;
                moveDir = attackLungeFaceDir;
                ApplyHorizontalMove(moveDir, attackLungeCurrentSpeed, respectLedge: false);
            }
        }

        if (!attackLungeActive)
        {
            switch (currentState)
            {
                case AllyState.Chase:
                    if (TryGetChaseMoveDir(out moveDir)
                        && ApplyHorizontalMove(moveDir, moveSpeed))
                        movingHorizontally = true;
                    else
                        StopMoving();
                    break;
                case AllyState.Return:
                    if (TryGetHomeMoveDir(arriveThreshold, out moveDir)
                        && ApplyHorizontalMove(moveDir, moveSpeed))
                        movingHorizontally = true;
                    else
                        StopMoving();
                    break;
                case AllyState.ComboDashing:
                    if (TryApplyVerticalComboDash())
                    {
                        movingHorizontally = Mathf.Abs(rb.linearVelocity.x) > 0.01f;
                        moveDir = Mathf.Sign(rb.linearVelocity.x);
                    }
                    else if (TryGetChaseMoveDir(out moveDir)
                        && ApplyHorizontalMove(moveDir, dashSpeed))
                    {
                        movingHorizontally = true;
                    }
                    else
                    {
                        StopMoving();
                    }
                    break;
                case AllyState.Idle:
                    if (idleFollowing
                        && ApplyHorizontalMove(idleFollowDir, moveSpeed))
                    {
                        movingHorizontally = true;
                        moveDir = idleFollowDir;
                    }
                    else if (airPhase == RobotAirPhase.Ground)
                    {
                        StopMoving();
                    }
                    break;
                case AllyState.ManualMove:
                    if (!pendingStationOnLand
                        && TryGetManualMoveDir(out moveDir)
                        && ApplyHorizontalMove(moveDir, moveSpeed, respectLedge: false))
                    {
                        movingHorizontally = true;
                    }
                    else if (airPhase == RobotAirPhase.Ground)
                    {
                        StopMoving();
                    }
                    break;
                case AllyState.Spawning:
                case AllyState.Recalling:
                case AllyState.Pulling:
                case AllyState.ComboAttacking:
                case AllyState.ComboDashWindup:
                case AllyState.Attack:
                    if (airPhase == RobotAirPhase.Ground)
                        StopMoving();
                    break;
            }
        }

        if (movingHorizontally)
        {
            CancelVelocityIntoWall(moveDir);
            CancelVelocityIntoSlope();
            // 手动遥控只用↑跳跃，关闭障碍自动跳；前冲也不自动跳；竖直/二维连携冲刺禁用自动跳
            if (currentState != AllyState.ManualMove
                && !attackLungeActive
                && !(currentState == AllyState.ComboDashing && NeedsVerticalComboDash(currentTarget)))
                TryAutoJump(moveDir);
        }
        else if (airPhase != RobotAirPhase.Ground)
        {
            CancelVelocityIntoWall(Mathf.Sign(transform.localScale.x));
        }
        else
        {
            CancelVelocityIntoSlope();
        }
    }

    public void Initialize(Transform player)
    {
        Initialize(player, RobotDeployMode.Stationed, null);
    }

    public void Initialize(Transform player, RobotDeployMode mode, Transform follow)
    {
        owner = player;
        ownerMovement = player != null ? player.GetComponent<PlayerMovement>() : null;
        ownerCharacter = player != null ? player.GetComponent<Character>() : null;
        ownerRb = player != null ? player.GetComponent<Rigidbody2D>() : null;
        deployMode = mode;
        followPoint = follow;
    }

    /// <summary>
    /// 播放 RobotRecall，结束后销毁自身。已在收回中则忽略。
    /// </summary>
    public bool BeginRecall()
    {
        if (currentState == AllyState.Recalling)
            return false;

        CancelPullForRecall();
        EndAttackLunge();
        EndComboAirHang();
        isLanding = false;
        landTimer = 0f;
        SwitchState(AllyState.Recalling);
        return true;
    }

    void CancelPullForRecall()
    {
        if (!pullInProgress)
            return;

        pullVisual?.Cancel();
        pullInProgress = false;
        pullOverlayMode = false;
        if (playerHooked)
            EndPull();
        ClearPullHookState();
    }

    /// <summary>
    /// 由玩家每帧下发 RobotMove。牵引 / 生成中输入无效。
    /// </summary>
    public void SetManualMoveInput(Vector2 input)
    {
        if (currentState == AllyState.Spawning || currentState == AllyState.Recalling || IsPulling)
            return;

        bool hasInput = Mathf.Abs(input.x) > ManualMoveInputThreshold
            || Mathf.Abs(input.y) > ManualMoveInputThreshold;

        if (hasInput)
        {
            manualMoveInput = input;
            if (pendingStationOnLand)
                pendingStationOnLand = false;

            if (currentState != AllyState.ManualMove)
                BeginManualMove();
        }
        else
        {
            manualMoveInput = Vector2.zero;
            manualJumpHeldPrev = false;

            if (currentState == AllyState.ManualMove && !pendingStationOnLand)
                EndManualMove();
        }
    }

    void BeginManualMove()
    {
        EndAttackLunge();
        blastComboStep = -1;
        SetDashActive(false);
        if (anim != null)
        {
            anim.ResetTrigger(attackTriggerName);
            anim.ResetTrigger(comboAttackTriggerName);
            anim.ResetTrigger(dashAttackTriggerName);
            anim.ResetTrigger(blastAttackTriggerName);
            anim.ResetTrigger(dashStartTriggerName);
            anim.ResetTrigger(pullTriggerName);
        }

        currentTarget = null;
        pendingRetarget = false;
        pendingStationOnLand = false;
        SwitchState(AllyState.ManualMove);
    }

    void EndManualMove()
    {
        deployMode = RobotDeployMode.Stationed;
        StopMoving();
        if (anim != null)
            anim.SetBool(walkBoolName, false);

        if (IsAirborneBusy || !IsSolidGrounded())
        {
            pendingStationOnLand = true;
            return;
        }

        CommitStationedAtCurrentPosition();
    }

    void CommitStationedAtCurrentPosition()
    {
        pendingStationOnLand = false;
        spawnPoint = transform.position;
        deployMode = RobotDeployMode.Stationed;
        manualMoveInput = Vector2.zero;
        manualJumpHeldPrev = false;
        SwitchState(AllyState.Idle);
    }

    bool TryGetManualMoveDir(out float moveDir)
    {
        if (Mathf.Abs(manualMoveInput.x) > ManualMoveInputThreshold)
        {
            moveDir = Mathf.Sign(manualMoveInput.x);
            return true;
        }

        moveDir = 0f;
        return false;
    }

    void UpdateManualMove()
    {
        if (pendingStationOnLand)
        {
            if (!IsAirborneBusy && IsSolidGrounded())
            {
                CommitStationedAtCurrentPosition();
                return;
            }

            if (anim != null && !IsAirborneBusy)
                anim.SetBool(walkBoolName, false);
            return;
        }

        if (isLanding)
            return;

        float moveX = 0f;
        if (TryGetManualMoveDir(out moveX))
        {
            SetFacing(moveX);
            if (!IsAirborneBusy && anim != null)
                anim.SetBool(walkBoolName, true);
        }
        else if (!IsAirborneBusy && anim != null)
        {
            anim.SetBool(walkBoolName, false);
        }

        bool jumpHeld = manualMoveInput.y > ManualMoveInputThreshold;
        if (jumpHeld && !manualJumpHeldPrev
            && !IsAirborneBusy
            && jumpCooldownTimer <= 0f)
        {
            float jumpDir = !Mathf.Approximately(moveX, 0f)
                ? moveX
                : Mathf.Sign(transform.localScale.x);
            if (Mathf.Approximately(jumpDir, 0f))
                jumpDir = 1f;
            PerformJump(jumpDir);
        }

        manualJumpHeldPrev = jumpHeld;
    }

    Vector3 ResolveFollowAnchor()
    {
        if (followPoint != null)
            return followPoint.position;

        if (owner == null)
            return spawnPoint;

        float face = ownerMovement != null ? ownerMovement.FaceDirection : 1f;
        if (Mathf.Approximately(face, 0f))
            face = 1f;

        return owner.position + Vector3.left * face * followOffsetX;
    }

    public void RequestRetarget()
    {
        if (IsPulling || IsBusyWithCombo || currentState == AllyState.Spawning
            || currentState == AllyState.Recalling || isLanding
            || currentState == AllyState.ManualMove || pendingStationOnLand)
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
        if (IsPulling)
            return TryReleasePulledPlayer();

        if (pullCooldownTimer > 0f)
            return false;

        if (currentState == AllyState.Spawning || currentState == AllyState.Recalling)
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

        // 空中落地 / 连携 / 手动遥控等：并行钩锁，不切 Pulling、不播机器人 pull、不转身
        bool overlay = IsAirborneBusy
            || IsBusyWithCombo
            || currentState == AllyState.ManualMove
            || pendingStationOnLand;

        if (!overlay)
            FaceTarget(owner.position);
        if (!overlay && anim != null)
            anim.SetTrigger(pullTriggerName);

        pullInProgress = true;
        pullOverlayMode = overlay;

        if (pullVisual != null)
            pullVisual.Begin(owner, pullExtendSpeed, pullSpeed, 0f, pullArriveThreshold);
        else
            BeginPullWithoutVisual();

        if (!overlay)
            SwitchState(AllyState.Pulling);

        pullCooldownTimer = PullCooldown;
        return true;
    }

    public void ComboAttack()
    {
        if (IsPulling || IsBusyWithCombo
            || currentState == AllyState.Recalling
            || currentState == AllyState.ManualMove || pendingStationOnLand)
            return;

        if (!TryAcquireTarget(out Transform target, includeAirEnemy: true))
            return;

        // 跳跃 / 下落 / 落地过程中允许立即打断空中动画进入连携
        InterruptAirForCombo();

        currentTarget = target;
        pendingBlastFinisher = false;

        if (IsWithinDashDecideRange(currentTarget))
        {
            BeginComboAttack();
            return;
        }

        BeginComboDashWindup();
    }

    public void BlastCombo()
    {
        if (IsPulling || IsBusyWithCombo
            || currentState == AllyState.Recalling
            || currentState == AllyState.ManualMove || pendingStationOnLand)
            return;

        if (!TryAcquireTarget(out Transform target, includeAirEnemy: true))
            return;

        InterruptAirForCombo();

        currentTarget = target;
        pendingBlastFinisher = true;

        if (IsWithinDashDecideRange(currentTarget))
        {
            BeginBlastAttack();
            return;
        }

        BeginComboDashWindup();
    }

    public void PlayAttackSfx(string attackType)
    {
        if (string.IsNullOrEmpty(attackType))
            return;

        FmodAudio.Play(attackEvent, attackTypeParam, attackType);
    }

    void StartDashSfx()
    {
        StopDashSfx();
        dashInstance = FmodAudio.PlayHeld(dashEvent);
    }

    void StopDashSfx()
    {
        FmodAudio.Stop(ref dashInstance);
    }

    /// <summary>
    /// 取消落地锁停，让 ForcePlayCombatAnim 能立刻切到连携动画。
    /// 逻辑空中阶段保留，连携结束后由 SyncAirVisualAfterBusy 接回 Fall/Ground。
    /// </summary>
    void InterruptAirForCombo()
    {
        isLanding = false;
        landTimer = 0f;
    }

    public bool TryFirePierceLaser()
    {
        if (currentState == AllyState.Spawning || currentState == AllyState.Recalling || IsPulling
            || currentState == AllyState.ManualMove || pendingStationOnLand)
            return false;

        Transform aimTarget = null;
        if (IsValidCombatTarget(currentTarget, allowAirEnemy: true))
            aimTarget = currentTarget;
        else if (TryAcquireTarget(out Transform acquired, includeAirEnemy: true))
        {
            // 空中敌 / Y 差距过大：不写入 currentTarget，避免激光后误入追击/近战
            if (CanPersistAsChaseTarget(acquired))
                currentTarget = acquired;
            aimTarget = acquired;
        }

        Vector2 origin = transform.position;
        Vector2 dir;
        if (aimTarget != null)
        {
            Vector2 aimPoint = GetCombatAimPoint(aimTarget);
            FaceTarget(aimPoint);
            Vector2 toTarget = aimPoint - origin;
            if (toTarget.sqrMagnitude < 0.0001f)
            {
                float face = Mathf.Sign(transform.localScale.x);
                if (Mathf.Approximately(face, 0f))
                    face = 1f;
                dir = new Vector2(face, 0f);
            }
            else
            {
                float faceX = Mathf.Sign(transform.localScale.x);
                float sx = Mathf.Sign(toTarget.x);
                if (Mathf.Approximately(sx, 0f))
                    sx = Mathf.Approximately(faceX, 0f) ? 1f : faceX;
                if (Mathf.Abs(toTarget.x) < laserMinAimHorizontal)
                    toTarget.x = sx * laserMinAimHorizontal;
                dir = SnapToNearestLaserDir(toTarget);
            }
        }
        else
        {
            float face = Mathf.Sign(transform.localScale.x);
            if (Mathf.Approximately(face, 0f))
                face = 1f;
            dir = new Vector2(face, 0f);
        }

        ApplyPierceLaserDamage(origin, dir);
        ShowPierceLaserVisual(origin, dir);
        FmodAudio.Play(laserEvent);
        return true;
    }

    static Vector2 SnapToNearestLaserDir(Vector2 desired)
    {
        if (desired.sqrMagnitude < 0.0001f)
            return Vector2.right;

        desired.Normalize();
        Vector2 best = LaserDirs16[0];
        float bestDot = Vector2.Dot(desired, best);
        for (int i = 1; i < LaserDirs16.Length; i++)
        {
            float d = Vector2.Dot(desired, LaserDirs16[i]);
            if (d > bestDot)
            {
                bestDot = d;
                best = LaserDirs16[i];
            }
        }
        return best;
    }

    void ApplyPierceLaserDamage(Vector2 origin, Vector2 dir)
    {
        EnsureLaserAttackSource();
        laserAttackSource.damage = laserDamage;
        laserAttackSource.attackType = AttackType.Melee;

        RaycastHit2D[] hits = Physics2D.RaycastAll(origin, dir, laserRange, laserHitMask);
        var damaged = new HashSet<Character>();

        for (int i = 0; i < hits.Length; i++)
        {
            Collider2D col = hits[i].collider;
            if (col == null)
                continue;

            Transform hitTf = col.transform;
            if (hitTf == transform || hitTf.IsChildOf(transform))
                continue;

            Character character = col.GetComponentInParent<Character>();
            if (character == null || damaged.Contains(character))
                continue;
            if (character == ownerCharacter)
                continue;
            if (col.GetComponentInParent<Enemy>() == null)
                continue;

            damaged.Add(character);
            character.TakeDamage(laserAttackSource);
        }
    }

    void EnsureLaserAttackSource()
    {
        if (laserAttackSource != null)
            return;

        var go = new GameObject("PierceLaserAttack");
        go.transform.SetParent(transform, false);
        if (!string.IsNullOrEmpty(laserAttackTag))
            go.tag = laserAttackTag;
        laserAttackSource = go.AddComponent<Attack>();
        laserAttackSource.attackType = AttackType.Melee;
        laserAttackSource.ignoreTag = "Player";
    }

    void ShowPierceLaserVisual(Vector2 origin, Vector2 dir)
    {
        Vector2 end = origin + dir * laserRange;

        if (pierceLaserVisualPrefab != null)
        {
            GameObject go = Instantiate(pierceLaserVisualPrefab, origin, Quaternion.identity);
            var visual = go.GetComponent<AllyRobotPierceLaserVisual>();
            if (visual != null)
            {
                visual.Setup(origin, dir, laserRange, laserVisualDuration);
                return;
            }

            Destroy(go);
            Debug.LogWarning(
                $"Pierce laser prefab '{pierceLaserVisualPrefab.name}' is missing AllyRobotPierceLaserVisual.",
                pierceLaserVisualPrefab);
        }

        EnsureLaserLineRenderer();
        laserLine.enabled = true;
        laserLine.SetPosition(0, origin);
        laserLine.SetPosition(1, end);

        if (laserVisualRoutine != null)
            StopCoroutine(laserVisualRoutine);
        laserVisualRoutine = StartCoroutine(HidePierceLaserVisualAfterDelay());
    }

    void EnsureLaserLineRenderer()
    {
        if (laserLine != null)
            return;

        laserLine = gameObject.AddComponent<LineRenderer>();
        laserLine.positionCount = 2;
        laserLine.useWorldSpace = true;
        laserLine.numCapVertices = 2;
        laserLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        laserLine.receiveShadows = false;
        laserLine.material = new Material(Shader.Find("Sprites/Default"));
        laserLine.startWidth = laserWidth;
        laserLine.endWidth = laserWidth;
        laserLine.startColor = laserColor;
        laserLine.endColor = laserColor;
        laserLine.enabled = false;
    }

    IEnumerator HidePierceLaserVisualAfterDelay()
    {
        yield return new WaitForSeconds(laserVisualDuration);
        if (laserLine != null)
            laserLine.enabled = false;
        laserVisualRoutine = null;
    }

    /// <summary>
    /// Animation Event：开始朝面向方向轻微前冲，使用 Inspector 默认时长。
    /// </summary>
    public void BeginAttackLunge()
    {
        BeginAttackLunge(attackLungeDuration);
    }

    /// <summary>
    /// Animation Event：开始朝面向方向轻微前冲。
    /// float 参数为时长（秒）；&lt;=0 时回退到 attackLungeDuration。
    /// </summary>
    public void BeginAttackLunge(float duration)
    {
        float len = duration > 0f ? duration : attackLungeDuration;
        if (len <= 0f || attackLungeSpeed <= 0f)
            return;

        float face = Mathf.Sign(transform.localScale.x);
        if (Mathf.Approximately(face, 0f))
            face = 1f;

        attackLungeActive = true;
        attackLungeTimer = len;
        attackLungeFaceDir = face;
        attackLungeCurrentSpeed = attackLungeSpeed;

        if (blastComboStep < 0 || !IsValidCombatTarget(currentTarget, allowAirEnemy: true))
            return;

        float extra = Mathf.Max(0f, GetDistXTo(currentTarget) - attackDistance);
        if (extra <= 0f)
            return;

        extra = Mathf.Min(extra, Mathf.Max(0f, blastComboExtraLungeMax));
        float defaultDist = attackLungeSpeed * len;
        attackLungeCurrentSpeed = (defaultDist + extra) / len;
    }

    /// <summary>
    /// Animation Event：立即结束前冲。不挂事件时靠时长自动结束。
    /// </summary>
    public void EndAttackLunge()
    {
        if (!attackLungeActive)
            return;

        attackLungeActive = false;
        attackLungeTimer = 0f;
        attackLungeCurrentSpeed = 0f;
        StopMoving();
    }

    void BeginComboAttack()
    {
        blastComboStep = -1;
        EndAttackLunge();
        StopMoving();
        if (anim != null)
            anim.SetBool(walkBoolName, false);

        if (IsValidCombatTarget(currentTarget))
            FaceTarget(currentTarget.position);

        ForcePlayCombatAnim(comboAttackStateName, comboAttackTriggerName);

        attackTimer = attackCooldown;
        SwitchState(AllyState.ComboAttacking);
    }

    void BeginDashAttack()
    {
        StopDashSfx();
        if (pendingBlastFinisher)
        {
            BeginBlastAttack();
            return;
        }

        blastComboStep = -1;
        EndAttackLunge();
        StopMoving();
        if (anim != null)
            anim.SetBool(walkBoolName, false);

        if (IsValidCombatTarget(currentTarget))
            FaceTarget(currentTarget.position);

        ForcePlayCombatAnim(dashAttackStateName, dashAttackTriggerName);

        attackTimer = attackCooldown;
        SwitchState(AllyState.ComboAttacking);
    }

    void BeginBlastAttack()
    {
        StopDashSfx();
        blastComboStep = 0;
        PlayBlastAttackStep(0);
    }

    void PlayBlastAttackStep(int step)
    {
        EndAttackLunge();
        StopMoving();
        if (anim != null)
            anim.SetBool(walkBoolName, false);

        RetargetBlastComboStep();

        string stateName = GetBlastAttackStateName(step);
        // 只靠 Play 切段；不要 SetTrigger(blastAttack)，否则 AnyState→BlastAttack1 会把 2/3 段拉回第一段
        ForcePlayCombatAnim(stateName, null);

        attackTimer = attackCooldown;
        comboAttackAnimSeen = false;
        if (currentState != AllyState.ComboAttacking)
            SwitchState(AllyState.ComboAttacking);
        else
            TryBeginComboAirHang();
    }

    string GetBlastAttackStateName(int step)
    {
        if (blastAttackStateNames != null
            && step >= 0
            && step < blastAttackStateNames.Length
            && !string.IsNullOrEmpty(blastAttackStateNames[step]))
            return blastAttackStateNames[step];

        return step switch
        {
            0 => "BlastAttack1",
            1 => "BlastAttack2",
            _ => "BlastAttack3",
        };
    }

    bool IsCurrentBlastAttackState(AnimatorStateInfo info)
    {
        if (blastComboStep < 0)
            return false;

        return info.IsName(GetBlastAttackStateName(blastComboStep));
    }

    void TryAdvanceBlastCombo()
    {
        int nextStep = blastComboStep + 1;
        if (nextStep >= BlastComboHitCount)
        {
            FinishBlastCombo();
            return;
        }

        blastComboStep = nextStep;
        PlayBlastAttackStep(nextStep);
    }

    void FinishBlastCombo()
    {
        EndAttackLunge();
        blastComboStep = -1;
        pendingBlastFinisher = false;
        ResumeStateAfterCombo();
    }

    void BeginComboDashWindup()
    {
        StopMoving();
        if (anim != null)
            anim.SetBool(walkBoolName, false);

        if (IsValidCombatTarget(currentTarget))
            FaceTarget(currentTarget.position);

        ForcePlayCombatAnim(dashStartStateName, dashStartTriggerName);
        StartDashSfx();

        SwitchState(AllyState.ComboDashWindup);
    }

    void ForcePlayCombatAnim(string stateName, string keepTriggerName)
    {
        if (anim == null || string.IsNullOrEmpty(stateName))
            return;

        anim.ResetTrigger(attackTriggerName);
        anim.ResetTrigger(comboAttackTriggerName);
        anim.ResetTrigger(dashAttackTriggerName);
        anim.ResetTrigger(blastAttackTriggerName);
        anim.ResetTrigger(dashStartTriggerName);
        anim.ResetTrigger(pullTriggerName);

        if (!string.IsNullOrEmpty(keepTriggerName))
            anim.SetTrigger(keepTriggerName);

        anim.Play(stateName, 0, 0f);
        anim.Update(0f);
    }

    void BeginPullWithoutVisual()
    {
        playerHooked = true;
        ResetPullStuckTracking();
        if (ownerCharacter != null)
            ownerCharacter.SetForcedInvulnerable(true);
        if (ownerMovement != null)
        {
            ownerMovement.BeginExternalControl(false);
            ownerMovement.SetForceOneWayPass(true);
        }
    }

    public Vector2 GetPullLandingPoint() => ComputeLandingPoint();

    public void OnHookGrabbed()
    {
        playerHooked = true;
        ResetPullStuckTracking();
        if (ownerCharacter != null)
            ownerCharacter.SetForcedInvulnerable(true);
        if (ownerMovement != null && !ownerMovement.IsActionLocked)
        {
            ownerMovement.BeginExternalControl(false);
            ownerMovement.SetForceOneWayPass(true);
        }
        else if (ownerMovement != null)
        {
            ownerMovement.SetForceOneWayPass(true);
        }
    }

    public void OnHookRetractStep(Vector2 hookPos)
    {
        if (!playerHooked || ownerRb == null)
            return;

        UpdatePullStuck(Vector2.Distance(ownerRb.position, ComputeLandingPoint()));
        if (!playerHooked)
            return;

        ownerMovement?.RefreshForceOneWayPass();
        ownerRb.MovePosition(hookPos);
    }

    public void OnHookRetractComplete()
    {
        if (playerHooked && ownerRb != null)
            ownerRb.position = ComputeLandingPoint();
        FinishPullSession();
    }

    public bool TryReleasePulledPlayer()
    {
        if (!playerHooked)
            return false;

        ReleasePulledPlayer();
        return true;
    }

    void ReleasePulledPlayer()
    {
        if (!playerHooked)
            return;

        playerHooked = false;
        ResetPullStuckTracking();
        EndPull();
    }

    void ResetPullStuckTracking()
    {
        pullStuckTimer = 0f;
        lastPullRemaining = -1f;
    }

    void ClearPullHookState()
    {
        playerHooked = false;
        ResetPullStuckTracking();
    }

    void UpdatePullStuck(float remaining)
    {
        if (lastPullRemaining < 0f)
        {
            lastPullRemaining = remaining;
            return;
        }

        float progress = lastPullRemaining - remaining;
        lastPullRemaining = remaining;

        if (remaining <= pullArriveThreshold)
        {
            pullStuckTimer = 0f;
            return;
        }

        if (progress < pullStuckProgressEpsilon)
            pullStuckTimer += Time.fixedDeltaTime;
        else
            pullStuckTimer = 0f;

        if (pullStuckTimeout > 0f && pullStuckTimer >= pullStuckTimeout)
            ReleasePulledPlayer();
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
            bool overlay = pullOverlayMode;
            pullInProgress = false;
            pullOverlayMode = false;
            if (playerHooked)
                EndPull();
            ClearPullHookState();
            if (!overlay && currentState == AllyState.Pulling)
            {
                if (anim != null)
                    anim.Play("Idle", 0, 0f);
                SwitchState(AllyState.Idle);
            }
            return;
        }

        if (pullVisual == null || !pullVisual.IsActive)
            UpdatePullingFallback();
    }

    void UpdatePullingFallback()
    {
        if (ownerMovement == null || ownerRb == null)
            return;

        if (!playerHooked)
        {
            FinishPullSession();
            return;
        }

        if (!ownerMovement.IsActionLocked)
            BeginPullWithoutVisual();

        Vector2 landing = ComputeLandingPoint();
        ownerMovement.RefreshForceOneWayPass();
        ownerMovement.StepExternalMove(landing, pullSpeed);
        UpdatePullStuck(Vector2.Distance(ownerRb.position, landing));

        if (!playerHooked)
        {
            FinishPullSession();
            return;
        }

        if (Vector2.Distance(ownerRb.position, landing) <= pullArriveThreshold)
        {
            ownerRb.position = landing;
            FinishPullSession();
        }
    }

    void EndPull()
    {
        if (ownerCharacter != null)
        {
            ownerCharacter.SetForcedInvulnerable(false);
            if (pullInvulnerableLinger > 0f)
                ownerCharacter.TriggerInvulnerable(pullInvulnerableLinger);
        }

        if (ownerMovement != null && ownerMovement.IsActionLocked)
            ownerMovement.EndExternalControl();
    }

    /// <summary>
    /// 结束钩锁会话。并行模式只释放玩家、保留机器人当前状态（如 Blast 三连）。
    /// </summary>
    void FinishPullSession()
    {
        if (!pullInProgress)
            return;

        bool overlay = pullOverlayMode;
        pullInProgress = false;
        pullOverlayMode = false;
        if (playerHooked)
            EndPull();
        ClearPullHookState();

        if (overlay)
            return;

        if (currentState == AllyState.Pulling)
            PerformRetarget();
    }

    void ResumeStateAfterPull()
    {
        PerformRetarget();
    }

    void SwitchState(AllyState next)
    {
        AllyState prev = currentState;
        OnExitState(prev);
        currentState = next;
        OnEnterState(currentState);

        if (IsComboState(prev) && !IsComboState(next))
        {
            pendingBlastFinisher = false;
            blastComboStep = -1;
            EndAttackLunge();
            EndComboAirHang();
            airComboBoostLatched = false;
            verticalComboDashLatched = false;
            SetBoostActive(false);
            if (next != AllyState.Recalling)
                SyncAirVisualAfterBusy();
        }
    }

    static bool IsComboState(AllyState state) =>
        state == AllyState.ComboAttacking
        || state == AllyState.ComboDashWindup
        || state == AllyState.ComboDashing;

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
            case AllyState.Recalling:
                SetDashActive(false);
                SetBoostActive(false);
                if (anim != null)
                {
                    anim.SetBool(walkBoolName, false);
                    anim.ResetTrigger(attackTriggerName);
                    anim.ResetTrigger(comboAttackTriggerName);
                    anim.ResetTrigger(dashAttackTriggerName);
                    anim.ResetTrigger(blastAttackTriggerName);
                    anim.ResetTrigger(dashStartTriggerName);
                    anim.ResetTrigger(pullTriggerName);
                    anim.Play(recallStateName, 0, 0f);
                    anim.Update(0f);
                }
                StopMoving();
                if (rb != null)
                {
                    rb.linearVelocity = Vector2.zero;
                    rb.bodyType = RigidbodyType2D.Kinematic;
                }
                recallAnimSeen = false;
                break;
            case AllyState.Idle:
                SetDashActive(false);
                if (!IsAirborneBusy && anim != null)
                    anim.SetBool(walkBoolName, false);
                break;
            case AllyState.Chase:
                SetDashActive(false);
                if (!IsAirborneBusy && anim != null)
                    anim.SetBool(walkBoolName, true);
                break;
            case AllyState.Attack:
                SetDashActive(false);
                if (anim != null)
                    anim.SetBool(walkBoolName, false);
                StopMoving();
                break;
            case AllyState.Return:
                SetDashActive(false);
                if (!IsAirborneBusy && anim != null)
                    anim.SetBool(walkBoolName, true);
                currentTarget = null;
                break;
            case AllyState.Pulling:
                SetDashActive(false);
                if (anim != null)
                    anim.SetBool(walkBoolName, false);
                StopMoving();
                break;
            case AllyState.ComboAttacking:
                SetDashActive(false);
                if (anim != null)
                    anim.SetBool(walkBoolName, false);
                StopMoving();
                comboAttackAnimSeen = false;
                TryBeginComboAirHang();
                break;
            case AllyState.ComboDashWindup:
                SetDashActive(true);
                if (anim != null)
                    anim.SetBool(walkBoolName, false);
                StopMoving();
                comboDashWindupAnimSeen = false;
                break;
            case AllyState.ComboDashing:
                SetDashActive(true);
                if (anim != null)
                    anim.SetBool(walkBoolName, false);
                dashTimer = dashTimeout;
                break;
            case AllyState.ManualMove:
                SetDashActive(false);
                StopMoving();
                if (anim != null)
                    anim.SetBool(walkBoolName, false);
                break;
        }
    }

    float GetDistXTo(Transform target)
    {
        if (target == null) return float.MaxValue;
        return Mathf.Abs(transform.position.x - GetCombatAimPoint(target).x);
    }

    float GetDistYTo(Transform target)
    {
        if (target == null) return float.MaxValue;
        return Mathf.Abs(transform.position.y - GetCombatAimPoint(target).y);
    }

    /// <summary>
    /// 空中敌优先用碰撞体中心作瞄准点，避免根节点偏低导致冲刺停在脚下。
    /// </summary>
    Vector2 GetCombatAimPoint(Transform target)
    {
        if (target == null)
            return Vector2.zero;

        Collider2D col = target.GetComponent<Collider2D>();
        if (col == null)
            col = target.GetComponentInChildren<Collider2D>();
        if (col != null)
            return col.bounds.center;

        return target.position;
    }

    bool IsAirEnemyTarget(Transform target)
    {
        return target != null && target.CompareTag(AirEnemyTag);
    }

    /// <summary>
    /// 与空中敌相同：|ΔY| 超出到位阈值时不可普通追击/近战，仅连携（及显式 allow）可响应。
    /// </summary>
    bool IsBeyondGroundChaseHeight(Transform target)
    {
        return target != null && GetDistYTo(target) > airAttackDistanceY;
    }

    /// <summary>
    /// 可写入 currentTarget 并进入追击/近战的目标（排除空中敌与过高 Y 差）。
    /// </summary>
    bool CanPersistAsChaseTarget(Transform target)
    {
        return target != null
            && !IsAirEnemyTarget(target)
            && !IsBeyondGroundChaseHeight(target);
    }

    /// <summary>
    /// 空中敌、地面敌 Y 差距超出到位阈值、或水平路径上有悬崖时，连携/Blast 冲锋走二维飞冲。
    /// 冲锋期间锁存，避免条件刚消失后切回水平冲锋导致空中下落抖动。
    /// </summary>
    bool NeedsVerticalComboDash(Transform target)
    {
        if (target == null)
            return false;

        if (IsAirEnemyTarget(target))
        {
            // 冲锋期间锁存，目标中途死亡后仍可据此清掉竖直速度
            if (currentState == AllyState.ComboDashWindup || currentState == AllyState.ComboDashing)
                verticalComboDashLatched = true;
            return true;
        }

        if (verticalComboDashLatched)
            return true;

        bool needsFly = IsBeyondGroundChaseHeight(target) || HasCliffGapToTarget(target);
        if (!needsFly)
            return false;

        if (currentState != AllyState.ComboDashWindup && currentState != AllyState.ComboDashing)
            return false;

        verticalComboDashLatched = true;
        return true;
    }

    /// <summary>
    /// 目标在水平方向、但中间有悬崖/缺口，贴地冲锋会停在边缘。
    /// </summary>
    bool HasCliffGapToTarget(Transform target)
    {
        if (target == null || physicsCheck == null)
            return false;

        Vector2 aim = GetCombatAimPoint(target);
        float dx = aim.x - transform.position.x;
        if (Mathf.Abs(dx) < 0.15f)
            return false;

        float dir = Mathf.Sign(dx);
        if (IsLedgeBlocking(dir))
            return true;

        Bounds bounds = bodyCollider != null
            ? bodyCollider.bounds
            : new Bounds(transform.position, Vector3.one * 0.5f);
        float fromX = dir > 0f ? bounds.max.x : bounds.min.x;
        if (Mathf.Sign(aim.x - fromX) != dir)
            return false;

        return physicsCheck.HasCliffGapAlongX(fromX, aim.x, bounds.min.y);
    }

    /// <summary>
    /// 地面敌与 AirEnemy 均需：X 进入 attackDistance，且 Y 进入 airAttackDistanceY。
    /// </summary>
    bool IsInAttackRange(Transform target)
    {
        if (target == null)
            return false;

        return GetDistXTo(target) <= attackDistance && GetDistYTo(target) <= airAttackDistanceY;
    }

    /// <summary>
    /// 地面敌与 AirEnemy 均需：X 进入 dashDecideDistance，且 Y 进入 airAttackDistanceY。
    /// </summary>
    bool IsWithinDashDecideRange(Transform target)
    {
        if (target == null)
            return false;

        return GetDistXTo(target) <= dashDecideDistance && GetDistYTo(target) <= airAttackDistanceY;
    }

    bool IsValidCombatTarget(Transform target, bool allowAirEnemy = false)
    {
        if (target == null || !target.gameObject.activeInHierarchy)
            return false;

        // 空中敌 / Y 差距过大：默认仅连携期间有效；激光等可显式允许
        if ((IsAirEnemyTarget(target) || IsBeyondGroundChaseHeight(target))
            && !allowAirEnemy && !IsBusyWithCombo)
            return false;

        if (!IsAllowedByActiveEncounter(GetCombatAimPoint(target)))
            return false;

        Enemy enemy = target.GetComponent<Enemy>();
        return enemy == null || enemy.IsHittable;
    }

    /// <summary>
    /// 锁区未解开时只允许锁定 EncounterBounds 内的敌人；UnlockLock / 遭遇结束后不限制。
    /// </summary>
    static bool IsAllowedByActiveEncounter(Vector2 worldPoint)
    {
        return EncounterZone.IsAllyTargetingAllowed(worldPoint);
    }

    bool TryAcquireTarget(out Transform target, bool includeAirEnemy = false)
    {
        target = FindClosestEnemy(includeAirEnemy);
        return target != null;
    }

    bool TryAcquireBlastComboTarget(out Transform target)
    {
        target = FindClosestEnemy(
            includeAirEnemy: true,
            rangeX: blastComboDetectRangeX,
            rangeY: blastComboDetectRangeY);
        return target != null;
    }

    /// <summary>
    /// 每段 Blast 开始时用专用索敌范围重锁目标并转向；找不到则保留上一有效目标。
    /// </summary>
    void RetargetBlastComboStep()
    {
        if (TryAcquireBlastComboTarget(out Transform target))
        {
            currentTarget = target;
            FaceTarget(currentTarget.position);
            return;
        }

        if (IsValidCombatTarget(currentTarget, allowAirEnemy: true))
            FaceTarget(currentTarget.position);
    }

    void SetDashActive(bool active)
    {
        if (anim != null)
            anim.SetBool(dashActiveBoolName, active);
    }

    void ExitComboDash()
    {
        StopDashSfx();
        EndAttackLunge();
        StopMoving();
        SetDashActive(false);
        pendingBlastFinisher = false;
        blastComboStep = -1;
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
            // 正常结束时 FinishPullSession 已清 pullInProgress；此处只处理被其它状态强行切走
            if (pullInProgress && !pullOverlayMode)
            {
                pullVisual?.Cancel();
                pullInProgress = false;
                pullOverlayMode = false;
                if (playerHooked)
                    EndPull();
                ClearPullHookState();
            }
            if (anim != null)
                anim.Play("Idle", 0, 0f);
        }
    }

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
            return;
        }

        anim.Play("Idle", 0, 0f);
        SwitchState(AllyState.Idle);
    }

    void UpdateRecalling()
    {
        if (anim == null)
        {
            Destroy(gameObject);
            return;
        }

        var info = anim.GetCurrentAnimatorStateInfo(0);
        if (info.IsName(recallStateName))
        {
            recallAnimSeen = true;
            if (info.normalizedTime < 1f)
                return;

            Destroy(gameObject);
            return;
        }

        if (recallAnimSeen)
        {
            Destroy(gameObject);
            return;
        }

        anim.Play(recallStateName, 0, 0f);
        anim.Update(0f);
    }

    void UpdateIdle()
    {
        if (isLanding)
            return;

        if (pendingRetarget)
        {
            PerformRetarget();
            return;
        }

        if (TryAcquireTarget(out Transform target))
        {
            BeginCombat(target);
            return;
        }

        if (deployMode != RobotDeployMode.Follow)
            return;

        Vector3 home = HomeAnchor;
        float distX = Mathf.Abs(transform.position.x - home.x);
        if (distX > followArriveDistance)
        {
            FaceTarget(home);
            idleFollowing = true;
            idleFollowDir = Mathf.Sign(home.x - transform.position.x);
            if (anim != null && airPhase == RobotAirPhase.Ground)
                anim.SetBool(walkBoolName, true);
        }
        else if (anim != null && airPhase == RobotAirPhase.Ground)
        {
            anim.SetBool(walkBoolName, false);
            if (owner != null)
                FaceTarget(owner.position);
        }
    }

    void UpdateChase()
    {
        if (isLanding)
            return;

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
        if (isLanding || IsAirborneBusy)
            return;

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

        if (!IsInAttackRange(currentTarget))
        {
            SwitchState(AllyState.Chase);
            return;
        }

        if (!attackLungeActive)
            StopMoving();

        if (attackTimer <= 0f)
        {
            FaceTarget(currentTarget.position);
            anim.SetTrigger(attackTriggerName);
            attackTimer = attackCooldown;
        }
    }

    void UpdateReturn()
    {
        if (isLanding)
            return;

        if (TryAcquireTarget(out Transform target))
        {
            BeginCombat(target);
            return;
        }

        Vector3 home = HomeAnchor;
        FaceTarget(home);

        float distToHome = Mathf.Abs(transform.position.x - home.x);
        if (distToHome <= arriveThreshold)
        {
            transform.position = new Vector3(home.x, transform.position.y, transform.position.z);
            SwitchState(AllyState.Idle);
        }
    }

    void UpdateComboAttacking()
    {
        if (!attackLungeActive)
            StopMoving();

        if (anim == null)
        {
            EndAttackLunge();
            blastComboStep = -1;
            pendingBlastFinisher = false;
            ResumeStateAfterCombo();
            return;
        }

        var info = anim.GetCurrentAnimatorStateInfo(0);
        bool inBlastFinisher = IsCurrentBlastAttackState(info);
        bool inFinisher = info.IsName(comboAttackStateName)
            || info.IsName(dashAttackStateName)
            || inBlastFinisher;
        if (inFinisher)
            comboAttackAnimSeen = true;

        if (!comboAttackAnimSeen || (inFinisher && info.normalizedTime < 1f))
            return;

        EndAttackLunge();

        if (blastComboStep >= 0)
        {
            if (blastComboStep + 1 < BlastComboHitCount)
                TryAdvanceBlastCombo();
            else
                FinishBlastCombo();
            return;
        }

        ResumeStateAfterCombo();
    }

    void UpdateComboDashWindup()
    {
        if (!attackLungeActive)
            StopMoving();

        if (!IsValidCombatTarget(currentTarget))
        {
            if (!TryAcquireTarget(out currentTarget, includeAirEnemy: true))
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
            if (!TryAcquireTarget(out currentTarget, includeAirEnemy: true))
            {
                ExitComboDash();
                return;
            }
        }

        if (IsInAttackRange(currentTarget))
        {
            BeginDashAttack();
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
            if (!TryAcquireTarget(out currentTarget, includeAirEnemy: true))
            {
                ExitComboDash();
                return;
            }
        }

        FaceTarget(currentTarget.position);

        if (IsInAttackRange(currentTarget))
        {
            StopMoving();
            BeginDashAttack();
        }
    }

    void ResumeStateAfterCombo() => ResumeStateAfterPull();

    bool TryGetChaseMoveDir(out float dir)
    {
        dir = 0f;
        if (!IsValidCombatTarget(currentTarget))
            return false;

        if (IsInAttackRange(currentTarget))
            return false;

        dir = Mathf.Sign(currentTarget.position.x - transform.position.x);
        return !Mathf.Approximately(dir, 0f);
    }

    bool TryGetHomeMoveDir(float threshold, out float dir)
    {
        dir = 0f;
        Vector3 home = HomeAnchor;
        float distX = Mathf.Abs(transform.position.x - home.x);
        if (distX <= threshold)
            return false;

        dir = Mathf.Sign(home.x - transform.position.x);
        return !Mathf.Approximately(dir, 0f);
    }

    /// <summary>
    /// 贴地 AI 移动时前方是否为悬崖；空中与单向平台上不拦截，平台上可走下去。
    /// 可越过的矮障不算悬崖，否则会先停步，自动跳永远得不到 movingHorizontally。
    /// </summary>
    bool IsLedgeBlocking(float dir)
    {
        if (physicsCheck == null || airPhase != RobotAirPhase.Ground)
            return false;
        if (Mathf.Approximately(dir, 0f) || !physicsCheck.ShouldRespectLedge)
            return false;
        if (physicsCheck.HasGroundAhead(dir))
            return false;

        return !CanJumpOverObstacle(dir);
    }

    /// <returns>true 表示已施加水平速度；遇边缘停步时返回 false。</returns>
    bool ApplyHorizontalMove(float dir, float speed, bool respectLedge = true)
    {
        if (Mathf.Approximately(dir, 0f))
        {
            StopMoving();
            return false;
        }

        if (respectLedge && IsLedgeBlocking(dir))
        {
            StopMoving();
            return false;
        }

        if (IsOnWalkableSlope)
        {
            Vector2 tangent = new Vector2(-physicsCheck.groundNormal.y, physicsCheck.groundNormal.x).normalized;
            if (Mathf.Sign(tangent.x) != Mathf.Sign(dir))
                tangent = -tangent;
            rb.linearVelocity = tangent * speed;
            return true;
        }

        rb.linearVelocity = new Vector2(speed * dir, rb.linearVelocity.y);
        return true;
    }

    /// <summary>
    /// 连携/Blast 竖直冲锋：空中敌、Y 差距过大或中间有悬崖时，按二维方向飞向碰撞体中心；已在攻击距离则停住。
    /// </summary>
    bool TryApplyVerticalComboDash()
    {
        if (!NeedsVerticalComboDash(currentTarget) || !IsValidCombatTarget(currentTarget))
            return false;

        if (IsInAttackRange(currentTarget))
        {
            rb.linearVelocity = Vector2.zero;
            return true;
        }

        Vector2 aim = GetCombatAimPoint(currentTarget);
        Vector2 toTarget = aim - rb.position;
        if (toTarget.sqrMagnitude < 0.0001f)
        {
            rb.linearVelocity = Vector2.zero;
            return true;
        }

        rb.linearVelocity = toTarget.normalized * dashSpeed;
        FaceTarget(aim);
        return true;
    }

    void StopMoving()
    {
        if (rb == null)
            return;

        // 竖直/二维连携冲刺停步时清掉竖直速度，避免冲刺惯性残留。
        // 用 verticalComboDashLatched：目标已死导致 currentTarget 为空时 NeedsVerticalComboDash 会失败。
        if (verticalComboDashLatched
            || (currentState == AllyState.ComboDashing && NeedsVerticalComboDash(currentTarget)))
            rb.linearVelocity = Vector2.zero;
        else if (IsOnWalkableSlope)
            rb.linearVelocity = Vector2.zero;
        else
            rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
    }

    bool IsOnWalkableSlope =>
        slopeDetachTimer <= 0f
        && !comboAirHanging
        && !verticalComboDashLatched
        && physicsCheck != null
        && physicsCheck.isGround
        && physicsCheck.isOnSlope;

    bool IsSlopePhysicsSuppressed =>
        slopeDetachTimer > 0f
        || comboAirHanging
        || verticalComboDashLatched;

    void UpdateSlopeGravity()
    {
        if (rb == null)
            return;

        if (IsSlopePhysicsSuppressed)
        {
            if (!comboAirHanging)
                rb.gravityScale = normalGravityScale;
            return;
        }

        if (physicsCheck != null && physicsCheck.isGround && physicsCheck.isOnSlope)
            rb.gravityScale = 0f;
        else
            rb.gravityScale = normalGravityScale;
    }

    void CancelVelocityIntoSlope()
    {
        if (rb == null || !IsOnWalkableSlope)
            return;

        if (rb.linearVelocity.y > 0.05f)
            return;

        Vector2 normal = physicsCheck.groundNormal;
        Vector2 velocity = rb.linearVelocity;
        float normalSpeed = Vector2.Dot(velocity, normal);
        rb.linearVelocity = velocity - normal * normalSpeed;
    }

    void BeginSlopeDetach(float duration = 0.25f)
    {
        slopeDetachTimer = Mathf.Max(slopeDetachTimer, duration);
        if (rb != null)
            rb.gravityScale = normalGravityScale;
    }

    void OnCollisionStay2D(Collision2D collision)
    {
        if (!IsOnWalkableSlope || physicsCheck == null || bodyCollider == null)
            return;

        if (((1 << collision.gameObject.layer) & physicsCheck.groundLayer) == 0)
            return;

        if (rb.linearVelocity.y > 0.05f)
            return;

        foreach (ContactPoint2D contact in collision.contacts)
        {
            if (contact.normal.y <= 0.5f)
                continue;

            var pathSlope = collision.collider.GetComponent<SlopePathSegment>()
                ?? collision.collider.GetComponentInParent<SlopePathSegment>();
            if (pathSlope != null)
            {
                Vector2 feetPos = new Vector2(bodyCollider.bounds.center.x, bodyCollider.bounds.min.y);
                if (!pathSlope.IsFeetAboveSurface(feetPos))
                    continue;
                MaintainSlopeContact(contact.normal);
                return;
            }

            var slope = collision.collider.GetComponent<SlopeOneWayPlatform>();
            if (slope == null)
                continue;

            Vector2 legacyFeet = new Vector2(bodyCollider.bounds.center.x, bodyCollider.bounds.min.y);
            if (!slope.IsFeetAboveSurface(legacyFeet))
                continue;

            MaintainSlopeContact(contact.normal);
            return;
        }
    }

    void MaintainSlopeContact(Vector2 groundNormal)
    {
        if (!IsOnWalkableSlope || rb.linearVelocity.y > 0.05f)
            return;

        rb.gravityScale = 0f;

        Vector2 vel = rb.linearVelocity;
        if (vel.sqrMagnitude < 0.0001f)
        {
            rb.linearVelocity = Vector2.zero;
            return;
        }

        Vector2 tangent = new Vector2(-groundNormal.y, groundNormal.x).normalized;
        if (Mathf.Sign(tangent.x) != Mathf.Sign(vel.x) && !Mathf.Approximately(vel.x, 0f))
            tangent = -tangent;

        float speed = vel.magnitude;
        rb.linearVelocity = tangent * speed;
    }

    /// <summary>
    /// 空中进入连携攻击时开启短暂低重力滞空。
    /// </summary>
    void TryBeginComboAirHang()
    {
        if (rb == null || IsSolidGrounded())
            return;

        comboAirHanging = true;
        comboAirHangTimer = Mathf.Max(0.05f, comboAirHangDuration);
        ApplyComboAirHangPhysics();
    }

    void UpdateComboAirHang()
    {
        if (!comboAirHanging)
            return;

        if (currentState != AllyState.ComboAttacking || IsSolidGrounded())
        {
            EndComboAirHang();
            return;
        }

        comboAirHangTimer -= Time.fixedDeltaTime;
        ApplyComboAirHangPhysics();

        if (comboAirHangTimer <= 0f)
            EndComboAirHang();
    }

    void ApplyComboAirHangPhysics()
    {
        if (rb == null)
            return;

        rb.gravityScale = normalGravityScale * comboAirHangGravityScale;
        if (rb.linearVelocity.y < 0f)
            rb.linearVelocity = new Vector2(rb.linearVelocity.x, 0f);
    }

    void EndComboAirHang()
    {
        comboAirHangTimer = 0f;
        if (!comboAirHanging)
            return;

        comboAirHanging = false;
        if (rb != null)
            rb.gravityScale = normalGravityScale;
    }

    /// <summary>
    /// 连携 Boost：空中敌整段连携锁存；地面目标仅在竖直/越崖冲锋（Windup/Dashing）期间显示。
    /// </summary>
    void UpdateComboBoostVisual()
    {
        if (IsBusyWithCombo)
        {
            if (currentTarget != null && IsAirEnemyTarget(currentTarget))
                airComboBoostLatched = true;
        }
        else
        {
            airComboBoostLatched = false;
        }

        bool groundVerticalDashBoost =
            !airComboBoostLatched
            && currentTarget != null
            && NeedsVerticalComboDash(currentTarget)
            && (currentState == AllyState.ComboDashWindup || currentState == AllyState.ComboDashing);

        SetBoostActive(airComboBoostLatched || groundVerticalDashBoost);
    }

    void SetBoostActive(bool active)
    {
        if (boostVisual == null)
            return;
        if (boostVisual.activeSelf == active)
            return;
        boostVisual.SetActive(active);
    }

    bool IsGrounded()
    {
        return physicsCheck != null && physicsCheck.isGround;
    }

    bool IsSolidGrounded()
    {
        return physicsCheck != null && physicsCheck.isSolidGround;
    }

    void UpdateAirAndLanding()
    {
        if (isLanding)
        {
            UpdateLanding();
            return;
        }

        bool grounded = IsGrounded();
        bool solidGrounded = IsSolidGrounded();
        float velocityY = rb != null ? rb.linearVelocity.y : 0f;

        if (!airStateInitialized)
        {
            wasGrounded = grounded;
            airStateInitialized = true;
            return;
        }

        switch (airPhase)
        {
            case RobotAirPhase.Ground:
                // 用带土狼跳的 isGround；斜面 coyote / 向上速度不误判坠落
                if (wasGrounded && !grounded)
                {
                    if (velocityY > DescendVelocityThreshold)
                        break;

                    if (physicsCheck != null && physicsCheck.WasOnSlopeRecently && velocityY > -MicroAirHitchMaxFallSpeed)
                        break;

                    leftGround = true;
                    airTimer = 0f;
                    if (SuppressAirAnim)
                        airPhase = RobotAirPhase.Fall;
                    else
                        SetAirPhase(RobotAirPhase.Fall, forcePlay: true);
                }
                break;

            case RobotAirPhase.Jump:
                airTimer += Time.deltaTime;
                if (!solidGrounded)
                    leftGround = true;

                if (velocityY <= DescendVelocityThreshold)
                {
                    if (SuppressAirAnim)
                        airPhase = RobotAirPhase.Fall;
                    else
                        SetAirPhase(RobotAirPhase.Fall, forcePlay: true);
                }

                if (leftGround && solidGrounded && airTimer >= MinAirTime && velocityY <= 0.05f)
                    FinishAirborneLanding(velocityY);
                break;

            case RobotAirPhase.Fall:
                airTimer += Time.deltaTime;
                if (!solidGrounded)
                    leftGround = true;

                if (leftGround && solidGrounded && airTimer >= MinAirTime && velocityY <= 0.05f)
                    FinishAirborneLanding(velocityY);
                break;
        }

        wasGrounded = grounded;
    }

    void FinishAirborneLanding(float velocityY)
    {
        if (ShouldSkipLandLock(velocityY))
            RecoverGroundWithoutLandAnim();
        else
            BeginLanding();
    }

    bool ShouldSkipLandLock(float velocityY)
    {
        if (SuppressAirAnim)
            return true;

        // 走路微离地、斜面过渡、短时失联：不要播 Land 锁停
        if (airTimer < MicroAirHitchMaxTime && velocityY > -MicroAirHitchMaxFallSpeed)
            return true;

        if (physicsCheck != null && physicsCheck.WasOnSlopeRecently && velocityY > -MicroAirHitchMaxFallSpeed)
            return true;

        return false;
    }

    /// <summary>
    /// 冲刺/牵引等忙碌态落地：只恢复逻辑接地，不播 Land、不加 isLanding 锁停。
    /// </summary>
    void RecoverGroundWithoutLandAnim()
    {
        leftGround = false;
        airTimer = 0f;
        isLanding = false;
        SetAirPhase(RobotAirPhase.Ground, forcePlay: false);

        if (anim == null || SuppressAirAnim)
            return;

        bool walking = currentState == AllyState.Chase
            || currentState == AllyState.Return
            || idleFollowing
            || (currentState == AllyState.ManualMove
                && !pendingStationOnLand
                && Mathf.Abs(manualMoveInput.x) > ManualMoveInputThreshold);
        anim.SetBool(walkBoolName, walking);
        anim.Play(walking ? "Walk" : "Idle", 0, 0f);
    }

    /// <summary>
    /// Combo 结束后若仍在空中，补播 Fall；已落地则静默回到 Ground。
    /// </summary>
    void SyncAirVisualAfterBusy()
    {
        if (IsSolidGrounded())
        {
            leftGround = false;
            airTimer = 0f;
            isLanding = false;
            airPhase = RobotAirPhase.Ground;
            if (anim != null)
                anim.SetInteger(airPhaseParamName, (int)RobotAirPhase.Ground);
            return;
        }

        leftGround = true;
        airTimer = 0f;
        SetAirPhase(RobotAirPhase.Fall, forcePlay: true);
    }

    void UpdateLanding()
    {
        StopMoving();
        landTimer -= Time.deltaTime;

        bool landAnimDone = true;
        if (anim != null)
        {
            var info = anim.GetCurrentAnimatorStateInfo(0);
            if (info.IsName(landStateName))
                landAnimDone = info.normalizedTime >= 1f;
        }

        if (landTimer > 0f || !landAnimDone)
            return;

        isLanding = false;
        SetAirPhase(RobotAirPhase.Ground, forcePlay: false);
        if (anim != null)
        {
            bool walking = currentState == AllyState.Chase
                || currentState == AllyState.Return
                || idleFollowing
                || (currentState == AllyState.ManualMove
                    && !pendingStationOnLand
                    && Mathf.Abs(manualMoveInput.x) > ManualMoveInputThreshold);
            anim.SetBool(walkBoolName, walking);
            if (!walking)
                anim.Play("Idle", 0, 0f);
            else
                anim.Play("Walk", 0, 0f);
        }

        if (pendingStationOnLand)
        {
            CommitStationedAtCurrentPosition();
            return;
        }

        if (pendingRetarget)
            PerformRetarget();
    }

    void BeginLanding()
    {
        isLanding = true;
        landTimer = landDuration;
        leftGround = false;
        airTimer = 0f;
        StopMoving();
        SetAirPhase(RobotAirPhase.Ground, forcePlay: false);
        if (anim != null)
        {
            anim.SetBool(walkBoolName, false);
            anim.Play(landStateName, 0, 0f);
            anim.Update(0f);
        }
    }

    void SetAirPhase(RobotAirPhase phase, bool forcePlay)
    {
        airPhase = phase;
        if (anim != null)
            anim.SetInteger(airPhaseParamName, (int)phase);

        if (!forcePlay || anim == null)
            return;

        string state = phase switch
        {
            RobotAirPhase.Jump => jumpStateName,
            RobotAirPhase.Fall => fallStateName,
            _ => null
        };

        if (!string.IsNullOrEmpty(state))
        {
            anim.SetBool(walkBoolName, false);
            anim.Play(state, 0, 0f);
            anim.Update(0f);
        }
    }

    void TryAutoJump(float moveDir)
    {
        if (IsAirborneBusy || jumpCooldownTimer > 0f)
            return;

        if (currentState == AllyState.Spawning
            || currentState == AllyState.Recalling
            || IsPulling
            || currentState == AllyState.Attack
            || currentState == AllyState.ManualMove
            || (IsBusyWithCombo && currentState != AllyState.ComboDashing))
            return;

        // 只用真实接地，避免土狼窗口内贴墙连跳。
        // 不看 isOnSlope：上台阶时脚底/棱角法线常被误判成斜面，反而把该跳的矮障拦掉。
        // 真斜坡立面由 CanJumpOverObstacle 的法线与斜坡组件过滤。
        if (!IsSolidGrounded() || Mathf.Approximately(moveDir, 0f))
            return;

        bool obstacleAhead = CanJumpOverObstacle(moveDir);
        if (suppressAutoJumpUntilObstacleClears)
        {
            if (obstacleAhead)
                return;
            suppressAutoJumpUntilObstacleClears = false;
        }

        if (!obstacleAhead)
            return;

        PerformJump(moveDir);
    }

    void CancelVelocityIntoWall(float moveDir)
    {
        if (physicsCheck == null || Mathf.Approximately(moveDir, 0f))
            return;

        if (physicsCheck.isOnSlope || physicsCheck.WasOnSlopeRecently)
            return;

        if (!physicsCheck.IsBlockedHorizontally(moveDir))
            return;

        Vector2 vel = rb.linearVelocity;
        if (moveDir > 0f && vel.x > 0f)
            vel.x = 0f;
        else if (moveDir < 0f && vel.x < 0f)
            vel.x = 0f;
        rb.linearVelocity = vel;
    }

    bool CanJumpOverObstacle(float moveDir)
    {
        LayerMask mask = jumpObstacleMask.value != 0
            ? jumpObstacleMask
            : (physicsCheck != null ? physicsCheck.groundLayer : (LayerMask)0);
        if (mask.value == 0)
            return false;

        float face = Mathf.Sign(moveDir);
        if (Mathf.Approximately(face, 0f))
            return false;

        float footY = GetFootY();
        // 探测高度必须高于脚底，否则水平射线会打进地面而不是墙面。
        float probeHeight = Mathf.Max(0.15f, jumpProbeHeight);
        float bodyFrontX = bodyCollider != null
            ? (face > 0f ? bodyCollider.bounds.max.x : bodyCollider.bounds.min.x)
            : transform.position.x;
        Vector2 probeOrigin = new Vector2(
            bodyFrontX + face * jumpProbeForwardPadding,
            footY + probeHeight);

        if (!TryFindWallHit(probeOrigin, face, jumpProbeDistance, mask, out RaycastHit2D wallHit))
        {
            // 主探针偏高时，0.25~0.45 的矮障会从下方漏掉
            float lowHeight = Mathf.Max(0.15f, minObstacleHeight + 0.02f);
            if (lowHeight >= probeHeight - 0.01f)
                return false;

            Vector2 lowOrigin = new Vector2(probeOrigin.x, footY + lowHeight);
            if (!TryFindWallHit(lowOrigin, face, jumpProbeDistance, mask, out wallHit))
                return false;
        }

        float topProbeX = wallHit.point.x + face * 0.08f;
        float topStartY = footY + Mathf.Max(maxAutoJumpHeight, probeHeight) + 0.5f;
        float castDist = topStartY - footY + 0.2f;

        RaycastHit2D[] topHits = Physics2D.RaycastAll(
            new Vector2(topProbeX, topStartY),
            Vector2.down,
            castDist,
            mask);

        float clearHeight = -1f;
        for (int i = 0; i < topHits.Length; i++)
        {
            RaycastHit2D topHit = topHits[i];
            if (!IsExternalObstacle(topHit.collider))
                continue;

            clearHeight = topHit.point.y - footY;
            break;
        }

        if (clearHeight < minObstacleHeight || clearHeight > maxAutoJumpHeight)
            return false;

        float headY = bodyCollider != null
            ? bodyCollider.bounds.max.y
            : transform.position.y + 1f;
        float ceilingCheckDist = Mathf.Max(0.1f, jumpCeilingClearance);
        RaycastHit2D ceilingHit = Physics2D.Raycast(
            new Vector2(transform.position.x, headY),
            Vector2.up,
            ceilingCheckDist,
            mask);
        if (ceilingHit.collider != null && IsExternalObstacle(ceilingHit.collider))
            return false;

        return true;
    }

    bool TryFindWallHit(
        Vector2 origin,
        float face,
        float distance,
        LayerMask mask,
        out RaycastHit2D wallHit)
    {
        wallHit = default;
        RaycastHit2D[] hits = Physics2D.RaycastAll(origin, Vector2.right * face, distance, mask);
        for (int i = 0; i < hits.Length; i++)
        {
            RaycastHit2D hit = hits[i];
            if (!IsExternalObstacle(hit.collider) || IsDedicatedSlopeSurface(hit.collider))
                continue;

            // 地面/斜坡法线偏上，不应当作需要跳过的墙。
            if (hit.normal.y > 0.55f)
                continue;

            wallHit = hit;
            return true;
        }

        return false;
    }

    bool IsExternalObstacle(Collider2D col)
    {
        if (col == null)
            return false;
        Transform hitTf = col.transform;
        return hitTf != transform && !hitTf.IsChildOf(transform);
    }

    bool IsDedicatedSlopeSurface(Collider2D col)
    {
        if (col == null)
            return false;

        return col.GetComponent<SlopePathSegment>() != null
            || col.GetComponentInParent<SlopePathSegment>() != null
            || col.GetComponent<SlopeOneWayPlatform>() != null;
    }

    float GetFootY()
    {
        if (bodyCollider != null)
            return bodyCollider.bounds.min.y;
        return transform.position.y;
    }

    void PerformJump(float moveDir)
    {
        // 斜坡站立时 gravityScale 可能为 0，必须用正常重力反算初速度
        float gravity = Mathf.Abs(Physics2D.gravity.y * normalGravityScale);
        if (gravity < 0.01f)
            gravity = Mathf.Abs(Physics2D.gravity.y);

        float jumpVelocity = Mathf.Sqrt(2f * gravity * Mathf.Max(0.01f, jumpHeight));
        float speed = currentState == AllyState.ComboDashing ? dashSpeed : moveSpeed;

        if (physicsCheck != null && physicsCheck.isOnSlope)
        {
            BeginSlopeDetach(0.25f);
            rb.position += physicsCheck.groundNormal * 0.06f;
        }
        else
        {
            rb.gravityScale = normalGravityScale;
        }

        rb.linearVelocity = new Vector2(speed * Mathf.Sign(moveDir), jumpVelocity);

        jumpCooldownTimer = jumpCooldown;
        leftGround = false;
        airTimer = 0f;
        isLanding = false;
        // 跳完后只要前方仍有同一障碍，就不再自动跳，防止贴墙连跳。
        suppressAutoJumpUntilObstacleClears = true;
        if (SuppressAirAnim)
            airPhase = RobotAirPhase.Jump;
        else
            SetAirPhase(RobotAirPhase.Jump, forcePlay: true);
    }

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

    const string AirEnemyTag = "AirEnemy";

    Transform FindClosestEnemy(bool includeAirEnemy = false, float rangeX = -1f, float rangeY = -1f)
    {
        Transform closestMarked = null;
        Transform closestUnmarked = null;
        float minMarkedDistY = float.MaxValue;
        float minMarkedDistX = float.MaxValue;
        float minUnmarkedDistY = float.MaxValue;
        float minUnmarkedDistX = float.MaxValue;

        float maxX = rangeX > 0f ? rangeX : detectRangeX;
        float comboMaxY = rangeY > 0f ? rangeY : detectRangeY;
        // 普通索敌：Y 不得超过到位阈值（过高只走连携）；连携/激光放宽到 detectRangeY
        float groundMaxY = includeAirEnemy ? comboMaxY : airAttackDistanceY;
        ConsiderEnemiesWithTag(
            "Enemy",
            maxX,
            groundMaxY,
            ref closestMarked, ref closestUnmarked,
            ref minMarkedDistY, ref minMarkedDistX,
            ref minUnmarkedDistY, ref minUnmarkedDistX);
        if (includeAirEnemy)
        {
            ConsiderEnemiesWithTag(
                AirEnemyTag,
                maxX,
                comboMaxY,
                ref closestMarked, ref closestUnmarked,
                ref minMarkedDistY, ref minMarkedDistX,
                ref minUnmarkedDistY, ref minUnmarkedDistX);
        }

        return closestMarked != null ? closestMarked : closestUnmarked;
    }

    /// <summary>
    /// 同标记层级内：更小 |ΔY| 优先；Y 相等时更小 |ΔX| 优先。
    /// </summary>
    static bool IsBetterAcquireCandidate(float distY, float distX, float bestY, float bestX)
    {
        if (distY < bestY)
            return true;
        if (distY > bestY)
            return false;
        return distX < bestX;
    }

    void ConsiderEnemiesWithTag(
        string tag,
        float maxDistX,
        float maxDistY,
        ref Transform closestMarked,
        ref Transform closestUnmarked,
        ref float minMarkedDistY,
        ref float minMarkedDistX,
        ref float minUnmarkedDistY,
        ref float minUnmarkedDistX)
    {
        GameObject[] enemies = GameObject.FindGameObjectsWithTag(tag);
        foreach (var e in enemies)
        {
            if (e == null || !e.activeInHierarchy)
                continue;

            Enemy enemy = e.GetComponent<Enemy>();
            if (enemy != null && !enemy.IsHittable)
                continue;

            Vector2 aim = GetCombatAimPoint(e.transform);
            float distX = Mathf.Abs(transform.position.x - aim.x);
            float distY = Mathf.Abs(transform.position.y - aim.y);
            if (distX > maxDistX || distY > maxDistY)
                continue;

            if (!IsAllowedByActiveEncounter(aim))
                continue;

            bool marked = enemy != null && enemy.isMarked;
            if (marked)
            {
                if (IsBetterAcquireCandidate(distY, distX, minMarkedDistY, minMarkedDistX))
                {
                    minMarkedDistY = distY;
                    minMarkedDistX = distX;
                    closestMarked = e.transform;
                }
            }
            else if (IsBetterAcquireCandidate(distY, distX, minUnmarkedDistY, minUnmarkedDistX))
            {
                minUnmarkedDistY = distY;
                minUnmarkedDistX = distX;
                closestUnmarked = e.transform;
            }
        }
    }

    bool IsOutsideMaxChaseRange()
    {
        return Vector2.Distance(transform.position, HomeAnchor) > maxChaseRange;
    }

    void DrawDetectRangeGizmo(Vector3 center)
    {
        DrawRangeRectGizmo(center, detectRangeX, detectRangeY);
    }

    void DrawRangeRectGizmo(Vector3 center, float hx, float hy)
    {
        Vector3 bl = center + new Vector3(-hx, -hy, 0f);
        Vector3 br = center + new Vector3( hx, -hy, 0f);
        Vector3 tr = center + new Vector3( hx,  hy, 0f);
        Vector3 tl = center + new Vector3(-hx,  hy, 0f);
        Gizmos.DrawLine(bl, br);
        Gizmos.DrawLine(br, tr);
        Gizmos.DrawLine(tr, tl);
        Gizmos.DrawLine(tl, bl);
    }

    void OnDrawGizmosSelected()
    {
        Vector3 origin = Application.isPlaying ? HomeAnchor : transform.position;

        Gizmos.color = Color.yellow;
        DrawDetectRangeGizmo(transform.position);

        Gizmos.color = new Color(0.6f, 0.2f, 1f, 1f);
        DrawRangeRectGizmo(transform.position, blastComboDetectRangeX, blastComboDetectRangeY);

        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, attackDistance);

        Gizmos.color = new Color(1f, 0.5f, 0f, 1f);
        Gizmos.DrawWireSphere(transform.position, dashDecideDistance);

        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(origin, maxChaseRange);

        Gizmos.color = Color.green;
        Gizmos.DrawLine(origin + Vector3.left * 0.3f, origin + Vector3.right * 0.3f);
        Gizmos.DrawLine(origin + Vector3.up * 0.3f, origin + Vector3.down * 0.3f);

        Gizmos.color = Color.magenta;
        float footY = Application.isPlaying ? GetFootY() : transform.position.y;
        float probeHeight = Mathf.Max(0.15f, jumpProbeHeight);
        float face = Mathf.Sign(transform.localScale.x);
        if (Mathf.Approximately(face, 0f)) face = 1f;
        float bodyFrontX = bodyCollider != null
            ? (face > 0f ? bodyCollider.bounds.max.x : bodyCollider.bounds.min.x)
            : transform.position.x;
        Vector3 probe = new Vector3(
            bodyFrontX + face * jumpProbeForwardPadding,
            footY + probeHeight,
            0f);
        Gizmos.DrawLine(probe, probe + Vector3.right * face * jumpProbeDistance);
    }
}
