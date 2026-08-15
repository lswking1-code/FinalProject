using System.Collections.Generic;
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
        [Range(0f, 1f)] public float hitStart;
        [Range(0f, 1f)] public float hitEnd;
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
        [Tooltip("特技 Hitbox 本地偏移（面向右为正 X）")]
        public Vector2 hitboxOffset;
        [Tooltip("0 = 不限制命中数；>0 为单次特技最多命中敌人数")]
        public int maxTargets;
        [Range(0f, 1f)] public float hitStart;
        [Range(0f, 1f)] public float hitEnd;

        [Header("Whip · 后方追加判定")]
        [Tooltip("后方追加判定开始（归一化 0-1）。whip_special 后挥约在 0.67，建议 0.55 起")]
        [Range(0f, 1f)] public float rearHitStart;
        [Tooltip("后方追加判定结束（归一化 0-1）。建议覆盖到 0.95，不要用片长秒数")]
        [Range(0f, 1f)] public float rearHitEnd;

        [Header("Buzzsaw · 双层圆形判定")]
        [Tooltip("外圈半径（高伤害，使用 damage）")]
        public float outerRadius;
        [Tooltip("内圈半径（低伤害，使用 innerDamage）")]
        public float innerRadius;
        [Tooltip("内圈伤害（应低于 damage）")]
        public int innerDamage;

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

    [System.Serializable]
    public struct WeaponActionSfx
    {
        public int weaponId;
        [Tooltip("站立/默认近战")]
        public AudioClip melee;
        [Tooltip("空中近战；空则回退 melee")]
        public AudioClip airMelee;
        [Tooltip("向上近战；空则回退 melee")]
        public AudioClip upMelee;
        [Tooltip("蹲攻；空则回退 melee")]
        public AudioClip crouchMelee;
        [Tooltip("特技 Ability1")]
        public AudioClip special;
    }

    [Header("二段跳")]
    [Tooltip("二段跳目标高度；若勾选下方选项则改用 PlayerMovement.jumpHeight")]
    [SerializeField] float doubleJumpHeight = 4.5f;
    [SerializeField] bool usePlayerJumpHeight = true;
    [Tooltip("摇杆死区，与 PlayerMovement 默认一致")]
    [SerializeField] float inputThreshold = 0.5f;

    [Header("普通攻击（J / Attack）")]
    [SerializeField] int meleeDamage = 40;
    [SerializeField] float hitStart = 0.15f;
    [SerializeField] float hitEnd = 0.45f;
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
            hitboxSize = new Vector2(1.2f, 1f), hitboxOffset = Vector2.zero,
            upHitboxSize = new Vector2(1.3f, 1.8f), upHitboxOffset = new Vector2(0f, 1.2f),
            detectSize = new Vector2(2f, 2f), detectOffset = new Vector2(0.5f, 0f),
            maxTargets = 0, hitStart = 0.15f, hitEnd = 0.45f,
        },
        new WeaponMeleeProfile
        {
            weaponId = 1, damage = 55,
            hitboxSize = new Vector2(2.6f, 1.25f), hitboxOffset = new Vector2(1.3f, 0f),
            upHitboxSize = new Vector2(1.5f, 2.6f), upHitboxOffset = new Vector2(0f, 1.5f),
            detectSize = new Vector2(3.2f, 2f), detectOffset = new Vector2(1.4f, 0f),
            maxTargets = 0, hitStart = 0.12f, hitEnd = 0.5f,
        },
        new WeaponMeleeProfile
        {
            weaponId = 2, damage = 45,
            hitboxSize = new Vector2(3.8f, 0.4f), hitboxOffset = new Vector2(1.9f, 0f),
            upHitboxSize = new Vector2(0.45f, 3.6f), upHitboxOffset = new Vector2(0f, 2.0f),
            detectSize = new Vector2(4.2f, 1.2f), detectOffset = new Vector2(2.0f, 0f),
            maxTargets = 0, hitStart = 0.1f, hitEnd = 0.55f,
        },
        new WeaponMeleeProfile
        {
            weaponId = 3, damage = 70,
            hitboxSize = new Vector2(1.1f, 1.1f), hitboxOffset = new Vector2(0.55f, 0f),
            upHitboxSize = new Vector2(1.2f, 1.4f), upHitboxOffset = new Vector2(0f, 1.0f),
            detectSize = new Vector2(1.8f, 1.6f), detectOffset = new Vector2(0.7f, 0f),
            maxTargets = 2, hitStart = 0.15f, hitEnd = 0.45f,
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
            maxTargets = 0, hitStart = 0.15f, hitEnd = 0.6f,
            rushSpeed = 18f, rushStart = 0.1f, rushEnd = 0.55f,
            rushPushSpeed = 20f,
            knockbackForce = 14f, knockbackDuration = 0.22f,
        },
        new WeaponSpecialProfile
        {
            weaponId = 2, damage = 70,
            hitboxSize = new Vector2(4.5f, 0.6f), hitboxOffset = new Vector2(2.2f, 0f),
            maxTargets = 0, hitStart = 0.15f, hitEnd = 0.55f,
            rearHitStart = 0.55f, rearHitEnd = 0.95f,
            knockbackForce = 16f, knockbackDuration = 0.28f,
        },
        new WeaponSpecialProfile
        {
            weaponId = 3, damage = 100, innerDamage = 55,
            hitboxSize = new Vector2(2.0f, 1.6f), hitboxOffset = Vector2.zero,
            maxTargets = 0, hitStart = 0.2f, hitEnd = 0.7f,
            outerRadius = 3.2f, innerRadius = 1.5f,
        },
    };

    [Header("大招（I / Ability2 · 复制特技，消耗能量）")]
    [Tooltip("发动大招消耗的 AbilityPower；0 表示不消耗。Melee_Player 默认上限 100")]
    [SerializeField] float ultimateAbilityPowerCost = 50f;
    [Tooltip("大招伤害 = 特技伤害 × 该倍率")]
    [SerializeField] float ultimateDamageMultiplier = 2f;

    [Header("短距冲刺（CrouchMelee · 无推怪）")]
    [Tooltip("蹲攻短距冲刺速度；应明显短于 rush_special")]
    [SerializeField] float shortMeleeDashSpeed = 10f;
    [Range(0f, 1f)] [SerializeField] float shortMeleeDashStart = 0.08f;
    [Range(0f, 1f)] [SerializeField] float shortMeleeDashEnd = 0.38f;

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
    [Tooltip("留空则运行时自动挂 AudioSource（2D）")]
    [SerializeField] AudioSource sfxSource;
    [Range(0f, 1f)] [SerializeField] float sfxVolume = 1f;
    [Tooltip("按武器 ID 配置近战/特技音效；未填的动作会回退")]
    [SerializeField] WeaponActionSfx[] weaponSfxProfiles =
    {
        new WeaponActionSfx { weaponId = 0 },
        new WeaponActionSfx { weaponId = 1 },
        new WeaponActionSfx { weaponId = 2 },
        new WeaponActionSfx { weaponId = 3 },
    };
    [SerializeField] AudioClip fallbackMeleeSfx;
    [Tooltip("地面滑行（CrouchMelee / default_down_melee）四武器共用；不走分武器 melee/crouchMelee")]
    [SerializeField] AudioClip downMeleeSfx;
    [Tooltip("空中落地砸地起始（四武器共用；不走分武器 melee/airMelee）")]
    [SerializeField] AudioClip jumpDownStartSfx;
    [Tooltip("空中落地砸地落地冲击（四武器共用）")]
    [SerializeField] AudioClip jumpDownImpactSfx;
    [SerializeField] AudioClip jumpSfx;
    [SerializeField] AudioClip doubleJumpSfx;
    [SerializeField] AudioClip switchWeaponSfx;
    [SerializeField] AudioClip dieSfx;

    [Header("向上攻击默认判定（剖面 upHitbox 未填时回退）")]
    [SerializeField] Vector2 defaultUpHitboxSize = new Vector2(1.3f, 1.8f);
    [SerializeField] Vector2 defaultUpHitboxOffset = new Vector2(0f, 1.2f);

    [Header("攻击范围可视化（Scene View）")]
    [Tooltip("运行中/编辑器 Scene 视图始终显示判定框，无需选中角色")]
    [SerializeField] bool showAttackRangesInScene = true;
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
    readonly HashSet<IHitCountable> swingHitCountables = new();
    readonly List<Character> overlapCharacters = new();
    readonly Collider2D[] overlapBuffer = new Collider2D[48];

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
        EnsureSfxSource();

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

        // 攻击锁期间关掉了 PlayerMovement，需自行推进空中/近战完成检测
        if (holdingAttackInputLock)
            MaintainAttackLockAnimation();

        if (actions.Player.Jump.WasPressedThisFrame())
            jumpPressedThisFrame = true;

        TryStartMeleeAttack();
        TryStartSpecialAttack();
        TryStartUltimateAttack();
        UpdateMeleeHitbox();
        UpdateCommonActionSfx();
    }

    void LateUpdate()
    {
        SyncDetectZoneAnchor();
        // 放在 LateUpdate：盖过敌人 FixedUpdate 里写回的 AI 速度
        ApplyWhipKnockbackVelocities();
    }

    void FixedUpdate()
    {
        UpdateJumpDownAttack();
        UpdateDashAttacks();
        ApplyWhipKnockbackVelocities();

        // 一段跳由 PlayerMovement 执行；此处只补音效
        if (playerMovement != null && playerMovement.DidGroundJumpThisFixedUpdate)
            PlaySfx(jumpSfx);

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

        // weapon 0：伤害与命中窗继续跟上方「普通攻击」字段，保持照旧可调
        if (weaponId == 0)
        {
            profile.damage = meleeDamage;
            profile.hitStart = hitStart;
            profile.hitEnd = hitEnd;
        }
        else if (profile.damage <= 0)
        {
            profile.damage = meleeDamage;
        }

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

        return profile;
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
                profile.rearHitStart = 0.55f;
                profile.rearHitEnd = 0.95f;
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
                profile.rushStart = 0.1f;
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
                    hitboxSize = new Vector2(2.6f, 1.25f), hitboxOffset = new Vector2(1.3f, 0f),
                    upHitboxSize = new Vector2(1.5f, 2.6f), upHitboxOffset = new Vector2(0f, 1.5f),
                    detectSize = new Vector2(3.2f, 2f), detectOffset = new Vector2(1.4f, 0f),
                    maxTargets = 0, hitStart = 0.12f, hitEnd = 0.5f,
                };
            case 2:
                return new WeaponMeleeProfile
                {
                    weaponId = 2, damage = 45,
                    hitboxSize = new Vector2(3.8f, 0.4f), hitboxOffset = new Vector2(1.9f, 0f),
                    upHitboxSize = new Vector2(0.45f, 3.6f), upHitboxOffset = new Vector2(0f, 2.0f),
                    detectSize = new Vector2(4.2f, 1.2f), detectOffset = new Vector2(2.0f, 0f),
                    maxTargets = 0, hitStart = 0.1f, hitEnd = 0.55f,
                };
            case 3:
                return new WeaponMeleeProfile
                {
                    weaponId = 3, damage = 70,
                    hitboxSize = new Vector2(1.1f, 1.1f), hitboxOffset = new Vector2(0.55f, 0f),
                    upHitboxSize = new Vector2(1.2f, 1.4f), upHitboxOffset = new Vector2(0f, 1.0f),
                    detectSize = new Vector2(1.8f, 1.6f), detectOffset = new Vector2(0.7f, 0f),
                    maxTargets = 2, hitStart = 0.15f, hitEnd = 0.45f,
                };
            default:
                return new WeaponMeleeProfile
                {
                    weaponId = 0, damage = 40,
                    hitboxSize = new Vector2(1.2f, 1f), hitboxOffset = Vector2.zero,
                    upHitboxSize = new Vector2(1.3f, 1.8f), upHitboxOffset = new Vector2(0f, 1.2f),
                    detectSize = new Vector2(2f, 2f), detectOffset = new Vector2(0.5f, 0f),
                    maxTargets = 0, hitStart = 0.15f, hitEnd = 0.45f,
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
                : (activeProfile.damage > 0 ? activeProfile.damage : meleeDamage);
            int maxTargets = special && hasSpecialProfile
                ? activeSpecialProfile.maxTargets
                : activeProfile.maxTargets;

            // Whip 前后双段 / Buzzsaw 双层圆 一律手动结算，避免 Trigger 重复或形状不符
            bool manualSpecial = special && hasSpecialProfile
                && (activeSpecialProfile.weaponId == 2 || activeSpecialProfile.weaponId == 3);

            meleeAttack.damage = damage;
            meleeAttack.attackType = AttackType.Melee;
            meleeAttack.ignoreTag = "Player";
            meleeAttack.enabled = !manualSpecial && maxTargets <= 0;

            if (special && hasSpecialProfile && activeSpecialProfile.weaponId == 1)
                ApplyRushAttackKnockback(true);
            else
                RestoreRushAttackKnockback();
        }
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
        return activeProfile.damage > 0 ? activeProfile.damage : meleeDamage;
    }

    void TryStartMeleeAttack()
    {
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
        swingHitCountables.Clear();
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
            PlayMeleeActionSfx();
            BeginAttackInputLock();
        }
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

        if (playerMovement != null && playerMovement.IsActionLocked)
            return;

        if (playerAnim == null || playerAnim.IsDead || playerAnim.IsSwitchingWeapon)
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
        else
        {
            int specialCost = ResolveSpecialAmmoCost(weaponId);
            if (!HasWeaponAmmo(weaponId, specialCost))
                return;
        }

        if (detectZone != null && detectZone.HasValidTarget)
        {
            var target = detectZone.GetNearestTarget(transform.position);
            if (target != null && playerMovement != null)
                playerMovement.FaceTowardWorldX(target.position.x);
        }

        swingHitTargets.Clear();
        specialRearHitTargets.Clear();
        swingHitCountables.Clear();
        playerAnim.InterruptTurn();

        bool played = ultimate
            ? fullBodyAnim != null && fullBodyAnim.TryPlayUltimateAnim()
            : playerAnim.TryPlaySpecialAnim();
        if (!played)
            return;

        if (ultimate)
        {
            if (ultimateAbilityPowerCost > 0f && selfCharacter != null)
                selfCharacter.DrainAbilityPower(ultimateAbilityPowerCost);
        }
        else
        {
            TryConsumeWeaponAmmo(weaponId, ResolveSpecialAmmoCost(weaponId));
        }

        ApplyActiveProfileToColliders();
        PlaySfx(ResolveWeaponSpecialSfx(weaponId));
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

        if (special && hasSpecialProfile && activeSpecialProfile.weaponId == 2)
        {
            UpdateWhipSpecialHits();
            return;
        }

        if (special && hasSpecialProfile && activeSpecialProfile.weaponId == 1)
            ApplyRushAttackKnockback(true);

        ApplyHitboxShape(upward: !special && IsCurrentSwingUpward(), special: special);

        float windowStart = special && hasSpecialProfile ? activeSpecialProfile.hitStart : activeProfile.hitStart;
        float windowEnd = special && hasSpecialProfile ? activeSpecialProfile.hitEnd : activeProfile.hitEnd;
        int maxTargets = special && hasSpecialProfile ? activeSpecialProfile.maxTargets : activeProfile.maxTargets;

        if (meleeAttack != null)
        {
            int damage = special && hasSpecialProfile
                ? ResolveSpecialSwingDamage()
                : (activeProfile.damage > 0 ? activeProfile.damage : meleeDamage);
            meleeAttack.damage = damage;
            meleeAttack.enabled = maxTargets <= 0;
        }

        bool inHitWindow = playerAnim.TryGetMeleeAnimProgress(out float t)
            && t >= windowStart && t <= windowEnd;

        if (inHitWindow)
        {
            if (!meleeHitbox.activeSelf)
                meleeHitbox.SetActive(true);

            if (maxTargets > 0)
                ProcessLimitedHitTargets(maxTargets);
        }
        else if (meleeHitbox.activeSelf)
        {
            meleeHitbox.SetActive(false);
        }
    }

    void UpdateWhipSpecialHits()
    {
        ApplyRushAttackKnockback(true);

        if (meleeAttack != null)
        {
            meleeAttack.damage = ResolveSpecialSwingDamage();
            meleeAttack.enabled = false;
        }

        if (!playerAnim.TryGetMeleeAnimProgress(out float t))
        {
            ApplyHitboxShape(upward: false, special: true);
            if (meleeHitbox.activeSelf)
                meleeHitbox.SetActive(false);
            return;
        }

        bool frontWindow = t >= activeSpecialProfile.hitStart && t <= activeSpecialProfile.hitEnd;
        bool rearWindow = t >= activeSpecialProfile.rearHitStart && t <= activeSpecialProfile.rearHitEnd;
        Vector2 rearOffset = ResolveWhipRearLocalOffset(activeSpecialProfile.hitboxOffset);

        // 仅后段时把可见盒也切到镜像位置，避免 Scene 里看起来永远只有前方
        if (meleeHitboxCollider != null)
        {
            meleeHitboxCollider.size = activeSpecialProfile.hitboxSize;
            meleeHitboxCollider.offset = rearWindow && !frontWindow
                ? rearOffset
                : activeSpecialProfile.hitboxOffset;
        }

        if (frontWindow || rearWindow)
        {
            if (!meleeHitbox.activeSelf)
                meleeHitbox.SetActive(true);

            Physics2D.SyncTransforms();

            if (frontWindow)
            {
                // 前方段：沿面朝方向击退（推离玩家）
                ProcessSpecialBoxHits(
                    activeSpecialProfile.hitboxOffset,
                    activeSpecialProfile.hitboxSize,
                    ResolveSpecialSwingDamage(),
                    swingHitTargets,
                    activeSpecialProfile.maxTargets,
                    knockbackSign: 1f);
            }

            if (rearWindow)
            {
                // 后方段：相对角色 pivot 镜像前方盒，朝背后击退
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

    void UpdateBuzzsawSpecialHits()
    {
        if (meleeHitbox != null && meleeHitbox.activeSelf)
            meleeHitbox.SetActive(false);

        if (meleeAttack != null)
            meleeAttack.enabled = false;

        if (!playerAnim.TryGetMeleeAnimProgress(out float t))
            return;

        if (t < activeSpecialProfile.hitStart || t > activeSpecialProfile.hitEnd)
            return;

        ProcessBuzzsawCircleHits();
    }

    void ProcessBuzzsawCircleHits()
    {
        if (meleeAttack == null)
            return;

        Vector2 center = ResolveSpecialCenter();
        float outer = activeSpecialProfile.outerRadius;
        float inner = activeSpecialProfile.innerRadius;
        int outerDamage = ResolveSpecialSwingDamage();
        int innerDamage = ResolveSpecialInnerDamage();
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
            meleeAttack.damage = distSq <= innerSq ? innerDamage : outerDamage;
            if (target.TakeDamage(meleeAttack))
            {
                meleeAttack.RaiseHitCameraShakeIfEnabled();
                LogSkillHit(target, meleeAttack.damage, distSq <= innerSq ? "内圈" : "外圈");
            }
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
        float knockbackSign = 0f)
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

            bool damaged = target.TakeDamage(meleeAttack);
            if (damaged)
            {
                meleeAttack.RaiseHitCameraShakeIfEnabled();
                // 敌人 AI 会盖掉 Character.AddForce，改由 Bob 持续推一段距离
                if (registerWhipPush)
                    RegisterWhipKnockback(target, knockbackSign);
                LogSkillHit(target, damage, knockbackSign < 0f ? "后方" : null);
            }

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
        return anchor.position;
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
        if (target.currentHealth <= 0f)
            return false;

        return true;
    }

    void TryRegisterHitCountable(Collider2D col)
    {
        if (col == null || meleeAttack == null)
            return;

        if (col.transform == transform || col.transform.IsChildOf(transform))
            return;

        var hitCountable = col.GetComponentInParent<IHitCountable>();
        if (hitCountable == null || swingHitCountables.Contains(hitCountable))
            return;

        if (hitCountable.RegisterHit(meleeAttack))
            swingHitCountables.Add(hitCountable);
    }

    void ProcessLimitedHitTargets(int maxTargets)
    {
        if (meleeHitboxCollider == null)
            return;

        ProcessSpecialBoxHits(
            meleeHitboxCollider.offset,
            meleeHitboxCollider.size,
            meleeAttack != null ? meleeAttack.damage : meleeDamage,
            swingHitTargets,
            maxTargets);
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
        PlaySfx(jumpDownStartSfx);

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
        PlaySfx(jumpDownImpactSfx);

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
        bool previousEnabled = meleeAttack.enabled;
        meleeAttack.damage = Mathf.Max(1, jumpDownImpactDamage);
        meleeAttack.enabled = false;

        swingHitTargets.Clear();
        for (int i = 0; i < count; i++)
        {
            if (!TryResolveAttackTarget(overlapBuffer[i], swingHitTargets, out Character target))
                continue;

            if (target.TakeDamage(meleeAttack))
            {
                meleeAttack.RaiseHitCameraShakeIfEnabled();
                LogSkillHit(target, meleeAttack.damage);
            }
            swingHitTargets.Add(target);
        }

        meleeAttack.damage = previousDamage;
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
        if (meleeHitbox == null)
            return;

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

        int count = Physics2D.OverlapBoxNonAlloc(center, worldSize, angle, overlapBuffer);
        if (count <= 0)
            return;

        float pushSpeed = activeSpecialProfile.rushPushSpeed > 0.01f
            ? activeSpecialProfile.rushPushSpeed
            : activeSpecialProfile.rushSpeed;
        float step = pushSpeed * Time.fixedDeltaTime;
        Vector2 delta = new Vector2(dir * step, 0f);

        for (int i = 0; i < count; i++)
        {
            var col = overlapBuffer[i];
            if (col == null)
                continue;
            if (col.transform == transform || col.transform.IsChildOf(transform))
                continue;

            var target = col.GetComponentInParent<Character>();
            if (target == null || target == selfCharacter || target.currentHealth <= 0f)
                continue;
            if (meleeAttack != null
                && !string.IsNullOrEmpty(meleeAttack.ignoreTag)
                && target.CompareTag(meleeAttack.ignoreTag))
                continue;

            var targetRb = target.GetComponent<Rigidbody2D>();
            if (targetRb == null || !targetRb.simulated)
            {
                target.transform.position += (Vector3)delta;
                continue;
            }

            if (targetRb.bodyType == RigidbodyType2D.Kinematic)
            {
                targetRb.MovePosition(targetRb.position + delta);
            }
            else
            {
                // 覆盖敌人本帧 AI 速度，使其随冲刺被推走
                targetRb.linearVelocity = new Vector2(dir * pushSpeed, targetRb.linearVelocity.y);
            }
        }
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
            RestoreRushAttackKnockback();
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
            velocityX = Mathf.Sign(move.x) * playerMovement.runSpeed;

        rb.linearVelocity = new Vector2(velocityX, jumpVelocity);
        hasUsedDoubleJump = true;
        PlaySfx(doubleJumpSfx);

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

    void EnsureSfxSource()
    {
        if (sfxSource == null)
            sfxSource = GetComponent<AudioSource>();

        if (sfxSource == null)
            sfxSource = gameObject.AddComponent<AudioSource>();

        sfxSource.playOnAwake = false;
        sfxSource.loop = false;
        sfxSource.spatialBlend = 0f;
    }

    void UpdateCommonActionSfx()
    {
        bool dead = playerAnim != null && playerAnim.IsDead;
        if (dead && !wasDeadForSfx)
            PlaySfx(dieSfx);
        wasDeadForSfx = dead;

        bool switching = playerAnim != null && playerAnim.IsSwitchingWeapon;
        if (switching && !wasSwitchingWeaponForSfx)
            PlaySfx(switchWeaponSfx);
        wasSwitchingWeaponForSfx = switching;
    }

    void PlaySfx(AudioClip clip)
    {
        if (clip == null)
            return;

        EnsureSfxSource();
        sfxSource.PlayOneShot(clip, sfxVolume);
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
            PlaySfx(downMeleeSfx != null ? downMeleeSfx : fallbackMeleeSfx);
            return;
        }

        int weaponId = ResolveCurrentWeaponId();
        WeaponActionSfx profile = FindWeaponSfx(weaponId);

        AudioClip clip;
        if (fullBodyAnim != null && fullBodyAnim.IsUpwardMelee)
            clip = profile.upMelee != null ? profile.upMelee : profile.melee;
        else if (physicsCheck != null && !physicsCheck.isGround)
            clip = profile.airMelee != null ? profile.airMelee : profile.melee;
        else
            clip = profile.melee;

        if (clip == null)
            clip = fallbackMeleeSfx;

        PlaySfx(clip);
    }

    AudioClip ResolveWeaponSpecialSfx(int weaponId)
    {
        WeaponActionSfx profile = FindWeaponSfx(weaponId);
        return profile.special;
    }

    WeaponActionSfx FindWeaponSfx(int weaponId)
    {
        if (weaponSfxProfiles != null)
        {
            for (int i = 0; i < weaponSfxProfiles.Length; i++)
            {
                if (weaponSfxProfiles[i].weaponId == weaponId)
                    return weaponSfxProfiles[i];
            }
        }

        return default;
    }

    void OnDrawGizmos()
    {
        if (!showAttackRangesInScene)
            return;

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

        // JumpDownAttack 落地冲击预览
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

        // Buzzsaw：双层圆预览；特技播放中只画圆、不画旧盒
        if (hasSpecialDraw && specialDraw.weaponId == 3 && specialDraw.outerRadius > 0.01f)
        {
            Vector3 center = Application.isPlaying
                ? (Vector3)ResolveSpecialCenter()
                : (meleePoint1 != null ? meleePoint1.position : transform.position);
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

        // Whip 特技：额外画出绕角色 pivot 镜像的后方盒
        if (hasSpecialDraw && specialDraw.weaponId == 2)
        {
            Vector2 rearOffset = ResolveWhipRearLocalOffset(specialDraw.hitboxOffset);
            Color rearColor = drawSpecial
                ? new Color(1f, 0.35f, 0.85f, 0.35f)
                : new Color(1f, 0.45f, 0.9f, 0.18f);
            DrawLocalBoxGizmo(hitMatrix, rearOffset, specialDraw.hitboxSize, rearColor, filled: false);
        }

        // 非向上挥击时额外用半透明线框标出上方判定（空手无上攻，不画）
        if (!drawUp && !drawSpecial && drawWeaponId != 0)
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

        // Rush / 其他：非播放中时粉线框预览特技盒
        if (hasSpecialDraw && !drawSpecial && specialDraw.weaponId == 1)
        {
            DrawLocalBoxGizmo(
                hitMatrix,
                specialDraw.hitboxOffset,
                specialDraw.hitboxSize,
                new Color(1f, 0.45f, 0.9f, 0.22f),
                filled: false);
        }
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
