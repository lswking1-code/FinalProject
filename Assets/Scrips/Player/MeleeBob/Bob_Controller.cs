using System.Collections.Generic;
using FMODUnity;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Melee_Player（Bob）专属能力管理。不改动其他玩家脚本，仅在本组件内扩展能力。
/// FixedUpdate 晚于默认 PlayerMovement，以便 Rush 冲刺速度不被水平移动覆盖。
/// </summary>
[DefaultExecutionOrder(100)]
[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(PhysicsCheck))]
public class Bob_Controller : MonoBehaviour
{
    [System.Serializable]
    public struct WeaponMeleeProfile
    {
        public int weaponId;
        public int damage;
        [Tooltip("前方攻击 Hitbox 本地尺寸")]
        public Vector2 hitboxSize;
        [Tooltip("前方攻击 Hitbox 本地偏移（面向右为正 X）")]
        public Vector2 hitboxOffset;
        [Tooltip("向上攻击（upattack / jump_upattack）Hitbox 尺寸；为 0 则用默认上方盒")]
        public Vector2 upHitboxSize;
        [Tooltip("向上攻击 Hitbox 偏移（正 Y 为上方）")]
        public Vector2 upHitboxOffset;
        [Tooltip("索敌区尺寸")]
        public Vector2 detectSize;
        [Tooltip("索敌区偏移")]
        public Vector2 detectOffset;
        [Tooltip("0 = 不限制命中数；>0 为单次挥击最多命中敌人数")]
        public int maxTargets;
        [Header("命中窗（归一化 0-1，对齐 *_hit 精灵）")]
        [Tooltip("站立普通攻击")]
        [Range(0f, 1f)] public float hitStart;
        [Range(0f, 1f)] public float hitEnd;
        [Tooltip("空中普通攻击；结束≤开始则回退站立窗")]
        [Range(0f, 1f)] public float airHitStart;
        [Range(0f, 1f)] public float airHitEnd;
        [Tooltip("站立上攻；结束≤开始则回退站立窗")]
        [Range(0f, 1f)] public float upHitStart;
        [Range(0f, 1f)] public float upHitEnd;
        [Tooltip("空中上攻；结束≤开始则回退上攻窗")]
        [Range(0f, 1f)] public float airUpHitStart;
        [Range(0f, 1f)] public float airUpHitEnd;
        [Tooltip("蹲攻（四武器共用 default_down_melee）；结束≤开始则回退站立窗")]
        [Range(0f, 1f)] public float crouchHitStart;
        [Range(0f, 1f)] public float crouchHitEnd;
    }

    [System.Serializable]
    public struct WeaponAmmoCost
    {
        public int weaponId;
        [Tooltip("普通攻击消耗（含上攻/空中/蹲攻/下砸）；0 不消耗。空手忽略")]
        public int meleeCost;
        [Tooltip("特技消耗；0 不消耗")]
        public int specialCost;
    }

    [System.Serializable]
    public struct WeaponSpecialProfile
    {
        public int weaponId;
        public int damage;
        [Tooltip("特技 Hitbox 本地尺寸（Rush / Whip 前方盒）")]
        public Vector2 hitboxSize;
        [Tooltip("特技 Hitbox / Buzzsaw 圆心本地偏移（面向右为正 X；Buzzsaw 双圆圆心也用此值）")]
        public Vector2 hitboxOffset;
        [Tooltip("0 = 不限制命中数；>0 为单次特技最多命中敌人数")]
        public int maxTargets;
        [Range(0f, 1f)] public float hitStart;
        [Range(0f, 1f)] public float hitEnd;

        [Header("Whip · 后方追加判定")]
        [Tooltip("后方追加判定开始（归一化 0-1）。whip_special 后挥 hit 约在 0.67")]
        [Range(0f, 1f)] public float rearHitStart;
        [Tooltip("后方追加判定结束（归一化 0-1）")]
        [Range(0f, 1f)] public float rearHitEnd;

        [Header("Buzzsaw · 双层圆形判定")]
        [Tooltip("外圈半径（低伤害，使用 innerDamage）")]
        public float outerRadius;
        [Tooltip("内圈半径（高伤害，使用 damage）")]
        public float innerRadius;
        [Tooltip("外圈伤害（应低于 damage；内圈用 damage）")]
        public int innerDamage;
        // 圆心相对 MeleePoint 的偏移：复用上方 hitboxOffset（正 X 为面向前方）

        [Header("Rush · 向前冲刺 + 推动")]
        [Tooltip("冲刺水平速度（单位/秒）")]
        public float rushSpeed;
        [Tooltip("冲刺开始（归一化动画时间）")]
        [Range(0f, 1f)] public float rushStart;
        [Tooltip("冲刺结束（归一化动画时间）")]
        [Range(0f, 1f)] public float rushEnd;
        [Tooltip("路径推动速度；≤0 时回退为 rushSpeed。每物理帧沿面向推动命中盒内敌人")]
        public float rushPushSpeed;
        [Tooltip("命中击退水平速度（单位/秒）；Whip/Rush 都会用来盖过敌人 AI 速度）")]
        public float knockbackForce;
        [Tooltip("击退持续时间（秒）")]
        public float knockbackDuration;
    }

    [Header("二段跳")]
    [Tooltip("二段跳目标高度；若勾选下方选项则改用 PlayerMovement.jumpHeight")]
    [SerializeField] float doubleJumpHeight = 4.5f;
    [SerializeField] bool usePlayerJumpHeight = true;
    [Tooltip("摇杆死区，与 PlayerMovement 默认一致")]
    [SerializeField] float inputThreshold = 0.5f;

    [Header("普通攻击（J / Attack）")]
    [SerializeField] int meleeDamage = 40;
    [Tooltip("仅作无效命中窗的回退值；实际判定走分武器分动作窗口")]
    [SerializeField] float hitStart = 0.62f;
    [SerializeField] float hitEnd = 0.88f;
    [SerializeField] Transform meleePoint1;
    [SerializeField] Transform meleePoint2;
    [SerializeField] GameObject meleeHitbox;
    [SerializeField] MeleeDetectZone detectZone;

    [Header("分武器攻击（0 照旧 / 1 中距 / 2 细长 / 3 短距最多 2 目标）")]
    [SerializeField] WeaponMeleeProfile[] weaponProfiles =
    {
        new WeaponMeleeProfile
        {
            weaponId = 0, damage = 40,
            hitboxSize = new Vector2(1.45f, 1.05f), hitboxOffset = new Vector2(0.25f, 0f),
            upHitboxSize = new Vector2(1.3f, 1.8f), upHitboxOffset = new Vector2(0f, 1.2f),
            detectSize = new Vector2(2f, 2f), detectOffset = new Vector2(0.5f, 0f),
            maxTargets = 0,
            hitStart = 0.62f, hitEnd = 0.88f,
            airHitStart = 0.35f, airHitEnd = 0.65f,
            upHitStart = 0.62f, upHitEnd = 0.88f,
            airUpHitStart = 0.35f, airUpHitEnd = 0.65f,
            crouchHitStart = 0.66f, crouchHitEnd = 0.90f,
        },
        new WeaponMeleeProfile
        {
            weaponId = 1, damage = 55,
            hitboxSize = new Vector2(2.6f, 1.8f), hitboxOffset = new Vector2(1.2f, 0.15f),
            upHitboxSize = new Vector2(1.5f, 2.6f), upHitboxOffset = new Vector2(0f, 1.5f),
            detectSize = new Vector2(3.2f, 2.2f), detectOffset = new Vector2(1.4f, 0.2f),
            maxTargets = 0,
            hitStart = 0.45f, hitEnd = 0.72f,
            airHitStart = 0.28f, airHitEnd = 0.55f,
            upHitStart = 0.28f, upHitEnd = 0.55f,
            airUpHitStart = 0.45f, airUpHitEnd = 0.72f,
            crouchHitStart = 0.66f, crouchHitEnd = 0.90f,
        },
        new WeaponMeleeProfile
        {
            weaponId = 2, damage = 45,
            hitboxSize = new Vector2(4.0f, 0.85f), hitboxOffset = new Vector2(2.0f, 0.05f),
            upHitboxSize = new Vector2(0.55f, 3.6f), upHitboxOffset = new Vector2(0f, 2.0f),
            detectSize = new Vector2(4.4f, 1.4f), detectOffset = new Vector2(2.1f, 0.1f),
            maxTargets = 0,
            hitStart = 0.45f, hitEnd = 0.72f,
            airHitStart = 0.40f, airHitEnd = 0.98f,
            upHitStart = 0.62f, upHitEnd = 0.88f,
            airUpHitStart = 0.32f, airUpHitEnd = 0.55f,
            crouchHitStart = 0.66f, crouchHitEnd = 0.90f,
        },
        new WeaponMeleeProfile
        {
            weaponId = 3, damage = 70,
            hitboxSize = new Vector2(1.55f, 1.35f), hitboxOffset = new Vector2(0.7f, -0.1f),
            upHitboxSize = new Vector2(1.3f, 1.6f), upHitboxOffset = new Vector2(0f, 1.1f),
            detectSize = new Vector2(2.0f, 1.8f), detectOffset = new Vector2(0.8f, 0f),
            maxTargets = 2,
            hitStart = 0.58f, hitEnd = 0.80f,
            airHitStart = 0.22f, airHitEnd = 0.42f,
            upHitStart = 0.52f, upHitEnd = 0.76f,
            airUpHitStart = 0.28f, airUpHitEnd = 0.55f,
            crouchHitStart = 0.66f, crouchHitEnd = 0.90f,
        },
    };

    [Header("弹药消耗（1=BulletS / 2=BulletM / 3=BulletL；空手不耗）")]
    [SerializeField] WeaponAmmoCost[] weaponAmmoCosts =
    {
        new WeaponAmmoCost { weaponId = 1, meleeCost = 1, specialCost = 10 },
        new WeaponAmmoCost { weaponId = 2, meleeCost = 1, specialCost = 10 },
        new WeaponAmmoCost { weaponId = 3, meleeCost = 1, specialCost = 10 },
    };

    [Header("特技（U / Ability1 · 仅武器 1/2/3）")]
    [SerializeField] WeaponSpecialProfile[] specialProfiles =
    {
        new WeaponSpecialProfile
        {
            weaponId = 1, damage = 80,
            hitboxSize = new Vector2(3.2f, 1.4f), hitboxOffset = new Vector2(1.6f, 0f),
            maxTargets = 0, hitStart = 0.18f, hitEnd = 0.82f,
            rushSpeed = 18f, rushStart = 0.18f, rushEnd = 0.55f,
            rushPushSpeed = 20f,
            knockbackForce = 14f, knockbackDuration = 0.22f,
        },
        new WeaponSpecialProfile
        {
            weaponId = 2, damage = 70,
            hitboxSize = new Vector2(4.5f, 0.6f), hitboxOffset = new Vector2(2.2f, 0f),
            maxTargets = 0, hitStart = 0.18f, hitEnd = 0.40f,
            rearHitStart = 0.60f, rearHitEnd = 0.85f,
            knockbackForce = 16f, knockbackDuration = 0.28f,
        },
        new WeaponSpecialProfile
        {
            weaponId = 3, damage = 100, innerDamage = 55,
            hitboxSize = new Vector2(2.0f, 1.6f), hitboxOffset = Vector2.zero,
            maxTargets = 0, hitStart = 0.18f, hitEnd = 0.62f,
            outerRadius = 3.2f, innerRadius = 1.5f,
        },
    };

    [Header("大招（I / Ability2 · 复制特技，消耗能量）")]
    [Tooltip("发动大招消耗的 AbilityPower；0 表示不消耗。Melee_Player 默认上限 100")]
    [SerializeField] float ultimateAbilityPowerCost = 50f;
    [Tooltip("大招伤害 = 特技伤害 × 该倍率")]
    [SerializeField] float ultimateDamageMultiplier = 2f;

    [Header("Buzzsaw · 破盾")]
    [Tooltip("圆锯刀打到持盾敌人盾牌时的伤害倍率；≤0 时按 2 倍。不影响绕盾打到本体")]
    [SerializeField] float buzzsawShieldDamageMultiplier = 2f;

    [Header("Rush 大招 · 后段击飞")]
    [Tooltip("击飞判定开始（归一化）。rush_special 后段 hit_c 约在 0.70")]
    [Range(0f, 1f)] [SerializeField] float rushUltimateLaunchStart = 0.62f;
    [Tooltip("击飞判定结束（归一化）")]
    [Range(0f, 1f)] [SerializeField] float rushUltimateLaunchEnd = 0.85f;
    [SerializeField] Vector2 rushUltimateLaunchHitboxSize = new Vector2(2.2f, 2.8f);
    [SerializeField] Vector2 rushUltimateLaunchHitboxOffset = new Vector2(0.9f, 1.35f);
    [Tooltip("击飞段伤害；≤0 则与大招冲刺段相同（已含大招倍率）")]
    [SerializeField] int rushUltimateLaunchDamage = 0;
    [Tooltip("击飞垂直速度")]
    [SerializeField] float rushUltimateLaunchSpeedY = 13f;
    [Tooltip("击飞水平速度（沿面朝，可把敌人略带向前）")]
    [SerializeField] float rushUltimateLaunchSpeedX = 2.5f;
    [Tooltip("击飞后压住敌人水平速度的时长，避免 AI 立刻把人拉回地面走位")]
    [SerializeField] float rushUltimateLaunchHoldDuration = 0.45f;

    [Header("Whip 大招 · 四向挥击（对齐 whip_ult：上 / 前 / 下 / 后）")]
    [Tooltip("上方判定开始。whip_ult 的 whip_upattack_hit 约在 0.11")]
    [Range(0f, 1f)] [SerializeField] float whipUltimateUpHitStart = 0.08f;
    [Range(0f, 1f)] [SerializeField] float whipUltimateUpHitEnd = 0.24f;
    [SerializeField] Vector2 whipUltimateUpHitboxSize = new Vector2(1.8f, 6.2f);
    [SerializeField] Vector2 whipUltimateUpHitboxOffset = new Vector2(-0.67f, 3.02f);
    [Tooltip("前方判定开始。whip_ult 的 whip_attack_hit 约在 0.33；盒复用特技前方盒")]
    [Range(0f, 1f)] [SerializeField] float whipUltimateFrontHitStart = 0.28f;
    [Range(0f, 1f)] [SerializeField] float whipUltimateFrontHitEnd = 0.46f;
    [Tooltip("下方判定开始。whip_ult 的 whip_downattack_hit 约在 0.56")]
    [Range(0f, 1f)] [SerializeField] float whipUltimateDownHitStart = 0.50f;
    [Range(0f, 1f)] [SerializeField] float whipUltimateDownHitEnd = 0.68f;
    [SerializeField] Vector2 whipUltimateDownHitboxSize = new Vector2(2.8f, 5.8f);
    [SerializeField] Vector2 whipUltimateDownHitboxOffset = new Vector2(-0.9f, -1.9f);
    [Tooltip("后方判定开始。whip_ult 的 whip_upattack_hit_b 约在 0.78；盒为特技前方盒镜像")]
    [Range(0f, 1f)] [SerializeField] float whipUltimateRearHitStart = 0.72f;
    [Range(0f, 1f)] [SerializeField] float whipUltimateRearHitEnd = 0.92f;

    [Header("近战连段（普攻 ↔ 上攻）")]
    [Tooltip("空手 / Rush：仅地面。普攻或上攻动画结束前再按攻击，衔接另一段")]
    [SerializeField] bool rushAttackComboEnabled = true;
    [Tooltip("连段缓冲最早可登记的归一化时间（避免与起手同一帧误触）")]
    [Range(0f, 0.5f)] [SerializeField] float rushComboBufferEarliest = 0.05f;

    [Header("短距冲刺（CrouchMelee · 无推怪）")]
    [Tooltip("蹲攻短距冲刺速度；应明显短于 rush_special")]
    [SerializeField] float shortMeleeDashSpeed = 10f;
    [Range(0f, 1f)] [SerializeField] float shortMeleeDashStart = 0.08f;
    [Range(0f, 1f)] [SerializeField] float shortMeleeDashEnd = 0.38f;
    [Tooltip("蹲下滑铲周身判定尺寸（相对 MeleePoint2）。四武器共用动画，不跟站立前方盒")]
    [SerializeField] Vector2 crouchMeleeHitboxSize = new Vector2(3f, 1.5f);
    [Tooltip("蹲下滑铲周身判定偏移。MeleePoint2 已在身前，负 X 把盒拉回角色中心")]
    [SerializeField] Vector2 crouchMeleeHitboxOffset = new Vector2(-1f, -0.25f);

    [Header("JumpDownAttack · 高速落地砸地")]
    [Tooltip("空中下砸下落速度")]
    [SerializeField] float jumpDownSlamSpeed = 28f;
    [Tooltip("落地冲击伤害")]
    [SerializeField] int jumpDownImpactDamage = 70;
    [Tooltip("落地冲击圆半径（相对角色 pivot）")]
    [SerializeField] float jumpDownImpactRadius = 2.5f;
    [Tooltip("冲击中心相对角色 pivot 的偏移")]
    [SerializeField] Vector2 jumpDownImpactOffset = new Vector2(0f, 0.4f);

    [Header("音效")]
    [SerializeField] EventReference attackEvent;
    [Tooltip("FMOD Attack 事件上的标签参数名")]
    [SerializeField] string meleeTypeParam = "MeleeType";
    [SerializeField] EventReference jumpEvent;
    [SerializeField] EventReference doubleJumpEvent;
    [SerializeField] EventReference switchWeaponEvent;
    [SerializeField] EventReference dieEvent;

    [Header("向上攻击默认判定（剖面 upHitbox 未填时回退）")]
    [SerializeField] Vector2 defaultUpHitboxSize = new Vector2(1.3f, 1.8f);
    [SerializeField] Vector2 defaultUpHitboxOffset = new Vector2(0f, 1.2f);

    [Header("攻击范围可视化（Scene / Prefab 预览）")]
    [Tooltip("Scene 与 Prefab 编辑模式始终显示判定框，无需选中")]
    [SerializeField] bool showAttackRangesInScene = true;
    [Tooltip("编辑器中叠加显示全部武器的普攻/上攻/蹲攻/特技/下砸范围（方便 Prefab 调试）")]
    [SerializeField] bool showAllAttackHitboxes = true;
    [Tooltip("在框旁显示文字标签（仅编辑器）")]
    [SerializeField] bool showHitboxLabels = true;
    [SerializeField] Color detectZoneGizmoColor = new Color(1f, 0.85f, 0.2f, 0.25f);
    [SerializeField] Color hitboxIdleGizmoColor = new Color(0.2f, 0.85f, 1f, 0.2f);
    [SerializeField] Color hitboxActiveGizmoColor = new Color(1f, 0.2f, 0.2f, 0.45f);

    [Header("Debug")]
    [Tooltip("在 Console 打印发动的技能名称与造成的伤害")]
    [SerializeField] bool debugLogSkillDamage = true;

    Rigidbody2D rb;
    PhysicsCheck physicsCheck;
    PlayerMovement playerMovement;
    PlayerAnimBase playerAnim;
    PlayerFullBodyAnim fullBodyAnim;
    PlayerWeaponController weaponController;
    InputSystem_Actions actions;
    Attack meleeAttack;
    BoxCollider2D meleeHitboxCollider;
    BoxCollider2D detectZoneCollider;
    Character selfCharacter;
    PlayerRoll playerRoll;
    CameraAirborneYLock airborneYLock;

    bool jumpPressedThisFrame;
    bool hasUsedDoubleJump;

    int activeWeaponId = -1;
    WeaponMeleeProfile activeProfile;
    WeaponSpecialProfile activeSpecialProfile;
    bool hasSpecialProfile;
    readonly HashSet<Character> swingHitTargets = new();
    readonly HashSet<Character> specialRearHitTargets = new();
    readonly HashSet<Character> whipUltUpHitTargets = new();
    readonly HashSet<Character> whipUltDownHitTargets = new();
    readonly HashSet<Character> rushCarriedTargets = new();
    readonly List<Collider2D> rushIgnoredEnemyColliders = new();
    readonly HashSet<IHitCountable> swingHitCountables = new();
    readonly List<Character> overlapCharacters = new();
    readonly Collider2D[] overlapBuffer = new Collider2D[48];

    const int BuzzsawMeleeHitTicks = 3;
    const int BuzzsawSpecialHitTicks = 5;
    int buzzsawActiveHitTick = -1;

    Collider2D playerBodyCollider;
    bool rushDashActive;
    bool savedAttackKnockbackEnable;
    float savedAttackKnockbackForce;
    float savedAttackKnockbackDuration;
    bool hasSavedAttackKnockback;
    bool holdingAttackInputLock;
    bool restoredMovementEnabled;
    bool restoredRollEnabled;
    bool restoredWeaponControllerEnabled;
    bool jumpDownAttackActive;
    bool jumpDownImpactApplied;
    bool wasDeadForSfx;
    bool wasSwitchingWeaponForSfx;
    bool hasPendingRushCombo;
    bool pendingRushComboUpward;
    readonly List<WhipKnockbackEntry> whipKnockbackEntries = new();

    struct WhipKnockbackEntry
    {
        public Rigidbody2D rb;
        public Transform targetTransform;
        public float dir;
        public float speed;
        public float untilTime;
    }

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        physicsCheck = GetComponent<PhysicsCheck>();
        playerMovement = GetComponent<PlayerMovement>();
        playerAnim = PlayerAnimBase.Resolve(gameObject);
        fullBodyAnim = playerAnim as PlayerFullBodyAnim;
        weaponController = GetComponent<PlayerWeaponController>();
        selfCharacter = GetComponent<Character>();
        playerRoll = GetComponent<PlayerRoll>();
        actions = new InputSystem_Actions();
        playerBodyCollider = GetComponent<CapsuleCollider2D>();
        if (playerBodyCollider == null)
            playerBodyCollider = GetComponent<Collider2D>();

        if (meleeHitbox != null)
        {
            meleeHitboxCollider = meleeHitbox.GetComponent<BoxCollider2D>();
            meleeAttack = meleeHitbox.GetComponent<Attack>();
            if (meleeAttack != null)
            {
                meleeAttack.attackType = AttackType.Melee;
                meleeAttack.ignoreTag = "Player";
            }

            meleeHitbox.SetActive(false);
        }

        if (detectZone != null)
            detectZoneCollider = detectZone.GetComponent<BoxCollider2D>();

        activeProfile = BuildFallbackProfile(0);
    }

    void Start() => RefreshWeaponProfile(force: true);

    void OnEnable()
    {
        actions.Player.Enable();
        if (meleeAttack != null)
            meleeAttack.CharacterDamaged += OnMeleeAttackDamaged;
    }

    void OnDisable()
    {
        if (meleeAttack != null)
            meleeAttack.CharacterDamaged -= OnMeleeAttackDamaged;

        EndJumpDownAttack();
        EndAttackInputLock();
        EndRushSpecialState();
        actions.Player.Disable();
    }

    void OnDestroy()
    {
        if (meleeAttack != null)
            meleeAttack.CharacterDamaged -= OnMeleeAttackDamaged;
        actions?.Dispose();
    }

    void Update()
    {
        RefreshWeaponProfile(force: false);

        // 须在近战 Complete 之前登记缓冲，否则最后一帧按键会丢
        TryBufferRushComboInput();

        // 攻击锁期间关掉了 PlayerMovement，需自行推进空中/近战完成检测
        if (holdingAttackInputLock)
            MaintainAttackLockAnimation();

        if (actions.Player.Jump.WasPressedThisFrame())
            jumpPressedThisFrame = true;

        TryStartMeleeAttack();
        TryStartSpecialAttack();
        TryStartUltimateAttack();
        UpdateMeleeHitbox();
        TryConsumePendingRushCombo();
        UpdateCommonActionSfx();
    }

    void LateUpdate()
    {
        SyncDetectZoneAnchor();
        // 放在 LateUpdate：盖过敌人 FixedUpdate 里写回的 AI 速度 / 物理挤出
        ApplyWhipKnockbackVelocities();
        MaintainRushCarriedTargets();
    }

    void FixedUpdate()
    {
        UpdateJumpDownAttack();
        UpdateDashAttacks();
        ApplyWhipKnockbackVelocities();

        // 一段跳由 PlayerMovement 执行；此处只补音效与记录
        if (playerMovement != null && playerMovement.DidGroundJumpThisFixedUpdate)
        {
            PlaySfx(jumpEvent);
            PlaySessionRecorder.Instance?.RecordMeleeJump();
        }

        if (physicsCheck.isGround)
        {
            hasUsedDoubleJump = false;
            jumpPressedThisFrame = false;
            return;
        }

        // 土狼窗口内仍走地面跳；同帧刚起跳也不要立刻消耗二段跳
        if (playerMovement != null
            && (playerMovement.CanGroundJump || playerMovement.DidGroundJumpThisFixedUpdate))
        {
            jumpPressedThisFrame = false;
            return;
        }

        // 下穿单向平台时这次跳跃已被 PlayerMovement 消费，不能再转成二段跳
        if (playerMovement != null && playerMovement.IsDroppingThrough)
        {
            jumpPressedThisFrame = false;
            return;
        }

        if (jumpPressedThisFrame && !hasUsedDoubleJump)
            TryDoubleJump();

        jumpPressedThisFrame = false;
    }

    int ResolveCurrentWeaponId()
    {
        if (weaponController != null)
            return weaponController.CurrentWeaponId;
        if (fullBodyAnim != null)
            return fullBodyAnim.AppliedWeaponId;
        return 0;
    }

    void RefreshWeaponProfile(bool force)
    {
        int weaponId = ResolveCurrentWeaponId();
        if (!force && weaponId == activeWeaponId)
            return;

        activeWeaponId = weaponId;
        activeProfile = FindProfile(weaponId);
        hasSpecialProfile = TryFindSpecialProfile(weaponId, out activeSpecialProfile);
        ApplyActiveProfileToColliders();
    }

    WeaponMeleeProfile FindProfile(int weaponId)
    {
        if (weaponProfiles != null)
        {
            for (int i = 0; i < weaponProfiles.Length; i++)
            {
                if (weaponProfiles[i].weaponId == weaponId)
                    return NormalizeProfile(weaponProfiles[i], weaponId);
            }
        }

        return BuildFallbackProfile(weaponId);
    }

    WeaponMeleeProfile NormalizeProfile(WeaponMeleeProfile profile, int weaponId)
    {
        profile.weaponId = weaponId;

        // weapon 0：伤害继续跟上方「普通攻击」字段；命中窗走分动作字段，不再被全局 hitStart/hitEnd 覆盖
        if (weaponId == 0)
            profile.damage = meleeDamage;
        else if (profile.damage <= 0)
            profile.damage = meleeDamage;

        if (profile.hitboxSize.x <= 0.01f || profile.hitboxSize.y <= 0.01f)
            profile.hitboxSize = new Vector2(1.2f, 1f);
        if (profile.upHitboxSize.x <= 0.01f || profile.upHitboxSize.y <= 0.01f)
            profile.upHitboxSize = defaultUpHitboxSize;
        if (Mathf.Approximately(profile.upHitboxOffset.x, 0f)
            && Mathf.Approximately(profile.upHitboxOffset.y, 0f))
            profile.upHitboxOffset = defaultUpHitboxOffset;
        if (profile.detectSize.x <= 0.01f || profile.detectSize.y <= 0.01f)
            profile.detectSize = new Vector2(2f, 2f);
        if (profile.hitEnd <= profile.hitStart)
        {
            profile.hitStart = hitStart;
            profile.hitEnd = hitEnd;
        }

        FillInvalidHitWindow(ref profile.airHitStart, ref profile.airHitEnd, profile.hitStart, profile.hitEnd);
        FillInvalidHitWindow(ref profile.upHitStart, ref profile.upHitEnd, profile.hitStart, profile.hitEnd);
        FillInvalidHitWindow(ref profile.airUpHitStart, ref profile.airUpHitEnd, profile.upHitStart, profile.upHitEnd);
        FillInvalidHitWindow(ref profile.crouchHitStart, ref profile.crouchHitEnd, 0.66f, 0.90f);

        return profile;
    }

    static void FillInvalidHitWindow(ref float start, ref float end, float fallbackStart, float fallbackEnd)
    {
        if (end > start)
            return;
        start = fallbackStart;
        end = fallbackEnd;
    }

    bool TryFindSpecialProfile(int weaponId, out WeaponSpecialProfile profile)
    {
        if (specialProfiles != null)
        {
            for (int i = 0; i < specialProfiles.Length; i++)
            {
                if (specialProfiles[i].weaponId != weaponId)
                    continue;

                profile = NormalizeSpecialProfile(specialProfiles[i], weaponId);
                return weaponId != 0;
            }
        }

        profile = default;
        return false;
    }

    WeaponSpecialProfile NormalizeSpecialProfile(WeaponSpecialProfile profile, int weaponId)
    {
        profile.weaponId = weaponId;
        if (profile.damage <= 0)
            profile.damage = Mathf.Max(meleeDamage, 1);
        if (profile.hitboxSize.x <= 0.01f || profile.hitboxSize.y <= 0.01f)
            profile.hitboxSize = new Vector2(2f, 1.2f);
        if (profile.hitEnd <= profile.hitStart)
        {
            profile.hitStart = 0.2f;
            profile.hitEnd = 0.7f;
        }

        if (weaponId == 2)
        {
            bool rearWindowInvalid = profile.rearHitEnd <= profile.rearHitStart;
            // 旧值 0.5–0.7 按片长秒数填写，归一化后几乎错过后挥（约 0.67）
            bool rearWindowTooEarly = profile.rearHitStart <= 0.51f && profile.rearHitEnd <= 0.75f;
            if (rearWindowInvalid || rearWindowTooEarly)
            {
                profile.rearHitStart = 0.60f;
                profile.rearHitEnd = 0.85f;
            }
            if (profile.knockbackForce <= 0.01f)
                profile.knockbackForce = 16f;
            if (profile.knockbackDuration <= 0.01f)
                profile.knockbackDuration = 0.28f;
        }
        else if (weaponId == 3)
        {
            if (profile.outerRadius <= 0.01f)
                profile.outerRadius = 3.2f;
            if (profile.innerRadius <= 0.01f)
                profile.innerRadius = profile.outerRadius * 0.45f;
            if (profile.innerRadius >= profile.outerRadius)
                profile.innerRadius = profile.outerRadius * 0.45f;
            if (profile.innerDamage <= 0)
                profile.innerDamage = Mathf.Max(1, profile.damage / 2);
        }
        else if (weaponId == 1)
        {
            if (profile.rushSpeed <= 0.01f)
                profile.rushSpeed = 18f;
            if (profile.rushPushSpeed <= 0.01f)
                profile.rushPushSpeed = profile.rushSpeed;
            if (profile.rushEnd <= profile.rushStart)
            {
                profile.rushStart = 0.18f;
                profile.rushEnd = 0.55f;
            }
            if (profile.knockbackForce <= 0.01f)
                profile.knockbackForce = 14f;
            if (profile.knockbackDuration <= 0.01f)
                profile.knockbackDuration = 0.22f;
        }

        return profile;
    }

    WeaponMeleeProfile BuildFallbackProfile(int weaponId)
        => NormalizeProfile(FindProfileInDefaults(weaponId), weaponId);

    static WeaponMeleeProfile FindProfileInDefaults(int weaponId)
    {
        switch (weaponId)
        {
            case 1:
                return new WeaponMeleeProfile
                {
                    weaponId = 1, damage = 55,
                    hitboxSize = new Vector2(2.6f, 1.8f), hitboxOffset = new Vector2(1.2f, 0.15f),
                    upHitboxSize = new Vector2(1.5f, 2.6f), upHitboxOffset = new Vector2(0f, 1.5f),
                    detectSize = new Vector2(3.2f, 2.2f), detectOffset = new Vector2(1.4f, 0.2f),
                    maxTargets = 0,
                    hitStart = 0.45f, hitEnd = 0.72f,
                    airHitStart = 0.28f, airHitEnd = 0.55f,
                    upHitStart = 0.28f, upHitEnd = 0.55f,
                    airUpHitStart = 0.45f, airUpHitEnd = 0.72f,
                    crouchHitStart = 0.66f, crouchHitEnd = 0.90f,
                };
            case 2:
                return new WeaponMeleeProfile
                {
                    weaponId = 2, damage = 45,
                    hitboxSize = new Vector2(4.0f, 0.85f), hitboxOffset = new Vector2(2.0f, 0.05f),
                    upHitboxSize = new Vector2(0.55f, 3.6f), upHitboxOffset = new Vector2(0f, 2.0f),
                    detectSize = new Vector2(4.4f, 1.4f), detectOffset = new Vector2(2.1f, 0.1f),
                    maxTargets = 0,
                    hitStart = 0.45f, hitEnd = 0.72f,
                    airHitStart = 0.40f, airHitEnd = 0.98f,
                    upHitStart = 0.62f, upHitEnd = 0.88f,
                    airUpHitStart = 0.32f, airUpHitEnd = 0.55f,
                    crouchHitStart = 0.66f, crouchHitEnd = 0.90f,
                };
            case 3:
                return new WeaponMeleeProfile
                {
                    weaponId = 3, damage = 70,
                    hitboxSize = new Vector2(1.55f, 1.35f), hitboxOffset = new Vector2(0.7f, -0.1f),
                    upHitboxSize = new Vector2(1.3f, 1.6f), upHitboxOffset = new Vector2(0f, 1.1f),
                    detectSize = new Vector2(2.0f, 1.8f), detectOffset = new Vector2(0.8f, 0f),
                    maxTargets = 2,
                    hitStart = 0.58f, hitEnd = 0.80f,
                    airHitStart = 0.22f, airHitEnd = 0.42f,
                    upHitStart = 0.52f, upHitEnd = 0.76f,
                    airUpHitStart = 0.28f, airUpHitEnd = 0.55f,
                    crouchHitStart = 0.66f, crouchHitEnd = 0.90f,
                };
            default:
                return new WeaponMeleeProfile
                {
                    weaponId = 0, damage = 40,
                    hitboxSize = new Vector2(1.45f, 1.05f), hitboxOffset = new Vector2(0.25f, 0f),
                    upHitboxSize = new Vector2(1.3f, 1.8f), upHitboxOffset = new Vector2(0f, 1.2f),
                    detectSize = new Vector2(2f, 2f), detectOffset = new Vector2(0.5f, 0f),
                    maxTargets = 0,
                    hitStart = 0.62f, hitEnd = 0.88f,
                    airHitStart = 0.35f, airHitEnd = 0.65f,
                    upHitStart = 0.62f, upHitEnd = 0.88f,
                    airUpHitStart = 0.35f, airUpHitEnd = 0.65f,
                    crouchHitStart = 0.66f, crouchHitEnd = 0.90f,
                };
        }
    }

    void ApplyActiveProfileToColliders()
    {
        bool special = IsCurrentSwingSpecial();
        ApplyHitboxShape(upward: false, special: special);

        if (detectZoneCollider != null)
        {
            detectZoneCollider.offset = activeProfile.detectOffset;
            detectZoneCollider.size = activeProfile.detectSize;
        }

        if (meleeAttack != null)
        {
            int damage = special && hasSpecialProfile
                ? ResolveSpecialSwingDamage()
                : ResolveMeleeSwingDamage();
            int maxTargets = special && hasSpecialProfile
                ? activeSpecialProfile.maxTargets
                : activeProfile.maxTargets;

            // Whip / Buzzsaw / Rush 特技均手动结算；Rush 不用 Attack 击退冲量
            bool manualSpecial = special && hasSpecialProfile
                && (activeSpecialProfile.weaponId == 1
                    || activeSpecialProfile.weaponId == 2
                    || activeSpecialProfile.weaponId == 3);

            meleeAttack.damage = damage;
            meleeAttack.attackType = AttackType.Melee;
            meleeAttack.ignoreTag = "Player";
            meleeAttack.chargesEnergyNode = activeProfile.weaponId == 2;
            meleeAttack.enabled = !manualSpecial && maxTargets <= 0;
            SyncBuzzsawShieldMultiplier();
            SyncCancelEnemyProjectiles();

            if (special && hasSpecialProfile && activeSpecialProfile.weaponId == 2)
                ApplyRushAttackKnockback(true);
            else
                RestoreRushAttackKnockback();
        }
    }

    void SyncBuzzsawShieldMultiplier()
    {
        if (meleeAttack == null)
            return;

        bool buzzsaw = activeProfile.weaponId == 3 || ResolveCurrentWeaponId() == 3;
        meleeAttack.shieldDamageMultiplier = buzzsaw
            ? ResolveBuzzsawShieldMultiplier()
            : 1f;
    }

    void SyncCancelEnemyProjectiles()
    {
        if (meleeAttack == null)
            return;

        // 空手（weapon 0）不抵销；Rush / Whip / Buzzsaw 的普攻、特技、大招均可
        meleeAttack.cancelEnemyProjectiles = CanCancelEnemyProjectiles();
    }

    bool CanCancelEnemyProjectiles()
        => ResolveCurrentWeaponId() != 0;

    float ResolveBuzzsawShieldMultiplier()
        => buzzsawShieldDamageMultiplier > 0f ? buzzsawShieldDamageMultiplier : 2f;

    bool TryDealMeleeDamage(Character target, string note = null)
    {
        if (target == null || meleeAttack == null)
            return false;

        var shield = target.GetComponentInChildren<EnemyShieldAbsorb>();
        float shieldBefore = shield != null ? shield.currentShieldHealth : 0f;
        bool damaged = target.TakeDamage(meleeAttack);
        if (damaged)
        {
            meleeAttack.RaiseHitCameraShakeIfEnabled();
            LogSkillHit(target, meleeAttack.damage, note);
            return true;
        }

        if (shield != null && shield.currentShieldHealth < shieldBefore - 0.01f)
        {
            int dealt = Mathf.Max(1, Mathf.RoundToInt(shieldBefore - Mathf.Max(0f, shield.currentShieldHealth)));
            meleeAttack.RaiseHitCameraShakeIfEnabled();
            LogSkillHit(target, dealt, CombineHitNotes(note, "盾牌"));
        }

        return false;
    }

    void ApplyHitboxShape(bool upward, bool special = false)
    {
        if (meleeHitboxCollider == null)
            return;

        if (special && hasSpecialProfile)
        {
            meleeHitboxCollider.size = activeSpecialProfile.hitboxSize;
            meleeHitboxCollider.offset = activeSpecialProfile.hitboxOffset;
            return;
        }

        if (upward)
        {
            meleeHitboxCollider.size = activeProfile.upHitboxSize.x > 0.01f
                ? activeProfile.upHitboxSize
                : defaultUpHitboxSize;
            meleeHitboxCollider.offset = activeProfile.upHitboxSize.x > 0.01f
                ? activeProfile.upHitboxOffset
                : defaultUpHitboxOffset;
        }
        else if (IsCurrentSwingCrouchMelee())
        {
            ResolveCrouchMeleeHitbox(out Vector2 crouchSize, out Vector2 crouchOffset);
            meleeHitboxCollider.size = crouchSize;
            meleeHitboxCollider.offset = crouchOffset;
        }
        else
        {
            meleeHitboxCollider.size = activeProfile.hitboxSize;
            meleeHitboxCollider.offset = activeProfile.hitboxOffset;
        }
    }

    bool IsCurrentSwingUpward()
        => fullBodyAnim != null && fullBodyAnim.IsUpwardMelee;

    bool IsCurrentSwingJumpDownAttack()
        => fullBodyAnim != null && fullBodyAnim.IsJumpDownAttack;

    bool IsCurrentSwingCrouchMelee()
        => fullBodyAnim != null && fullBodyAnim.IsCrouchMelee;

    void ResolveCrouchMeleeHitbox(out Vector2 size, out Vector2 offset)
    {
        size = crouchMeleeHitboxSize.x > 0.01f && crouchMeleeHitboxSize.y > 0.01f
            ? crouchMeleeHitboxSize
            : new Vector2(3f, 1.5f);
        offset = crouchMeleeHitboxOffset;
    }

    void ResolveMeleeHitWindow(out float start, out float end)
    {
        if (IsCurrentSwingCrouchMelee())
        {
            start = activeProfile.crouchHitStart;
            end = activeProfile.crouchHitEnd;
            return;
        }

        if (IsCurrentSwingUpward())
        {
            bool air = physicsCheck != null && !physicsCheck.isGround;
            if (air)
            {
                start = activeProfile.airUpHitStart;
                end = activeProfile.airUpHitEnd;
            }
            else
            {
                start = activeProfile.upHitStart;
                end = activeProfile.upHitEnd;
            }
            return;
        }

        if (physicsCheck != null && !physicsCheck.isGround)
        {
            start = activeProfile.airHitStart;
            end = activeProfile.airHitEnd;
            return;
        }

        start = activeProfile.hitStart;
        end = activeProfile.hitEnd;
    }

    bool IsCurrentShortDashMelee()
        => IsCurrentSwingCrouchMelee();

    bool IsCurrentSwingSpecial()
        => playerAnim != null && playerAnim.IsSpecial;

    bool IsCurrentSwingUltimate()
        => fullBodyAnim != null && fullBodyAnim.IsUltimate;

    int ResolveSpecialSwingDamage()
    {
        int damage = Mathf.Max(1, activeSpecialProfile.damage);
        if (IsCurrentSwingUltimate())
            damage = Mathf.Max(1, Mathf.RoundToInt(damage * Mathf.Max(1f, ultimateDamageMultiplier)));
        return damage;
    }

    int ResolveSpecialInnerDamage()
    {
        int inner = activeSpecialProfile.innerDamage > 0
            ? activeSpecialProfile.innerDamage
            : Mathf.Max(1, activeSpecialProfile.damage / 2);
        if (IsCurrentSwingUltimate())
            inner = Mathf.Max(1, Mathf.RoundToInt(inner * Mathf.Max(1f, ultimateDamageMultiplier)));
        return inner;
    }

    void OnMeleeAttackDamaged(Character target, int damage)
        => LogSkillHit(target, damage);

    void LogSkillCast()
    {
        if (!debugLogSkillDamage)
            return;

        string skill = ResolveCurrentSkillName();
        int damage = ResolveExpectedSkillDamage();
        int ticks = ResolveBuzzsawHitTickCount();
        if (ticks > 1)
            Debug.Log($"[Bob] 发动 {skill}  伤害={damage}（{ticks}段）", this);
        else
            Debug.Log($"[Bob] 发动 {skill}  伤害={damage}", this);
    }

    void LogSkillHit(Character target, int damage, string note = null)
    {
        if (!debugLogSkillDamage || target == null)
            return;

        string skill = ResolveCurrentSkillName();
        string targetName = target.gameObject.name;
        if (!string.IsNullOrEmpty(note))
            Debug.Log($"[Bob] 命中 {targetName}  {skill}（{note}）  伤害={damage}", this);
        else
            Debug.Log($"[Bob] 命中 {targetName}  {skill}  伤害={damage}", this);
    }

    string ResolveWeaponLabel()
    {
        return ResolveCurrentWeaponId() switch
        {
            1 => "Rush",
            2 => "Whip",
            3 => "Buzzsaw",
            _ => "空手",
        };
    }

    string ResolveCurrentSkillName()
    {
        string weapon = ResolveWeaponLabel();
        if (IsCurrentSwingUltimate())
            return $"{weapon} 大招";
        if (IsCurrentSwingSpecial())
            return $"{weapon} 特技";
        if (IsCurrentSwingJumpDownAttack())
            return $"{weapon} 下砸";
        if (IsCurrentSwingCrouchMelee())
            return $"{weapon} 蹲攻";
        if (IsCurrentSwingUpward())
        {
            bool airborne = physicsCheck != null && !physicsCheck.isGround;
            return airborne ? $"{weapon} 空中上攻" : $"{weapon} 上攻";
        }

        bool inAir = physicsCheck != null && !physicsCheck.isGround;
        return inAir ? $"{weapon} 空中攻击" : $"{weapon} 普通攻击";
    }

    int ResolveExpectedSkillDamage()
    {
        if (IsCurrentSwingJumpDownAttack())
            return Mathf.Max(1, jumpDownImpactDamage);
        if (IsCurrentSwingSpecial() && hasSpecialProfile)
            return ResolveSpecialSwingDamage();
        return ResolveMeleeSwingDamage();
    }

    /// <summary>普通近战伤害。蹲攻四武器统一为空手 meleeDamage。</summary>
    int ResolveMeleeSwingDamage()
    {
        if (IsCurrentSwingCrouchMelee())
            return Mathf.Max(1, meleeDamage);
        return activeProfile.damage > 0 ? activeProfile.damage : meleeDamage;
    }

    void TryStartMeleeAttack()
    {
        // 有 Rush 连段待接时本帧不走普通起手，交给 TryConsumePendingRushCombo
        if (hasPendingRushCombo)
            return;

        if (holdingAttackInputLock)
            return;

        if (playerMovement != null && playerMovement.IsActionLocked)
            return;

        if (playerAnim == null || playerAnim.IsDead)
            return;

        if (playerAnim.IsMelee || playerAnim.IsSpecial)
            return;

        if (!actions.Player.Attack.WasPressedThisFrame())
            return;

        int weaponId = ResolveCurrentWeaponId();
        int meleeAmmoCost = ResolveMeleeAmmoCost(weaponId);
        if (!HasWeaponAmmo(weaponId, meleeAmmoCost))
            return;

        if (detectZone != null && detectZone.HasValidTarget)
        {
            var target = detectZone.GetNearestTarget(transform.position);
            if (target != null && playerMovement != null)
                playerMovement.FaceTowardWorldX(target.position.x);
        }

        // 攻击前用本帧输入同步仰视/俯视，避免与 Bob 比 PlayerMovement 的 Update 顺序导致站立 upattack 丢方向
        Vector2 move = actions.Player.Move.ReadValue<Vector2>();
        bool lookUp = move.y > inputThreshold;
        bool lookDown = !physicsCheck.isGround && move.y < -inputThreshold;
        playerAnim.SetLookUp(lookUp);
        playerAnim.SetLookDown(lookDown);

        swingHitTargets.Clear();
        specialRearHitTargets.Clear();
        whipUltUpHitTargets.Clear();
        whipUltDownHitTargets.Clear();
        swingHitCountables.Clear();
        buzzsawActiveHitTick = -1;
        playerAnim.InterruptTurn();
        if (!playerAnim.TryPlayMeleeAnim())
            return;

        TryConsumeWeaponAmmo(weaponId, meleeAmmoCost);
        ApplyActiveProfileToColliders();
        LogSkillCast();

        if (fullBodyAnim != null && fullBodyAnim.IsJumpDownAttack)
        {
            // 落地砸地四武器共用音效，不播分武器 melee/airMelee
            BeginJumpDownAttack();
        }
        else
        {
            if (fullBodyAnim != null && fullBodyAnim.IsCrouchMelee)
                PlaySessionRecorder.Instance?.RecordMeleeSlide();
            PlayMeleeActionSfx();
            BeginAttackInputLock();
        }
    }

    bool IsAttackUpComboWeapon(int weaponId)
        => weaponId == 0 || weaponId == 1;

    bool IsRushComboMeleeEligible()
    {
        if (!rushAttackComboEnabled || fullBodyAnim == null)
            return false;
        if (!IsAttackUpComboWeapon(ResolveCurrentWeaponId()))
            return false;
        // 仅地面可登记/衔接连段
        if (physicsCheck == null || !physicsCheck.isGround)
            return false;
        if (playerAnim == null || !playerAnim.IsMelee)
            return false;
        if (IsCurrentSwingSpecial() || IsCurrentSwingUltimate())
            return false;
        if (IsCurrentSwingCrouchMelee() || IsCurrentSwingJumpDownAttack())
            return false;
        // 仅普攻 / 上攻可交替连段
        return true;
    }

    void TryBufferRushComboInput()
    {
        if (!actions.Player.Attack.WasPressedThisFrame())
            return;
        if (!IsRushComboMeleeEligible())
            return;

        if (playerAnim.TryGetMeleeAnimProgress(out float t) && t < rushComboBufferEarliest)
            return;

        // 当前上攻 → 下一段普攻；当前普攻 → 下一段上攻
        pendingRushComboUpward = !IsCurrentSwingUpward();
        hasPendingRushCombo = true;
    }

    void TryConsumePendingRushCombo()
    {
        if (!hasPendingRushCombo)
            return;
        if (!rushAttackComboEnabled || fullBodyAnim == null)
        {
            ClearPendingRushCombo();
            return;
        }
        if (!IsAttackUpComboWeapon(ResolveCurrentWeaponId()))
        {
            ClearPendingRushCombo();
            return;
        }
        // 离地则丢弃缓冲，不做空中接技
        if (physicsCheck == null || !physicsCheck.isGround)
        {
            ClearPendingRushCombo();
            return;
        }
        if (playerAnim == null || playerAnim.IsDead || playerAnim.IsMelee || playerAnim.IsSpecial)
            return;
        if (holdingAttackInputLock)
            return;
        if (playerMovement != null && playerMovement.IsActionLocked)
        {
            ClearPendingRushCombo();
            return;
        }

        bool upward = pendingRushComboUpward;
        ClearPendingRushCombo();
        TryStartRushComboMelee(upward);
    }

    void ClearPendingRushCombo()
    {
        hasPendingRushCombo = false;
        pendingRushComboUpward = false;
    }

    void TryStartRushComboMelee(bool upward)
    {
        if (fullBodyAnim == null || playerAnim == null)
            return;
        if (playerAnim.IsMelee || playerAnim.IsSpecial || playerAnim.IsDead)
            return;
        if (holdingAttackInputLock)
            return;
        if (physicsCheck == null || !physicsCheck.isGround)
            return;

        int weaponId = ResolveCurrentWeaponId();
        if (!IsAttackUpComboWeapon(weaponId))
            return;

        int meleeAmmoCost = ResolveMeleeAmmoCost(weaponId);
        if (!HasWeaponAmmo(weaponId, meleeAmmoCost))
            return;

        if (detectZone != null && detectZone.HasValidTarget)
        {
            var target = detectZone.GetNearestTarget(transform.position);
            if (target != null && playerMovement != null)
                playerMovement.FaceTowardWorldX(target.position.x);
        }

        playerAnim.SetLookUp(upward);
        playerAnim.SetLookDown(false);

        swingHitTargets.Clear();
        specialRearHitTargets.Clear();
        whipUltUpHitTargets.Clear();
        whipUltDownHitTargets.Clear();
        swingHitCountables.Clear();
        buzzsawActiveHitTick = -1;
        playerAnim.InterruptTurn();

        if (!fullBodyAnim.TryPlayMeleeAnimForcedLookUp(upward))
            return;

        // 强制上攻连段不应落成下砸；且连段仅地面
        if (fullBodyAnim.IsJumpDownAttack || !physicsCheck.isGround)
            return;

        TryConsumeWeaponAmmo(weaponId, meleeAmmoCost);
        ApplyActiveProfileToColliders();
        LogSkillCast();
        PlayMeleeActionSfx();
        BeginAttackInputLock();
    }

    void TryStartSpecialAttack()
    {
        if (!actions.Player.Ability1.WasPressedThisFrame())
            return;
        TryBeginSpecialOrUltimate(ultimate: false);
    }

    void TryStartUltimateAttack()
    {
        if (!actions.Player.Ability2.WasPressedThisFrame())
            return;
        TryBeginSpecialOrUltimate(ultimate: true);
    }

    void TryBeginSpecialOrUltimate(bool ultimate)
    {
        if (holdingAttackInputLock)
            return;

        ClearPendingRushCombo();

        if (playerMovement != null && playerMovement.IsActionLocked)
            return;

        if (playerAnim == null || playerAnim.IsDead)
            return;

        if (playerAnim.IsMelee)
            return;

        int weaponId = ResolveCurrentWeaponId();
        if (weaponId == 0 || !hasSpecialProfile || activeSpecialProfile.weaponId != weaponId)
            return;

        if (fullBodyAnim != null
            && (fullBodyAnim.AppliedWeaponDefinition == null
                || fullBodyAnim.AppliedWeaponDefinition.special == null))
            return;

        if (ultimate)
        {
            if (ultimateAbilityPowerCost > 0f
                && (selfCharacter == null || selfCharacter.AbilityPower < ultimateAbilityPowerCost))
                return;
        }

        int specialCost = ResolveSpecialAmmoCost(weaponId);
        if (!HasWeaponAmmo(weaponId, specialCost))
            return;

        if (detectZone != null && detectZone.HasValidTarget)
        {
            var target = detectZone.GetNearestTarget(transform.position);
            if (target != null && playerMovement != null)
                playerMovement.FaceTowardWorldX(target.position.x);
        }

        swingHitTargets.Clear();
        specialRearHitTargets.Clear();
        whipUltUpHitTargets.Clear();
        whipUltDownHitTargets.Clear();
        ClearRushCarryState();
        swingHitCountables.Clear();
        buzzsawActiveHitTick = -1;
        playerAnim.InterruptTurn();

        bool played = ultimate
            ? fullBodyAnim != null && fullBodyAnim.TryPlayUltimateAnim()
            : playerAnim.TryPlaySpecialAnim();
        if (!played)
            return;

        if (ultimate)
            PlaySessionRecorder.Instance?.RecordAbility2();
        else
            PlaySessionRecorder.Instance?.RecordAbility1();

        if (ultimate
            && ultimateAbilityPowerCost > 0f
            && selfCharacter != null)
            selfCharacter.DrainAbilityPower(ultimateAbilityPowerCost);

        TryConsumeWeaponAmmo(weaponId, specialCost);

        ApplyActiveProfileToColliders();
        PlayAttackSfx(ResolveWeaponSpecialLabel(weaponId));
        BeginAttackInputLock();
        LogSkillCast();
    }

    static AmmoType ResolveWeaponAmmoType(int weaponId) => weaponId switch
    {
        1 => AmmoType.S,
        2 => AmmoType.M,
        3 => AmmoType.L,
        _ => AmmoType.S,
    };

    int ResolveMeleeAmmoCost(int weaponId)
    {
        if (weaponId == 0)
            return 0;
        return Mathf.Max(0, FindAmmoCost(weaponId).meleeCost);
    }

    int ResolveSpecialAmmoCost(int weaponId)
    {
        if (weaponId == 0)
            return 0;
        return Mathf.Max(0, FindAmmoCost(weaponId).specialCost);
    }

    WeaponAmmoCost FindAmmoCost(int weaponId)
    {
        if (weaponAmmoCosts != null)
        {
            for (int i = 0; i < weaponAmmoCosts.Length; i++)
            {
                if (weaponAmmoCosts[i].weaponId == weaponId)
                    return weaponAmmoCosts[i];
            }
        }

        return default;
    }

    bool HasWeaponAmmo(int weaponId, int amount)
    {
        if (weaponId == 0 || amount <= 0)
            return true;
        if (selfCharacter == null)
            return false;
        return HasEnoughAmmo(ResolveWeaponAmmoType(weaponId), amount);
    }

    bool TryConsumeWeaponAmmo(int weaponId, int amount)
    {
        if (weaponId == 0 || amount <= 0 || selfCharacter == null)
            return amount <= 0;
        return selfCharacter.TrySpendAmmo(ResolveWeaponAmmoType(weaponId), amount);
    }

    bool HasEnoughAmmo(AmmoType type, int amount)
    {
        if (selfCharacter == null || amount <= 0)
            return amount <= 0;

        return type switch
        {
            AmmoType.S => selfCharacter.BulletS >= amount,
            AmmoType.M => selfCharacter.BulletM >= amount,
            AmmoType.L => selfCharacter.BulletL >= amount,
            _ => false,
        };
    }

    void UpdateMeleeHitbox()
    {
        if (meleeHitbox == null || playerAnim == null)
            return;

        if (!playerAnim.IsMelee)
        {
            if (meleeHitbox.activeSelf)
                meleeHitbox.SetActive(false);
            ApplyHitboxShape(upward: false, special: false);
            EndRushSpecialState();
            EndJumpDownAttack();
            EndAttackInputLock();
            buzzsawActiveHitTick = -1;
            return;
        }

        bool special = IsCurrentSwingSpecial();
        SyncHitboxAnchor();

        // 砸地：下落中无近战盒，落地由 ApplyJumpDownImpact 结算
        if (IsCurrentSwingJumpDownAttack())
        {
            if (meleeHitbox.activeSelf)
                meleeHitbox.SetActive(false);
            if (meleeAttack != null)
                meleeAttack.enabled = false;
            return;
        }

        if (special && hasSpecialProfile && activeSpecialProfile.weaponId == 3)
        {
            UpdateBuzzsawSpecialHits();
            return;
        }

        if (!special && activeProfile.weaponId == 3)
        {
            UpdateBuzzsawMeleeHits();
            return;
        }

        if (special && hasSpecialProfile && activeSpecialProfile.weaponId == 2)
        {
            UpdateWhipSpecialHits();
            return;
        }

        if (special && hasSpecialProfile && activeSpecialProfile.weaponId == 1)
        {
            UpdateRushSpecialHits();
            return;
        }

        ApplyHitboxShape(upward: !special && IsCurrentSwingUpward(), special: special);

        float windowStart;
        float windowEnd;
        if (special && hasSpecialProfile)
        {
            windowStart = activeSpecialProfile.hitStart;
            windowEnd = activeSpecialProfile.hitEnd;
        }
        else
        {
            ResolveMeleeHitWindow(out windowStart, out windowEnd);
        }
        int maxTargets = special && hasSpecialProfile ? activeSpecialProfile.maxTargets : activeProfile.maxTargets;

        if (meleeAttack != null)
        {
            int damage = special && hasSpecialProfile
                ? ResolveSpecialSwingDamage()
                : ResolveMeleeSwingDamage();
            meleeAttack.damage = damage;
            meleeAttack.enabled = maxTargets <= 0;
            SyncCancelEnemyProjectiles();
        }

        bool inHitWindow = playerAnim.TryGetMeleeAnimProgress(out float t)
            && t >= windowStart && t <= windowEnd;

        if (inHitWindow)
        {
            if (!meleeHitbox.activeSelf)
                meleeHitbox.SetActive(true);

            if (maxTargets > 0)
                ProcessLimitedHitTargets(maxTargets);
            else if (CanCancelEnemyProjectiles())
                CancelEnemyProjectilesInCurrentHitbox();
        }
        else if (meleeHitbox.activeSelf)
        {
            meleeHitbox.SetActive(false);
        }
    }

    void UpdateRushSpecialHits()
    {
        if (meleeAttack != null)
        {
            meleeAttack.damage = ResolveSpecialSwingDamage();
            meleeAttack.enabled = false;
            SyncCancelEnemyProjectiles();
        }

        if (!playerAnim.TryGetMeleeAnimProgress(out float t))
        {
            ForceDisableAttackKnockback();
            ApplyHitboxShape(upward: false, special: true);
            if (meleeHitbox != null && meleeHitbox.activeSelf)
                meleeHitbox.SetActive(false);
            return;
        }

        bool ultimateLaunch = IsCurrentSwingUltimate() && HasRushUltimateLaunchWindow();
        bool launchWindow = ultimateLaunch
            && t >= rushUltimateLaunchStart
            && t <= rushUltimateLaunchEnd;
        float dashEnd = ultimateLaunch ? rushUltimateLaunchStart : activeSpecialProfile.hitEnd;
        bool dashWindow = !launchWindow
            && t >= activeSpecialProfile.hitStart
            && t <= dashEnd;

        if (launchWindow)
        {
            UpdateRushUltimateLaunchHits();
            return;
        }

        // Rush 冲刺段强制关掉击退冲量；推动只靠钉在判定盒前端
        ForceDisableAttackKnockback();
        ApplyHitboxShape(upward: false, special: true);

        if (dashWindow)
        {
            if (meleeHitbox != null && !meleeHitbox.activeSelf)
                meleeHitbox.SetActive(true);

            ProcessSpecialBoxHits(
                activeSpecialProfile.hitboxOffset,
                activeSpecialProfile.hitboxSize,
                ResolveSpecialSwingDamage(),
                swingHitTargets,
                activeSpecialProfile.maxTargets,
                hitNote: null,
                launchUpward: false,
                registerRushCarry: true);
        }
        else if (meleeHitbox != null && meleeHitbox.activeSelf)
        {
            meleeHitbox.SetActive(false);
        }
    }

    void UpdateRushUltimateLaunchHits()
    {
        ForceDisableAttackKnockback();
        ClearRushCarryState();

        Vector2 size = rushUltimateLaunchHitboxSize.x > 0.01f && rushUltimateLaunchHitboxSize.y > 0.01f
            ? rushUltimateLaunchHitboxSize
            : new Vector2(2.2f, 2.8f);
        Vector2 offset = rushUltimateLaunchHitboxOffset;

        if (meleeHitboxCollider != null)
        {
            meleeHitboxCollider.size = size;
            meleeHitboxCollider.offset = offset;
        }

        if (meleeHitbox != null && !meleeHitbox.activeSelf)
            meleeHitbox.SetActive(true);

        if (meleeAttack != null)
        {
            meleeAttack.enableKnockback = false;
            meleeAttack.knockbackForce = 0f;
            meleeAttack.damage = ResolveRushUltimateLaunchDamage();
            SyncCancelEnemyProjectiles();
        }

        ProcessSpecialBoxHits(
            offset,
            size,
            ResolveRushUltimateLaunchDamage(),
            specialRearHitTargets,
            activeSpecialProfile.maxTargets,
            hitNote: "击飞",
            launchUpward: true);
    }

    bool HasRushUltimateLaunchWindow()
        => rushUltimateLaunchEnd > rushUltimateLaunchStart + 0.01f;

    int ResolveRushUltimateLaunchDamage()
    {
        if (rushUltimateLaunchDamage > 0)
            return rushUltimateLaunchDamage;
        return ResolveSpecialSwingDamage();
    }

    void UpdateWhipSpecialHits()
    {
        ApplyRushAttackKnockback(true);

        if (meleeAttack != null)
        {
            meleeAttack.damage = ResolveSpecialSwingDamage();
            meleeAttack.enabled = false;
            SyncCancelEnemyProjectiles();
        }

        if (!playerAnim.TryGetMeleeAnimProgress(out float t))
        {
            ApplyHitboxShape(upward: false, special: true);
            if (meleeHitbox.activeSelf)
                meleeHitbox.SetActive(false);
            return;
        }

        bool ultimate = IsCurrentSwingUltimate();
        ResolveWhipDirectionalWindows(
            ultimate,
            out float frontStart,
            out float frontEnd,
            out float rearStart,
            out float rearEnd);

        bool frontWindow = t >= frontStart && t <= frontEnd;
        bool rearWindow = t >= rearStart && t <= rearEnd;
        bool upWindow = ultimate
            && whipUltimateUpHitEnd > whipUltimateUpHitStart + 0.01f
            && t >= whipUltimateUpHitStart
            && t <= whipUltimateUpHitEnd;
        bool downWindow = ultimate
            && whipUltimateDownHitEnd > whipUltimateDownHitStart + 0.01f
            && t >= whipUltimateDownHitStart
            && t <= whipUltimateDownHitEnd;

        Vector2 rearOffset = ResolveWhipRearLocalOffset(activeSpecialProfile.hitboxOffset);
        ResolveWhipUltimateUpHitbox(out Vector2 upSize, out Vector2 upOffset);
        ResolveWhipUltimateDownHitbox(out Vector2 downSize, out Vector2 downOffset);

        // 可见盒跟随当前挥击方向，避免 Scene 里看起来永远只有前方
        if (meleeHitboxCollider != null)
        {
            if (upWindow)
            {
                meleeHitboxCollider.size = upSize;
                meleeHitboxCollider.offset = upOffset;
            }
            else if (downWindow)
            {
                meleeHitboxCollider.size = downSize;
                meleeHitboxCollider.offset = downOffset;
            }
            else
            {
                meleeHitboxCollider.size = activeSpecialProfile.hitboxSize;
                meleeHitboxCollider.offset = rearWindow && !frontWindow
                    ? rearOffset
                    : activeSpecialProfile.hitboxOffset;
            }
        }

        if (frontWindow || rearWindow || upWindow || downWindow)
        {
            if (!meleeHitbox.activeSelf)
                meleeHitbox.SetActive(true);

            Physics2D.SyncTransforms();

            if (upWindow)
            {
                ProcessSpecialBoxHits(
                    upOffset,
                    upSize,
                    ResolveSpecialSwingDamage(),
                    whipUltUpHitTargets,
                    activeSpecialProfile.maxTargets,
                    hitNote: "上方");
            }

            if (frontWindow)
            {
                ProcessSpecialBoxHits(
                    activeSpecialProfile.hitboxOffset,
                    activeSpecialProfile.hitboxSize,
                    ResolveSpecialSwingDamage(),
                    swingHitTargets,
                    activeSpecialProfile.maxTargets,
                    knockbackSign: 1f);
            }

            if (downWindow)
            {
                ProcessSpecialBoxHits(
                    downOffset,
                    downSize,
                    ResolveSpecialSwingDamage(),
                    whipUltDownHitTargets,
                    activeSpecialProfile.maxTargets,
                    hitNote: "下方");
            }

            if (rearWindow)
            {
                ProcessSpecialBoxHits(
                    rearOffset,
                    activeSpecialProfile.hitboxSize,
                    ResolveSpecialSwingDamage(),
                    specialRearHitTargets,
                    activeSpecialProfile.maxTargets,
                    knockbackSign: -1f);
            }
        }
        else if (meleeHitbox.activeSelf)
        {
            meleeHitbox.SetActive(false);
        }
    }

    void ResolveWhipDirectionalWindows(
        bool ultimate,
        out float frontStart,
        out float frontEnd,
        out float rearStart,
        out float rearEnd)
    {
        frontStart = activeSpecialProfile.hitStart;
        frontEnd = activeSpecialProfile.hitEnd;
        rearStart = activeSpecialProfile.rearHitStart;
        rearEnd = activeSpecialProfile.rearHitEnd;

        if (!ultimate)
            return;

        if (whipUltimateFrontHitEnd > whipUltimateFrontHitStart + 0.01f)
        {
            frontStart = whipUltimateFrontHitStart;
            frontEnd = whipUltimateFrontHitEnd;
        }

        if (whipUltimateRearHitEnd > whipUltimateRearHitStart + 0.01f)
        {
            rearStart = whipUltimateRearHitStart;
            rearEnd = whipUltimateRearHitEnd;
        }
    }

    void ResolveWhipUltimateUpHitbox(out Vector2 size, out Vector2 offset)
    {
        size = whipUltimateUpHitboxSize.x > 0.01f && whipUltimateUpHitboxSize.y > 0.01f
            ? whipUltimateUpHitboxSize
            : new Vector2(1.8f, 6.2f);
        offset = whipUltimateUpHitboxOffset;
    }

    void ResolveWhipUltimateDownHitbox(out Vector2 size, out Vector2 offset)
    {
        size = whipUltimateDownHitboxSize.x > 0.01f && whipUltimateDownHitboxSize.y > 0.01f
            ? whipUltimateDownHitboxSize
            : new Vector2(2.8f, 5.8f);
        offset = whipUltimateDownHitboxOffset;
    }

    /// <summary>
    /// 将前方特技盒绕角色 pivot 做 X 镜像。
    /// 不能只取 -hitboxOffset：MeleePoint1 已在身前，那样后方盒会仍落在身前/身上。
    /// </summary>
    Vector2 ResolveWhipRearLocalOffset(Vector2 frontLocal)
    {
        Transform space = meleeHitbox != null ? meleeHitbox.transform : transform;
        Vector2 frontWorld = space.TransformPoint(frontLocal);
        Vector2 rearWorld = new Vector2(2f * transform.position.x - frontWorld.x, frontWorld.y);
        return space.InverseTransformPoint(rearWorld);
    }

    void UpdateBuzzsawMeleeHits()
    {
        ApplyHitboxShape(upward: IsCurrentSwingUpward(), special: false);
        ResolveMeleeHitWindow(out float windowStart, out float windowEnd);

        int totalDamage = ResolveMeleeSwingDamage();
        if (meleeAttack != null)
            meleeAttack.enabled = false;

        if (!playerAnim.TryGetMeleeAnimProgress(out float t)
            || !TryAdvanceBuzzsawHitTick(t, windowStart, windowEnd, BuzzsawMeleeHitTicks, out int tick))
        {
            if (meleeHitbox != null && meleeHitbox.activeSelf)
                meleeHitbox.SetActive(false);
            return;
        }

        int tickDamage = SplitDamageAcrossTicks(totalDamage, BuzzsawMeleeHitTicks, tick);
        if (meleeAttack != null)
            meleeAttack.damage = tickDamage;

        SyncBuzzsawShieldMultiplier();
        SyncCancelEnemyProjectiles();

        if (meleeHitbox != null && !meleeHitbox.activeSelf)
            meleeHitbox.SetActive(true);

        ProcessLimitedHitTargets(activeProfile.maxTargets, $"第{tick + 1}/{BuzzsawMeleeHitTicks}段");
    }

    void UpdateBuzzsawSpecialHits()
    {
        if (meleeHitbox != null && meleeHitbox.activeSelf)
            meleeHitbox.SetActive(false);

        if (meleeAttack != null)
            meleeAttack.enabled = false;

        if (!playerAnim.TryGetMeleeAnimProgress(out float t))
            return;

        if (!TryAdvanceBuzzsawHitTick(
                t,
                activeSpecialProfile.hitStart,
                activeSpecialProfile.hitEnd,
                BuzzsawSpecialHitTicks,
                out int tick))
            return;

        SyncBuzzsawShieldMultiplier();
        SyncCancelEnemyProjectiles();
        ProcessBuzzsawCircleHits(tick);
    }

    int ResolveBuzzsawHitTickCount()
    {
        if (activeProfile.weaponId != 3)
            return 1;
        if (IsCurrentSwingJumpDownAttack())
            return 1;
        if (IsCurrentSwingSpecial())
            return BuzzsawSpecialHitTicks;
        return BuzzsawMeleeHitTicks;
    }

    static int ResolveHitTickIndex(float t, float windowStart, float windowEnd, int ticks)
    {
        if (ticks <= 1)
            return t >= windowStart && t <= windowEnd ? 0 : -1;
        if (windowEnd <= windowStart || t < windowStart || t > windowEnd)
            return -1;

        float u = Mathf.InverseLerp(windowStart, windowEnd, t);
        int tick = Mathf.FloorToInt(u * ticks);
        return Mathf.Clamp(tick, 0, ticks - 1);
    }

    bool TryAdvanceBuzzsawHitTick(float t, float windowStart, float windowEnd, int ticks, out int tick)
    {
        tick = ResolveHitTickIndex(t, windowStart, windowEnd, ticks);
        if (tick < 0)
        {
            buzzsawActiveHitTick = -1;
            return false;
        }

        if (tick != buzzsawActiveHitTick)
        {
            buzzsawActiveHitTick = tick;
            swingHitTargets.Clear();
            swingHitCountables.Clear();
        }

        return true;
    }

    static int SplitDamageAcrossTicks(int total, int ticks, int tickIndex)
    {
        ticks = Mathf.Max(1, ticks);
        total = Mathf.Max(0, total);
        if (total <= 0)
            return 0;

        tickIndex = Mathf.Clamp(tickIndex, 0, ticks - 1);
        int baseDamage = total / ticks;
        int remainder = total % ticks;
        int damage = baseDamage + (tickIndex < remainder ? 1 : 0);
        return Mathf.Max(1, damage);
    }

    void ProcessBuzzsawCircleHits(int tick)
    {
        if (meleeAttack == null)
            return;

        Vector2 center = ResolveSpecialCenter();
        float outer = activeSpecialProfile.outerRadius;
        float inner = activeSpecialProfile.innerRadius;
        int highDamage = SplitDamageAcrossTicks(ResolveSpecialSwingDamage(), BuzzsawSpecialHitTicks, tick);
        int lowDamage = SplitDamageAcrossTicks(ResolveSpecialInnerDamage(), BuzzsawSpecialHitTicks, tick);
        int maxTargets = activeSpecialProfile.maxTargets;

        if (maxTargets > 0 && swingHitTargets.Count >= maxTargets)
            return;

        int count = Physics2D.OverlapCircleNonAlloc(center, outer, overlapBuffer);
        if (count <= 0)
            return;

        for (int i = 0; i < count; i++)
            TryRegisterHitCountable(overlapBuffer[i]);

        overlapCharacters.Clear();
        for (int i = 0; i < count; i++)
        {
            if (!TryResolveAttackTarget(overlapBuffer[i], swingHitTargets, out Character target))
                continue;
            if (!overlapCharacters.Contains(target))
                overlapCharacters.Add(target);
        }

        if (overlapCharacters.Count == 0)
            return;

        overlapCharacters.Sort((a, b) =>
        {
            float da = ((Vector2)a.transform.position - center).sqrMagnitude;
            float db = ((Vector2)b.transform.position - center).sqrMagnitude;
            return da.CompareTo(db);
        });

        int slots = maxTargets > 0 ? maxTargets - swingHitTargets.Count : int.MaxValue;
        float innerSq = inner * inner;
        for (int i = 0; i < overlapCharacters.Count && slots > 0; i++)
        {
            var target = overlapCharacters[i];
            if (swingHitTargets.Contains(target))
                continue;

            float distSq = ((Vector2)target.transform.position - center).sqrMagnitude;
            // 内圈高伤（damage），外圈低伤（innerDamage）
            bool inInner = distSq <= innerSq;
            meleeAttack.damage = inInner ? highDamage : lowDamage;
            TryDealMeleeDamage(
                target,
                inInner
                    ? $"内圈 第{tick + 1}/{BuzzsawSpecialHitTicks}段"
                    : $"外圈 第{tick + 1}/{BuzzsawSpecialHitTicks}段");
            swingHitTargets.Add(target);
            slots--;
        }
    }

    void ProcessSpecialBoxHits(
        Vector2 localOffset,
        Vector2 localSize,
        int damage,
        HashSet<Character> hitSet,
        int maxTargets,
        float knockbackSign = 0f,
        string hitNote = null,
        bool launchUpward = false,
        bool registerRushCarry = false)
    {
        if (meleeAttack == null || meleeHitbox == null)
            return;

        if (maxTargets > 0 && hitSet.Count >= maxTargets)
            return;

        Transform space = meleeHitbox.transform;
        Vector2 center = space.TransformPoint(localOffset);
        Vector3 lossy = space.lossyScale;
        Vector2 worldSize = new Vector2(
            Mathf.Abs(localSize.x * lossy.x),
            Mathf.Abs(localSize.y * lossy.y));
        float angle = space.eulerAngles.z;

        var filter = new ContactFilter2D
        {
            useTriggers = true,
            useLayerMask = false,
            useDepth = false,
        };

        int count = Physics2D.OverlapBox(center, worldSize, angle, filter, overlapBuffer);
        if (count <= 0)
            return;

        for (int i = 0; i < count; i++)
            TryRegisterHitCountable(overlapBuffer[i]);

        overlapCharacters.Clear();
        for (int i = 0; i < count; i++)
        {
            if (!TryResolveAttackTarget(overlapBuffer[i], hitSet, out Character target))
                continue;
            if (!overlapCharacters.Contains(target))
                overlapCharacters.Add(target);
        }

        if (overlapCharacters.Count == 0)
            return;

        Vector2 origin = transform.position;
        overlapCharacters.Sort((a, b) =>
        {
            float da = ((Vector2)a.transform.position - origin).sqrMagnitude;
            float db = ((Vector2)b.transform.position - origin).sqrMagnitude;
            return da.CompareTo(db);
        });

        int slots = maxTargets > 0 ? maxTargets - hitSet.Count : int.MaxValue;
        int previousDamage = meleeAttack.damage;
        meleeAttack.damage = damage;

        // Attack.ResolveKnockbackDir 读 transform.right；后方段临时翻转（给 Character 路径用）
        Transform attackTransform = meleeAttack.transform;
        Vector3 savedAttackScale = attackTransform.localScale;
        bool flipKnockback = knockbackSign < 0f;
        if (flipKnockback)
        {
            Vector3 flipped = savedAttackScale;
            flipped.x = -flipped.x;
            attackTransform.localScale = flipped;
        }

        bool registerWhipPush = Mathf.Abs(knockbackSign) > 0.01f
            && IsCurrentSwingSpecial()
            && hasSpecialProfile
            && activeSpecialProfile.weaponId == 2
            && activeSpecialProfile.knockbackForce > 0.01f;

        for (int i = 0; i < overlapCharacters.Count && slots > 0; i++)
        {
            var target = overlapCharacters[i];
            if (hitSet.Contains(target))
                continue;

            bool damaged = TryDealMeleeDamage(
                target,
                CombineHitNotes(hitNote, knockbackSign < 0f ? "后方" : null));
            if (damaged && registerWhipPush)
                RegisterWhipKnockback(target, knockbackSign);
            if (damaged && launchUpward)
                RegisterRushLaunch(target);
            if (registerRushCarry)
                BeginRushCarry(target);

            hitSet.Add(target);
            slots--;
        }

        if (flipKnockback)
            attackTransform.localScale = savedAttackScale;

        meleeAttack.damage = previousDamage;
    }

    void RegisterWhipKnockback(Character target, float knockbackSign)
    {
        if (target == null)
            return;

        float face = playerMovement != null
            ? playerMovement.FaceDirection
            : Mathf.Sign(transform.localScale.x);
        if (Mathf.Approximately(face, 0f))
            face = 1f;

        float dir = face * Mathf.Sign(knockbackSign);
        float speed = Mathf.Max(1f, activeSpecialProfile.knockbackForce);
        float duration = Mathf.Max(0.05f, activeSpecialProfile.knockbackDuration);
        float until = Time.time + duration;

        var targetRb = target.GetComponent<Rigidbody2D>();
        for (int i = 0; i < whipKnockbackEntries.Count; i++)
        {
            var entry = whipKnockbackEntries[i];
            bool same = (targetRb != null && entry.rb == targetRb)
                || entry.targetTransform == target.transform;
            if (!same)
                continue;

            entry.dir = dir;
            entry.speed = speed;
            entry.untilTime = until;
            entry.rb = targetRb;
            entry.targetTransform = target.transform;
            whipKnockbackEntries[i] = entry;
            return;
        }

        whipKnockbackEntries.Add(new WhipKnockbackEntry
        {
            rb = targetRb,
            targetTransform = target.transform,
            dir = dir,
            speed = speed,
            untilTime = until,
        });
    }

    void RegisterRushLaunch(Character target)
    {
        if (target == null)
            return;

        float face = playerMovement != null
            ? playerMovement.FaceDirection
            : Mathf.Sign(transform.localScale.x);
        if (Mathf.Approximately(face, 0f))
            face = 1f;

        float vx = face * rushUltimateLaunchSpeedX;
        float vy = Mathf.Max(0.01f, rushUltimateLaunchSpeedY);
        var targetRb = target.GetComponent<Rigidbody2D>();
        if (targetRb != null && targetRb.simulated)
        {
            if (targetRb.bodyType == RigidbodyType2D.Kinematic)
                targetRb.MovePosition(targetRb.position + new Vector2(vx, vy) * 0.02f);
            else
                targetRb.linearVelocity = new Vector2(vx, vy);
        }

        float duration = Mathf.Max(0.05f, rushUltimateLaunchHoldDuration);
        float until = Time.time + duration;
        for (int i = 0; i < whipKnockbackEntries.Count; i++)
        {
            var entry = whipKnockbackEntries[i];
            bool same = (targetRb != null && entry.rb == targetRb)
                || entry.targetTransform == target.transform;
            if (!same)
                continue;

            entry.dir = face;
            entry.speed = rushUltimateLaunchSpeedX;
            entry.untilTime = until;
            entry.rb = targetRb;
            entry.targetTransform = target.transform;
            whipKnockbackEntries[i] = entry;
            return;
        }

        whipKnockbackEntries.Add(new WhipKnockbackEntry
        {
            rb = targetRb,
            targetTransform = target.transform,
            dir = face,
            speed = rushUltimateLaunchSpeedX,
            untilTime = until,
        });
    }

    void ApplyWhipKnockbackVelocities()
    {
        if (whipKnockbackEntries.Count == 0)
            return;

        float now = Time.time;
        float dt = Time.deltaTime;
        for (int i = whipKnockbackEntries.Count - 1; i >= 0; i--)
        {
            var entry = whipKnockbackEntries[i];
            if (now >= entry.untilTime
                || entry.targetTransform == null
                || (entry.rb == null && entry.targetTransform == null))
            {
                whipKnockbackEntries.RemoveAt(i);
                continue;
            }

            Vector2 delta = new Vector2(entry.dir * entry.speed * dt, 0f);
            if (entry.rb != null && entry.rb.simulated)
            {
                if (entry.rb.bodyType == RigidbodyType2D.Kinematic)
                    entry.rb.MovePosition(entry.rb.position + delta);
                else
                    entry.rb.linearVelocity = new Vector2(entry.dir * entry.speed, entry.rb.linearVelocity.y);
            }
            else
            {
                entry.targetTransform.position += (Vector3)delta;
            }
        }
    }

    void ClearWhipKnockbacks()
    {
        whipKnockbackEntries.Clear();
    }

    Vector2 ResolveSpecialCenter()
    {
        Transform anchor = playerAnim != null && playerAnim.IsCrouching ? meleePoint2 : meleePoint1;
        if (anchor == null)
            anchor = transform;

        Vector2 localOffset = hasSpecialProfile ? activeSpecialProfile.hitboxOffset : Vector2.zero;
        // MeleePoint 通常随角色翻转，TransformPoint 会带上面向
        return anchor.TransformPoint(localOffset);
    }

    bool TryResolveAttackTarget(Collider2D col, HashSet<Character> alreadyHit, out Character target)
    {
        target = null;
        if (col == null)
            return false;

        if (col.transform == transform || col.transform.IsChildOf(transform))
            return false;

        target = col.GetComponentInParent<Character>();
        if (target == null || target == selfCharacter || alreadyHit.Contains(target))
            return false;
        if (meleeAttack != null
            && !string.IsNullOrEmpty(meleeAttack.ignoreTag)
            && target.CompareTag(meleeAttack.ignoreTag))
            return false;
        if (!target.CanReceiveHits)
            return false;

        return true;
    }

    void TryRegisterHitCountable(Collider2D col)
    {
        if (col == null || meleeAttack == null)
            return;

        if (col.transform == transform || col.transform.IsChildOf(transform))
            return;

        if (CanCancelEnemyProjectiles())
        {
            var cancelable = col.GetComponentInParent<IEnemyProjectileCancelable>();
            if (cancelable != null)
            {
                cancelable.TryCancelByMelee(meleeAttack);
                return;
            }
        }

        var hitCountable = col.GetComponentInParent<IHitCountable>();
        if (hitCountable == null || swingHitCountables.Contains(hitCountable))
            return;

        // 敌人飞行道具在空手时也不走 IHitCountable 抵销
        if (hitCountable is IEnemyProjectileCancelable)
            return;

        if (hitCountable.RegisterHit(meleeAttack))
            swingHitCountables.Add(hitCountable);
    }

    void CancelEnemyProjectilesInCurrentHitbox()
    {
        if (meleeHitboxCollider == null || !CanCancelEnemyProjectiles())
            return;

        Transform space = meleeHitbox != null ? meleeHitbox.transform : transform;
        Vector2 center = space.TransformPoint(meleeHitboxCollider.offset);
        Vector3 lossy = space.lossyScale;
        Vector2 worldSize = new Vector2(
            Mathf.Abs(meleeHitboxCollider.size.x * lossy.x),
            Mathf.Abs(meleeHitboxCollider.size.y * lossy.y));
        float angle = space.eulerAngles.z;

        var filter = new ContactFilter2D
        {
            useTriggers = true,
            useLayerMask = false,
            useDepth = false,
        };

        int count = Physics2D.OverlapBox(center, worldSize, angle, filter, overlapBuffer);
        for (int i = 0; i < count; i++)
            TryRegisterHitCountable(overlapBuffer[i]);
    }

    static string CombineHitNotes(string a, string b)
    {
        if (string.IsNullOrEmpty(a))
            return b;
        if (string.IsNullOrEmpty(b))
            return a;
        return $"{a} {b}";
    }

    void ProcessLimitedHitTargets(int maxTargets, string hitNote = null)
    {
        if (meleeHitboxCollider == null)
            return;

        ProcessSpecialBoxHits(
            meleeHitboxCollider.offset,
            meleeHitboxCollider.size,
            meleeAttack != null ? meleeAttack.damage : meleeDamage,
            swingHitTargets,
            maxTargets,
            hitNote: hitNote);
    }

    void UpdateDashAttacks()
    {
        // 砸地期间由 UpdateJumpDownAttack 接管速度
        if (jumpDownAttackActive)
            return;

        if (physicsCheck != null)
            physicsCheck.Check();

        if (playerAnim == null || !playerAnim.IsMelee)
        {
            if (rushDashActive)
                rushDashActive = false;
            if (holdingAttackInputLock)
                ApplyAttackLockHorizontalVelocity(wantDash: false, dashSpeed: 0f, dashDir: 0f);

            return;
        }

        float dir = playerMovement != null
            ? playerMovement.FaceDirection
            : Mathf.Sign(transform.localScale.x);
        if (Mathf.Approximately(dir, 0f))
            dir = 1f;

        bool hasProgress = playerAnim.TryGetMeleeAnimProgress(out float t);

        bool rushDash = hasProgress
            && IsCurrentSwingSpecial()
            && hasSpecialProfile
            && activeSpecialProfile.weaponId == 1
            && activeSpecialProfile.rushSpeed > 0.01f
            && t >= activeSpecialProfile.rushStart
            && t <= activeSpecialProfile.rushEnd;

        bool shortDash = hasProgress
            && IsCurrentShortDashMelee()
            && shortMeleeDashSpeed > 0.01f
            && t >= shortMeleeDashStart
            && t <= shortMeleeDashEnd;

        // 锁定期接管水平速度：冲刺窗内加速；空中可左右移动；地面刹停
        if (holdingAttackInputLock || rushDash || shortDash)
        {
            bool wantDash = rushDash || shortDash;
            float speed = rushDash ? activeSpecialProfile.rushSpeed : shortMeleeDashSpeed;
            ApplyAttackLockHorizontalVelocity(wantDash, speed, dir);
            return;
        }

        if (rushDashActive)
            rushDashActive = false;
    }

    /// <summary>
    /// 攻击锁期间的水平速度：冲刺窗优先；空中允许左右移动；地面保持刹停。
    /// </summary>
    void ApplyAttackLockHorizontalVelocity(bool wantDash, float dashSpeed, float dashDir)
    {
        if (rb == null)
            return;

        bool airborne = physicsCheck != null && !physicsCheck.isGround;

        if (wantDash)
        {
            bool blocked = physicsCheck != null && physicsCheck.IsBlockedHorizontally(dashDir);
            if (!blocked)
            {
                rushDashActive = true;
                rb.linearVelocity = new Vector2(dashDir * dashSpeed, rb.linearVelocity.y);
                if (IsCurrentSwingSpecial()
                    && hasSpecialProfile
                    && activeSpecialProfile.weaponId == 1)
                    PushEnemiesAlongRushPath(dashDir);
                return;
            }
        }

        rushDashActive = false;

        if (airborne)
        {
            ApplyAirHorizontalDuringAttackLock();
            return;
        }

        rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
    }

    void ApplyAirHorizontalDuringAttackLock()
    {
        if (rb == null)
            return;

        Vector2 move = actions.Player.Move.ReadValue<Vector2>();
        float moveX = Mathf.Abs(move.x) > inputThreshold ? Mathf.Sign(move.x) : 0f;

        if (physicsCheck != null && moveX != 0f && physicsCheck.IsBlockedHorizontally(moveX))
            moveX = 0f;

        float speed = playerMovement != null ? playerMovement.runSpeed : 4f;

        if (moveX != 0f)
        {
            rb.linearVelocity = new Vector2(moveX * speed, rb.linearVelocity.y);
            if (playerMovement != null)
                playerMovement.FaceTowardWorldX(transform.position.x + moveX);
            return;
        }

        // 无输入：保留惯性；贴墙时清水平速度，避免卡住
        if (physicsCheck != null && (physicsCheck.touchLeftWall || physicsCheck.touchRightWall))
            rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
    }

    void BeginJumpDownAttack()
    {
        jumpDownAttackActive = true;
        jumpDownImpactApplied = false;
        if (fullBodyAnim != null)
            fullBodyAnim.HoldJumpDownAttackUntilImpact = true;

        BeginAttackInputLock();
        PlayAttackSfx("JumpDownStart");

        if (rb != null)
        {
            ApplyAirHorizontalDuringAttackLock();
            rb.linearVelocity = new Vector2(rb.linearVelocity.x, -Mathf.Abs(jumpDownSlamSpeed));
        }
    }

    void EndJumpDownAttack()
    {
        jumpDownAttackActive = false;
        jumpDownImpactApplied = false;
        if (fullBodyAnim != null)
            fullBodyAnim.HoldJumpDownAttackUntilImpact = false;
    }

    void UpdateJumpDownAttack()
    {
        if (!jumpDownAttackActive)
            return;

        if (playerAnim == null || !IsCurrentSwingJumpDownAttack())
        {
            // 近战已结束：若尚未结算冲击且已落地，补一次
            if (!jumpDownImpactApplied && physicsCheck != null)
            {
                physicsCheck.Check();
                if (physicsCheck.isGround)
                    TryApplyJumpDownImpact();
            }

            EndJumpDownAttack();
            return;
        }

        if (physicsCheck != null)
            physicsCheck.Check();

        if (physicsCheck == null || !physicsCheck.isGround)
        {
            ApplyAirHorizontalDuringAttackLock();
            rb.linearVelocity = new Vector2(rb.linearVelocity.x, -Mathf.Abs(jumpDownSlamSpeed));
            return;
        }

        rb.linearVelocity = Vector2.zero;
        TryApplyJumpDownImpact();
    }

    /// <summary>
    /// 落地冲击。必须在 UpdateAirState 之前调用，否则近战会被提前 Complete。
    /// </summary>
    void TryApplyJumpDownImpact()
    {
        if (!jumpDownAttackActive || jumpDownImpactApplied)
            return;

        if (physicsCheck != null)
            physicsCheck.Check();
        if (physicsCheck != null && !physicsCheck.isGround)
            return;

        ApplyJumpDownImpact();
        jumpDownImpactApplied = true;
        PlayAttackSfx("JumpDownImpact");

        if (fullBodyAnim != null)
            fullBodyAnim.HoldJumpDownAttackUntilImpact = false;
    }

    void ApplyJumpDownImpact()
    {
        if (meleeAttack == null)
            return;

        Physics2D.SyncTransforms();

        Vector2 center = (Vector2)transform.position + jumpDownImpactOffset;
        float radius = Mathf.Max(0.25f, jumpDownImpactRadius);

        var filter = new ContactFilter2D
        {
            useTriggers = true,
            useLayerMask = false,
            useDepth = false,
        };

        int count = Physics2D.OverlapCircle(center, radius, filter, overlapBuffer);
        if (count <= 0)
        {
            // 再试一次稍大盒，避免圆判定刚好擦边漏掉高胶囊敌人
            Vector2 boxSize = new Vector2(radius * 2f, radius * 1.6f);
            count = Physics2D.OverlapBox(center, boxSize, 0f, filter, overlapBuffer);
            if (count <= 0)
                return;
        }

        int previousDamage = meleeAttack.damage;
        float previousShieldMultiplier = meleeAttack.shieldDamageMultiplier;
        bool previousEnabled = meleeAttack.enabled;
        meleeAttack.damage = Mathf.Max(1, jumpDownImpactDamage);
        meleeAttack.enabled = false;
        SyncBuzzsawShieldMultiplier();
        SyncCancelEnemyProjectiles();

        swingHitTargets.Clear();
        for (int i = 0; i < count; i++)
        {
            TryRegisterHitCountable(overlapBuffer[i]);

            if (!TryResolveAttackTarget(overlapBuffer[i], swingHitTargets, out Character target))
                continue;

            TryDealMeleeDamage(target);
            swingHitTargets.Add(target);
        }

        meleeAttack.damage = previousDamage;
        meleeAttack.shieldDamageMultiplier = previousShieldMultiplier;
        meleeAttack.enabled = previousEnabled;
    }

    void MaintainAttackLockAnimation()
    {
        if (physicsCheck != null)
            physicsCheck.Check();

        // 砸地：先结算落地伤害，再推进动画完成，避免「动画结束 → 解锁」抢在伤害之前
        if (jumpDownAttackActive)
        {
            bool grounded = physicsCheck != null && physicsCheck.isGround;
            if (!grounded && rb != null)
            {
                ApplyAirHorizontalDuringAttackLock();
                rb.linearVelocity = new Vector2(rb.linearVelocity.x, -Mathf.Abs(jumpDownSlamSpeed));
            }
            else
                TryApplyJumpDownImpact();
        }

        float velocityY = rb != null ? rb.linearVelocity.y : 0f;
        bool groundedNow = physicsCheck != null && physicsCheck.isGround;
        playerAnim?.UpdateAirState(groundedNow, velocityY);

        // 必须等近战（含下砸片播完）完整结束才解锁操作
        if (playerAnim != null && playerAnim.IsMelee)
            return;

        if (jumpDownAttackActive && !jumpDownImpactApplied && groundedNow)
            TryApplyJumpDownImpact();

        EndJumpDownAttack();
        EndAttackInputLock();
    }

    void BeginAttackInputLock()
    {
        if (holdingAttackInputLock)
            return;

        holdingAttackInputLock = true;

        // 不改 PlayerMovement 逻辑：临时禁用组件以屏蔽移动/跳跃输入；
        // 近战完成改由 MaintainAttackLockAnimation 驱动。
        if (playerMovement != null)
        {
            restoredMovementEnabled = playerMovement.enabled;
            playerMovement.enabled = false;
        }

        if (playerRoll != null)
        {
            restoredRollEnabled = playerRoll.enabled;
            playerRoll.enabled = false;
        }

        if (weaponController != null)
        {
            restoredWeaponControllerEnabled = weaponController.enabled;
            weaponController.enabled = false;
        }
    }

    void EndAttackInputLock()
    {
        if (!holdingAttackInputLock)
            return;

        holdingAttackInputLock = false;
        rushDashActive = false;

        if (playerMovement != null && restoredMovementEnabled)
            playerMovement.enabled = true;

        if (playerRoll != null && restoredRollEnabled)
            playerRoll.enabled = true;

        if (weaponController != null && restoredWeaponControllerEnabled)
            weaponController.enabled = true;
    }

    void PushEnemiesAlongRushPath(float dir)
    {
        if (meleeHitbox == null || !hasSpecialProfile || activeSpecialProfile.weaponId != 1)
            return;

        // 大招后段击飞窗不再水平携带
        if (IsCurrentSwingUltimate()
            && HasRushUltimateLaunchWindow()
            && playerAnim != null
            && playerAnim.TryGetMeleeAnimProgress(out float t)
            && t >= rushUltimateLaunchStart)
        {
            ClearRushCarryState();
            return;
        }

        ForceDisableAttackKnockback();
        SyncHitboxAnchor();
        ApplyHitboxShape(upward: false, special: true);

        Transform space = meleeHitbox.transform;
        Vector2 localOffset = activeSpecialProfile.hitboxOffset;
        Vector2 localSize = activeSpecialProfile.hitboxSize;
        Vector2 center = space.TransformPoint(localOffset);
        Vector3 lossy = space.lossyScale;
        Vector2 worldSize = new Vector2(
            Mathf.Abs(localSize.x * lossy.x),
            Mathf.Abs(localSize.y * lossy.y));
        float angle = space.eulerAngles.z;

        Vector2 querySize = worldSize + new Vector2(0.8f, 0.5f);
        int count = Physics2D.OverlapBoxNonAlloc(center, querySize, angle, overlapBuffer);
        for (int i = 0; i < count; i++)
        {
            var col = overlapBuffer[i];
            if (col == null)
                continue;
            if (col.transform == transform || col.transform.IsChildOf(transform))
                continue;

            var target = col.GetComponentInParent<Character>();
            if (target == null || target == selfCharacter || !target.CanReceiveHits)
                continue;
            if (meleeAttack != null
                && !string.IsNullOrEmpty(meleeAttack.ignoreTag)
                && target.CompareTag(meleeAttack.ignoreTag))
                continue;

            BeginRushCarry(target);
        }

        MaintainRushCarriedTargets(dir, center, worldSize);
    }

    void MaintainRushCarriedTargets()
    {
        if (rushCarriedTargets.Count == 0)
            return;
        if (!IsCurrentSwingSpecial()
            || !hasSpecialProfile
            || activeSpecialProfile.weaponId != 1)
            return;
        if (IsCurrentSwingUltimate()
            && HasRushUltimateLaunchWindow()
            && playerAnim != null
            && playerAnim.TryGetMeleeAnimProgress(out float t)
            && t >= rushUltimateLaunchStart)
            return;

        float dir = playerMovement != null
            ? playerMovement.FaceDirection
            : Mathf.Sign(transform.localScale.x);
        if (Mathf.Approximately(dir, 0f))
            dir = 1f;

        if (meleeHitbox == null)
            return;

        Transform space = meleeHitbox.transform;
        Vector2 center = space.TransformPoint(activeSpecialProfile.hitboxOffset);
        Vector3 lossy = space.lossyScale;
        Vector2 worldSize = new Vector2(
            Mathf.Abs(activeSpecialProfile.hitboxSize.x * lossy.x),
            Mathf.Abs(activeSpecialProfile.hitboxSize.y * lossy.y));
        MaintainRushCarriedTargets(dir, center, worldSize);
    }

    void MaintainRushCarriedTargets(float dir, Vector2 center, Vector2 worldSize)
    {
        if (rushCarriedTargets.Count == 0)
            return;

        float halfWidth = worldSize.x * 0.5f;
        float frontInset = Mathf.Clamp(halfWidth * 0.35f, 0.25f, 0.85f);
        float pinX = center.x + dir * (halfWidth - frontInset);
        float carrySpeed = activeSpecialProfile.rushSpeed > 0.01f
            ? activeSpecialProfile.rushSpeed
            : 0f;

        // 拷贝后遍历，便于移除失效目标
        overlapCharacters.Clear();
        foreach (var target in rushCarriedTargets)
            overlapCharacters.Add(target);

        for (int i = 0; i < overlapCharacters.Count; i++)
        {
            var target = overlapCharacters[i];
            if (target == null || !target.CanReceiveHits)
            {
                EndRushCarry(target);
                continue;
            }

            PinTargetToRushFront(target, pinX, dir, carrySpeed);
        }
    }

    void BeginRushCarry(Character target)
    {
        if (target == null || rushCarriedTargets.Contains(target))
            return;

        rushCarriedTargets.Add(target);
        IgnorePlayerCollisionWith(target, ignore: true);
    }

    void EndRushCarry(Character target)
    {
        if (target == null)
        {
            rushCarriedTargets.Remove(null);
            return;
        }

        if (!rushCarriedTargets.Remove(target))
            return;

        IgnorePlayerCollisionWith(target, ignore: false);
    }

    void ClearRushCarryState()
    {
        if (rushCarriedTargets.Count > 0)
        {
            overlapCharacters.Clear();
            foreach (var target in rushCarriedTargets)
                overlapCharacters.Add(target);

            for (int i = 0; i < overlapCharacters.Count; i++)
                IgnorePlayerCollisionWith(overlapCharacters[i], ignore: false);
        }

        rushCarriedTargets.Clear();

        if (playerBodyCollider != null)
        {
            for (int i = 0; i < rushIgnoredEnemyColliders.Count; i++)
            {
                var enemyCol = rushIgnoredEnemyColliders[i];
                if (enemyCol != null)
                    Physics2D.IgnoreCollision(playerBodyCollider, enemyCol, false);
            }
        }

        rushIgnoredEnemyColliders.Clear();
    }

    void IgnorePlayerCollisionWith(Character target, bool ignore)
    {
        if (playerBodyCollider == null || target == null)
            return;

        var cols = target.GetComponentsInChildren<Collider2D>();
        for (int i = 0; i < cols.Length; i++)
        {
            var enemyCol = cols[i];
            if (enemyCol == null || !enemyCol.enabled || enemyCol.isTrigger)
                continue;

            Physics2D.IgnoreCollision(playerBodyCollider, enemyCol, ignore);
            if (ignore)
            {
                if (!rushIgnoredEnemyColliders.Contains(enemyCol))
                    rushIgnoredEnemyColliders.Add(enemyCol);
            }
            else
            {
                rushIgnoredEnemyColliders.Remove(enemyCol);
            }
        }
    }

    void PinTargetToRushFront(Character target, float pinX, float dir, float carrySpeed)
    {
        if (target == null)
            return;

        var targetRb = target.GetComponent<Rigidbody2D>();
        if (targetRb == null || !targetRb.simulated)
        {
            Vector3 p = target.transform.position;
            p.x = pinX;
            target.transform.position = p;
            return;
        }

        // 压掉向上弹射（身体碰撞 / 击退冲量常见副作用）
        float vy = targetRb.linearVelocity.y;
        if (vy > 0f)
            vy = 0f;

        Vector2 pinned = new Vector2(pinX, targetRb.position.y);
        if (targetRb.bodyType == RigidbodyType2D.Kinematic)
        {
            targetRb.MovePosition(pinned);
            return;
        }

        targetRb.linearVelocity = new Vector2(dir * Mathf.Max(0f, carrySpeed), vy);
        targetRb.MovePosition(pinned);
    }

    void ForceDisableAttackKnockback()
    {
        if (meleeAttack == null)
            return;

        if (!hasSavedAttackKnockback)
        {
            savedAttackKnockbackEnable = meleeAttack.enableKnockback;
            savedAttackKnockbackForce = meleeAttack.knockbackForce;
            savedAttackKnockbackDuration = meleeAttack.knockbackDuration;
            hasSavedAttackKnockback = true;
        }

        meleeAttack.enableKnockback = false;
        meleeAttack.knockbackForce = 0f;
    }

    void ApplyRushAttackKnockback(bool enable)
    {
        if (meleeAttack == null)
            return;

        if (!hasSavedAttackKnockback)
        {
            savedAttackKnockbackEnable = meleeAttack.enableKnockback;
            savedAttackKnockbackForce = meleeAttack.knockbackForce;
            savedAttackKnockbackDuration = meleeAttack.knockbackDuration;
            hasSavedAttackKnockback = true;
        }

        if (!enable)
        {
            meleeAttack.enableKnockback = false;
            meleeAttack.knockbackForce = 0f;
            return;
        }

        meleeAttack.enableKnockback = activeSpecialProfile.knockbackForce > 0.01f;
        meleeAttack.knockbackForce = activeSpecialProfile.knockbackForce;
        meleeAttack.knockbackDuration = Mathf.Max(0.05f, activeSpecialProfile.knockbackDuration);
    }

    void RestoreRushAttackKnockback()
    {
        if (meleeAttack == null || !hasSavedAttackKnockback)
            return;

        meleeAttack.enableKnockback = savedAttackKnockbackEnable;
        meleeAttack.knockbackForce = savedAttackKnockbackForce;
        meleeAttack.knockbackDuration = savedAttackKnockbackDuration;
        hasSavedAttackKnockback = false;
    }

    void EndRushSpecialState()
    {
        rushDashActive = false;
        ClearRushCarryState();
        RestoreRushAttackKnockback();
        ClearWhipKnockbacks();
    }

    void SyncDetectZoneAnchor()
    {
        if (detectZone == null)
            return;

        Transform anchor = playerAnim != null && playerAnim.IsCrouching ? meleePoint2 : meleePoint1;
        if (anchor == null)
            anchor = transform;

        var zoneTransform = detectZone.transform;
        if (zoneTransform.parent == anchor)
            return;

        zoneTransform.SetParent(anchor, false);
        zoneTransform.localPosition = Vector3.zero;
        zoneTransform.localRotation = Quaternion.identity;
        zoneTransform.localScale = Vector3.one;
    }

    void SyncHitboxAnchor()
    {
        if (meleeHitbox == null)
            return;

        Transform anchor = playerAnim != null && playerAnim.IsCrouching ? meleePoint2 : meleePoint1;
        if (anchor == null)
            anchor = transform;

        var hitboxTransform = meleeHitbox.transform;
        if (hitboxTransform.parent == anchor)
            return;

        hitboxTransform.SetParent(anchor, false);
        hitboxTransform.localPosition = Vector3.zero;
        hitboxTransform.localRotation = Quaternion.identity;
        hitboxTransform.localScale = Vector3.one;
    }

    void TryDoubleJump()
    {
        if (holdingAttackInputLock)
            return;

        if (playerMovement != null && playerMovement.IsActionLocked)
            return;

        if (playerAnim != null && playerAnim.IsDead)
            return;

        float height = doubleJumpHeight;
        if (usePlayerJumpHeight && playerMovement != null)
            height = playerMovement.jumpHeight;

        float gravityScale = rb.gravityScale;
        if (gravityScale < 0.01f && playerMovement != null)
            gravityScale = 3f; // 空中偶发 gravityScale≈0 时回退到 Prefab 默认值

        float gravity = Mathf.Abs(Physics2D.gravity.y * gravityScale);
        float jumpVelocity = Mathf.Sqrt(2f * gravity * height);

        Vector2 move = actions.Player.Move.ReadValue<Vector2>();
        bool hasHorizontal = Mathf.Abs(move.x) > inputThreshold;

        float velocityX = rb.linearVelocity.x;
        if (hasHorizontal && playerMovement != null)
        {
            float moveX = Mathf.Sign(move.x);
            velocityX = moveX * playerMovement.runSpeed;
            playerMovement.FaceTowardWorldX(transform.position.x + moveX);
        }

        rb.linearVelocity = new Vector2(velocityX, jumpVelocity);
        hasUsedDoubleJump = true;
        PlaySfx(doubleJumpEvent);
        PlaySessionRecorder.Instance?.RecordMeleeDoubleJump();

        if (fullBodyAnim != null)
            fullBodyAnim.PlayDoubleJumpAnim(hasHorizontal);
        else
            playerAnim?.PlayJumpAnim(hasHorizontal);

        NotifyCameraAirJump();
    }

    void NotifyCameraAirJump()
    {
        if (airborneYLock == null)
            airborneYLock = FindFirstObjectByType<CameraAirborneYLock>();

        airborneYLock?.NotifyAirJump();
    }

    void UpdateCommonActionSfx()
    {
        bool dead = playerAnim != null && playerAnim.IsDead;
        if (dead && !wasDeadForSfx)
            PlaySfx(dieEvent);
        wasDeadForSfx = dead;

        bool switching = playerAnim != null && playerAnim.IsSwitchingWeapon;
        if (switching && !wasSwitchingWeaponForSfx)
            PlaySfx(switchWeaponEvent);
        wasSwitchingWeaponForSfx = switching;
    }

    void PlaySfx(EventReference evt) => FmodAudio.Play(evt);

    void PlayAttackSfx(string label)
    {
        if (string.IsNullOrEmpty(label))
            return;

        FmodAudio.Play(attackEvent, meleeTypeParam, label);
    }

    void PlayMeleeActionSfx()
    {
        if (fullBodyAnim != null && fullBodyAnim.IsJumpDownAttack)
        {
            // 下砸起始音在 BeginJumpDownAttack 播放，避免重复
            return;
        }

        // 地面滑行四武器同一套动画，只播共用音效，避免叠上 rush/whip/buzzsaw 的 melee
        if (fullBodyAnim != null && fullBodyAnim.IsCrouchMelee)
        {
            PlayAttackSfx("DownMelee");
            return;
        }

        PlayAttackSfx(ResolveWeaponMeleeLabel(ResolveCurrentWeaponId()));
    }

    static string ResolveWeaponMeleeLabel(int weaponId) => weaponId switch
    {
        1 => "Rush",
        2 => "Whip",
        3 => "Buzzsaw",
        _ => "Idle",
    };

    static string ResolveWeaponSpecialLabel(int weaponId) => weaponId switch
    {
        1 => "RushSpecial",
        2 => "WhipSpecial",
        3 => "BuzzsawSpecial",
        _ => null,
    };

    void OnDrawGizmos()
    {
        if (!showAttackRangesInScene)
            return;

        // 编辑器 / Prefab：叠画全部武器判定；Play 中正在挥击时只画当前实际盒
        if (showAllAttackHitboxes && !IsCurrentSwingActiveForGizmo())
        {
            DrawAllAttackHitboxGizmos();
            return;
        }

        DrawRuntimeAttackHitboxGizmos();
    }

    bool IsCurrentSwingActiveForGizmo()
        => Application.isPlaying && playerAnim != null && playerAnim.IsMelee;

    void DrawAllAttackHitboxGizmos()
    {
        Matrix4x4 standMatrix = GetAnchorDrawMatrix(meleePoint1);
        Matrix4x4 crouchMatrix = GetAnchorDrawMatrix(meleePoint2 != null ? meleePoint2 : meleePoint1);

        // 索敌（以当前/默认武器 0）
        var detectProfile = FindProfile(0);
        DrawLocalBoxGizmo(
            GetDetectDrawMatrix(),
            detectProfile.detectOffset,
            detectProfile.detectSize,
            detectZoneGizmoColor,
            filled: true);
        DrawHitboxLabel(GetDetectDrawMatrix(), detectProfile.detectOffset, "Detect");

        // 下砸冲击（四武器共用）
        {
            Vector3 center = transform.position + (Vector3)jumpDownImpactOffset;
            DrawWireCircleGizmo(center, jumpDownImpactRadius, new Color(1f, 0.55f, 0.2f, 0.35f));
            DrawWorldLabel(center + Vector3.up * (jumpDownImpactRadius + 0.15f), "JumpDown");
        }

        // 蹲下滑铲：四武器共用周身盒（相对 MeleePoint2，不跟站立前方盒）
        {
            ResolveCrouchMeleeHitbox(out Vector2 crouchSize, out Vector2 crouchOffset);
            Color crouchColor = new Color(0.95f, 0.55f, 0.2f, 0.28f);
            DrawLocalBoxGizmo(crouchMatrix, crouchOffset, crouchSize, crouchColor, filled: false);
            DrawHitboxLabel(crouchMatrix, crouchOffset, "Crouch Slide");
        }

        int[] weaponIds = { 0, 1, 2, 3 };
        for (int i = 0; i < weaponIds.Length; i++)
        {
            int weaponId = weaponIds[i];
            var profile = FindProfile(weaponId);
            Color meleeColor = ResolveWeaponGizmoColor(weaponId, 0.28f);
            Color upColor = ResolveWeaponGizmoColor(weaponId, 0.22f);

            string weaponName = weaponId switch
            {
                1 => "Rush",
                2 => "Whip",
                3 => "Buzzsaw",
                _ => "Unarmed",
            };

            // 站立/空中普攻盒
            DrawLocalBoxGizmo(standMatrix, profile.hitboxOffset, profile.hitboxSize, meleeColor, filled: false);
            DrawHitboxLabel(standMatrix, profile.hitboxOffset, $"{weaponName} Melee");

            // 上攻
            {
                Vector2 upSize = profile.upHitboxSize.x > 0.01f ? profile.upHitboxSize : defaultUpHitboxSize;
                Vector2 upOffset = profile.upHitboxSize.x > 0.01f ? profile.upHitboxOffset : defaultUpHitboxOffset;
                DrawLocalBoxGizmo(standMatrix, upOffset, upSize, upColor, filled: false);
                DrawHitboxLabel(standMatrix, upOffset, $"{weaponName} Up");
            }

            if (!TryFindSpecialProfile(weaponId, out var special))
                continue;

            Color specialColor = ResolveWeaponGizmoColor(weaponId, 0.32f);
            specialColor.a = 0.35f;

            if (weaponId == 3 && special.outerRadius > 0.01f)
            {
                Vector3 center = standMatrix.MultiplyPoint3x4(special.hitboxOffset);
                DrawWireCircleGizmo(center, special.outerRadius, specialColor);
                DrawWireCircleGizmo(center, special.innerRadius, new Color(1f, 0.75f, 0.2f, 0.28f));
                DrawWorldLabel(center + Vector3.up * (special.outerRadius + 0.2f), $"{weaponName} Special");
            }
            else
            {
                DrawLocalBoxGizmo(standMatrix, special.hitboxOffset, special.hitboxSize, specialColor, filled: false);
                DrawHitboxLabel(standMatrix, special.hitboxOffset, $"{weaponName} Special");
            }

            if (weaponId == 2)
            {
                Vector2 rearOffset = ResolveWhipRearLocalOffsetForGizmo(special.hitboxOffset, standMatrix);
                DrawLocalBoxGizmo(
                    standMatrix,
                    rearOffset,
                    special.hitboxSize,
                    new Color(1f, 0.35f, 0.85f, 0.3f),
                    filled: false);
                DrawHitboxLabel(standMatrix, rearOffset, "Whip Rear");

                ResolveWhipUltimateUpHitbox(out Vector2 ultUpSize, out Vector2 ultUpOffset);
                ResolveWhipUltimateDownHitbox(out Vector2 ultDownSize, out Vector2 ultDownOffset);
                DrawLocalBoxGizmo(
                    standMatrix,
                    ultUpOffset,
                    ultUpSize,
                    new Color(0.45f, 0.85f, 1f, 0.28f),
                    filled: false);
                DrawHitboxLabel(standMatrix, ultUpOffset, "Whip Ult Up");
                DrawLocalBoxGizmo(
                    standMatrix,
                    ultDownOffset,
                    ultDownSize,
                    new Color(1f, 0.7f, 0.25f, 0.28f),
                    filled: false);
                DrawHitboxLabel(standMatrix, ultDownOffset, "Whip Ult Down");
            }

            if (weaponId == 1 && HasRushUltimateLaunchWindow())
            {
                DrawLocalBoxGizmo(
                    standMatrix,
                    rushUltimateLaunchHitboxOffset,
                    rushUltimateLaunchHitboxSize,
                    new Color(1f, 0.85f, 0.2f, 0.32f),
                    filled: false);
                DrawHitboxLabel(standMatrix, rushUltimateLaunchHitboxOffset, "Rush Ult Launch");
            }
        }
    }

    void DrawRuntimeAttackHitboxGizmos()
    {
        WeaponMeleeProfile drawProfile;
        int drawWeaponId = 0;
        if (Application.isPlaying)
        {
            drawProfile = activeProfile;
            drawWeaponId = activeWeaponId;
        }
        else
        {
            var wc = weaponController != null ? weaponController : GetComponent<PlayerWeaponController>();
            drawWeaponId = wc != null ? wc.CurrentWeaponId : 0;
            drawProfile = FindProfile(drawWeaponId);
        }

        DrawLocalBoxGizmo(
            GetDetectDrawMatrix(),
            drawProfile.detectOffset,
            drawProfile.detectSize,
            detectZoneGizmoColor,
            filled: true);

        {
            Vector3 center = transform.position + (Vector3)jumpDownImpactOffset;
            Color c = Application.isPlaying && jumpDownAttackActive
                ? new Color(1f, 0.35f, 0.1f, 0.45f)
                : new Color(1f, 0.55f, 0.2f, 0.18f);
            DrawWireCircleGizmo(center, jumpDownImpactRadius, c);
        }

        bool hitboxLive = Application.isPlaying && meleeHitbox != null && meleeHitbox.activeInHierarchy;
        Color hitColor = hitboxLive ? hitboxActiveGizmoColor : hitboxIdleGizmoColor;
        Matrix4x4 hitMatrix = GetHitboxDrawMatrix(meleeHitbox != null ? meleeHitbox.transform : null);

        bool drawSpecial = Application.isPlaying && IsCurrentSwingSpecial();
        bool drawUp = !drawSpecial && Application.isPlaying && IsCurrentSwingUpward();

        WeaponSpecialProfile specialDraw = default;
        bool hasSpecialDraw = false;
        if (Application.isPlaying && hasSpecialProfile)
        {
            specialDraw = activeSpecialProfile;
            hasSpecialDraw = true;
        }
        else if (!Application.isPlaying && TryFindSpecialProfile(drawWeaponId, out specialDraw))
        {
            hasSpecialDraw = true;
        }

        if (hasSpecialDraw && specialDraw.weaponId == 3 && specialDraw.outerRadius > 0.01f)
        {
            Vector3 center = Application.isPlaying
                ? (Vector3)ResolveSpecialCenter()
                : (meleePoint1 != null
                    ? meleePoint1.TransformPoint(specialDraw.hitboxOffset)
                    : transform.position + (Vector3)specialDraw.hitboxOffset);
            bool live = drawSpecial;
            Color outer = live
                ? new Color(1f, 0.25f, 0.2f, 0.35f)
                : new Color(1f, 0.45f, 0.9f, 0.18f);
            Color inner = live
                ? new Color(1f, 0.75f, 0.2f, 0.28f)
                : new Color(1f, 0.7f, 0.35f, 0.14f);
            DrawWireCircleGizmo(center, specialDraw.outerRadius, outer);
            DrawWireCircleGizmo(center, specialDraw.innerRadius, inner);
            if (drawSpecial)
                return;
        }

        Vector2 hitSize;
        Vector2 hitOffset;
        if (drawSpecial && hasSpecialProfile)
        {
            hitSize = activeSpecialProfile.hitboxSize;
            hitOffset = activeSpecialProfile.hitboxOffset;
        }
        else if (Application.isPlaying && IsCurrentSwingCrouchMelee())
        {
            ResolveCrouchMeleeHitbox(out hitSize, out hitOffset);
        }
        else
        {
            hitSize = drawUp
                ? (drawProfile.upHitboxSize.x > 0.01f ? drawProfile.upHitboxSize : defaultUpHitboxSize)
                : drawProfile.hitboxSize;
            hitOffset = drawUp
                ? (drawProfile.upHitboxSize.x > 0.01f ? drawProfile.upHitboxOffset : defaultUpHitboxOffset)
                : drawProfile.hitboxOffset;
        }

        DrawLocalBoxGizmo(hitMatrix, hitOffset, hitSize, hitColor, filled: true);
        DrawLocalBoxGizmo(
            hitMatrix,
            hitOffset,
            hitSize,
            new Color(hitColor.r, hitColor.g, hitColor.b, Mathf.Clamp01(hitColor.a + 0.35f)),
            filled: false);

        if (hasSpecialDraw && specialDraw.weaponId == 2)
        {
            Vector2 rearOffset = ResolveWhipRearLocalOffset(specialDraw.hitboxOffset);
            Color rearColor = drawSpecial
                ? new Color(1f, 0.35f, 0.85f, 0.35f)
                : new Color(1f, 0.45f, 0.9f, 0.18f);
            DrawLocalBoxGizmo(hitMatrix, rearOffset, specialDraw.hitboxSize, rearColor, filled: false);

            bool drawUltVertical = !Application.isPlaying || IsCurrentSwingUltimate();
            if (drawUltVertical)
            {
                ResolveWhipUltimateUpHitbox(out Vector2 ultUpSize, out Vector2 ultUpOffset);
                ResolveWhipUltimateDownHitbox(out Vector2 ultDownSize, out Vector2 ultDownOffset);
                Color upColor = drawSpecial
                    ? new Color(0.45f, 0.85f, 1f, 0.35f)
                    : new Color(0.45f, 0.85f, 1f, 0.18f);
                Color downColor = drawSpecial
                    ? new Color(1f, 0.7f, 0.25f, 0.35f)
                    : new Color(1f, 0.7f, 0.25f, 0.18f);
                DrawLocalBoxGizmo(hitMatrix, ultUpOffset, ultUpSize, upColor, filled: false);
                DrawLocalBoxGizmo(hitMatrix, ultDownOffset, ultDownSize, downColor, filled: false);
            }
        }

        if (!drawUp && !drawSpecial && !(Application.isPlaying && IsCurrentSwingCrouchMelee()))
        {
            Vector2 upSize = drawProfile.upHitboxSize.x > 0.01f ? drawProfile.upHitboxSize : defaultUpHitboxSize;
            Vector2 upOffset = drawProfile.upHitboxSize.x > 0.01f ? drawProfile.upHitboxOffset : defaultUpHitboxOffset;
            DrawLocalBoxGizmo(
                hitMatrix,
                upOffset,
                upSize,
                new Color(0.4f, 1f, 0.5f, 0.2f),
                filled: false);
        }

        if (hasSpecialDraw && !drawSpecial && specialDraw.weaponId == 1)
        {
            DrawLocalBoxGizmo(
                hitMatrix,
                specialDraw.hitboxOffset,
                specialDraw.hitboxSize,
                new Color(1f, 0.45f, 0.9f, 0.22f),
                filled: false);
        }

        if (hasSpecialDraw && specialDraw.weaponId == 1 && HasRushUltimateLaunchWindow())
        {
            bool launchLive = drawSpecial
                && Application.isPlaying
                && IsCurrentSwingUltimate();
            Color launchColor = launchLive
                ? new Color(1f, 0.85f, 0.2f, 0.4f)
                : new Color(1f, 0.8f, 0.25f, 0.18f);
            DrawLocalBoxGizmo(
                hitMatrix,
                rushUltimateLaunchHitboxOffset,
                rushUltimateLaunchHitboxSize,
                launchColor,
                filled: false);
        }
    }

    static Color ResolveWeaponGizmoColor(int weaponId, float alpha) => weaponId switch
    {
        1 => new Color(1f, 0.35f, 0.2f, alpha),
        2 => new Color(0.95f, 0.35f, 1f, alpha),
        3 => new Color(0.35f, 1f, 0.45f, alpha),
        _ => new Color(0.25f, 0.85f, 1f, alpha),
    };

    Matrix4x4 GetAnchorDrawMatrix(Transform anchor)
    {
        if (anchor == null)
            anchor = transform;
        return anchor.localToWorldMatrix;
    }

    /// <summary>编辑器预览用：不依赖 meleeHitbox 激活状态。</summary>
    Vector2 ResolveWhipRearLocalOffsetForGizmo(Vector2 frontLocal, Matrix4x4 hitMatrix)
    {
        Vector3 frontWorld = hitMatrix.MultiplyPoint3x4(frontLocal);
        Vector3 rearWorld = new Vector3(2f * transform.position.x - frontWorld.x, frontWorld.y, frontWorld.z);
        return hitMatrix.inverse.MultiplyPoint3x4(rearWorld);
    }

    void DrawHitboxLabel(Matrix4x4 localToWorld, Vector2 offset, string label)
    {
        if (!showHitboxLabels)
            return;
        Vector3 world = localToWorld.MultiplyPoint3x4(offset);
        DrawWorldLabel(world, label);
    }

    void DrawWorldLabel(Vector3 world, string label)
    {
#if UNITY_EDITOR
        if (!showHitboxLabels || string.IsNullOrEmpty(label))
            return;
        UnityEditor.Handles.color = Color.white;
        UnityEditor.Handles.Label(world + Vector3.up * 0.08f, label);
#endif
    }

    Matrix4x4 GetDetectDrawMatrix()
    {
        if (detectZone != null)
            return detectZone.transform.localToWorldMatrix;

        Transform anchor = meleePoint1 != null ? meleePoint1 : transform;
        return anchor.localToWorldMatrix;
    }

    Matrix4x4 GetHitboxDrawMatrix(Transform hitboxTransform)
    {
        // 攻击中已挂在正确锚点；未激活时仍可能留在旧父节点，按当前蹲姿估算锚点
        if (meleeHitbox != null && meleeHitbox.activeInHierarchy)
            return hitboxTransform.localToWorldMatrix;

        Transform anchor = null;
        if (Application.isPlaying && playerAnim != null)
            anchor = playerAnim.IsCrouching ? meleePoint2 : meleePoint1;
        else if (!Application.isPlaying)
            anchor = meleePoint1 != null ? meleePoint1 : meleePoint2;

        if (anchor == null)
            anchor = hitboxTransform != null ? hitboxTransform.parent : transform;
        if (anchor == null)
            anchor = transform;

        return anchor.localToWorldMatrix;
    }

    static void DrawLocalBoxGizmo(Matrix4x4 localToWorld, Vector2 offset, Vector2 size, Color color, bool filled)
    {
        Matrix4x4 prev = Gizmos.matrix;
        Color prevColor = Gizmos.color;

        Gizmos.matrix = localToWorld;
        Gizmos.color = color;
        if (filled)
            Gizmos.DrawCube(offset, size);
        else
            Gizmos.DrawWireCube(offset, size);

        Gizmos.matrix = prev;
        Gizmos.color = prevColor;
    }

    static void DrawWireCircleGizmo(Vector3 center, float radius, Color color)
    {
        if (radius <= 0.01f)
            return;

        Color prev = Gizmos.color;
        Gizmos.color = color;
        const int segments = 48;
        Vector3 prevPoint = center + new Vector3(radius, 0f, 0f);
        for (int i = 1; i <= segments; i++)
        {
            float ang = (i / (float)segments) * Mathf.PI * 2f;
            Vector3 next = center + new Vector3(Mathf.Cos(ang) * radius, Mathf.Sin(ang) * radius, 0f);
            Gizmos.DrawLine(prevPoint, next);
            prevPoint = next;
        }

        Gizmos.color = prev;
    }
}
