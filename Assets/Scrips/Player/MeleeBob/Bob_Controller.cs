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
        [Tooltip("后方追加判定开始（归一化动画时间）；与前方盒同尺寸，X 镜像")]
        [Range(0f, 1f)] public float rearHitStart;
        [Tooltip("后方追加判定结束")]
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
        [Tooltip("命中时写入 Attack.knockbackForce（不改 Attack.cs）")]
        public float knockbackForce;
        [Tooltip("命中时写入 Attack.knockbackDuration")]
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

    [Header("特技（U / Ability1 · 仅武器 1/2/3）")]
    [Tooltip("发动特技消耗的弹药数；武器 1/2/3 分别扣 BulletS/M/L；0 表示不消耗")]
    [SerializeField] int specialAmmoCost = 1;
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
            rearHitStart = 0.5f, rearHitEnd = 0.7f,
        },
        new WeaponSpecialProfile
        {
            weaponId = 3, damage = 100, innerDamage = 55,
            hitboxSize = new Vector2(2.0f, 1.6f), hitboxOffset = Vector2.zero,
            maxTargets = 0, hitStart = 0.2f, hitEnd = 0.7f,
            outerRadius = 3.2f, innerRadius = 1.5f,
        },
    };

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
    [Tooltip("落地冲击圆半径")]
    [SerializeField] float jumpDownImpactRadius = 1.35f;
    [Tooltip("冲击中心相对脚底锚点的偏移")]
    [SerializeField] Vector2 jumpDownImpactOffset = Vector2.zero;

    [Header("向上攻击默认判定（剖面 upHitbox 未填时回退）")]
    [SerializeField] Vector2 defaultUpHitboxSize = new Vector2(1.3f, 1.8f);
    [SerializeField] Vector2 defaultUpHitboxOffset = new Vector2(0f, 1.2f);

    [Header("攻击范围可视化（Scene View）")]
    [Tooltip("运行中/编辑器 Scene 视图始终显示判定框，无需选中角色")]
    [SerializeField] bool showAttackRangesInScene = true;
    [SerializeField] Color detectZoneGizmoColor = new Color(1f, 0.85f, 0.2f, 0.25f);
    [SerializeField] Color hitboxIdleGizmoColor = new Color(0.2f, 0.85f, 1f, 0.2f);
    [SerializeField] Color hitboxActiveGizmoColor = new Color(1f, 0.2f, 0.2f, 0.45f);

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
    bool holdingDashInputLock;
    bool restoredMovementEnabled;
    bool restoredRollEnabled;
    bool restoredWeaponControllerEnabled;
    bool jumpDownAttackActive;
    bool jumpDownImpactApplied;

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
    }

    void OnDisable()
    {
        EndJumpDownAttack();
        EndDashInputLock();
        EndRushSpecialState();
        actions.Player.Disable();
    }

    void OnDestroy()
    {
        actions?.Dispose();
    }

    void Update()
    {
        RefreshWeaponProfile(force: false);

        // 冲刺锁期间关掉了 PlayerMovement，需自行推进空中/近战完成检测，否则 Special 永不结束
        if (holdingDashInputLock)
            MaintainDashLockAnimation();

        if (actions.Player.Jump.WasPressedThisFrame())
            jumpPressedThisFrame = true;

        TryStartMeleeAttack();
        TryStartSpecialAttack();
        UpdateMeleeHitbox();
    }

    void LateUpdate() => SyncDetectZoneAnchor();

    void FixedUpdate()
    {
        UpdateJumpDownAttack();
        UpdateDashAttacks();

        if (physicsCheck.isGround)
        {
            hasUsedDoubleJump = false;
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
            if (profile.rearHitEnd <= profile.rearHitStart)
            {
                profile.rearHitStart = Mathf.Clamp01(profile.hitEnd);
                profile.rearHitEnd = Mathf.Clamp01(profile.rearHitStart + 0.2f);
            }
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
                ? activeSpecialProfile.damage
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

    void TryStartMeleeAttack()
    {
        if (holdingDashInputLock)
            return;

        if (playerMovement != null && playerMovement.IsActionLocked)
            return;

        if (playerAnim == null || playerAnim.IsDead)
            return;

        if (playerAnim.IsSpecial)
            return;

        if (!actions.Player.Attack.WasPressedThisFrame())
            return;

        if (detectZone != null && detectZone.HasValidTarget)
        {
            var target = detectZone.GetNearestTarget(transform.position);
            if (target != null && playerMovement != null)
                playerMovement.FaceTowardWorldX(target.position.x);
        }

        // 攻击前用本帧输入同步仰视/俯视，避免与 PlayerMovement 的 Update 顺序导致站立 upattack 丢方向
        Vector2 move = actions.Player.Move.ReadValue<Vector2>();
        bool lookUp = move.y > inputThreshold;
        bool lookDown = !physicsCheck.isGround && move.y < -inputThreshold;
        playerAnim.SetLookUp(lookUp);
        playerAnim.SetLookDown(lookDown);

        swingHitTargets.Clear();
        specialRearHitTargets.Clear();
        swingHitCountables.Clear();
        playerAnim.InterruptTurn();
        playerAnim.TryPlayMeleeAnim();
        ApplyActiveProfileToColliders();

        if (fullBodyAnim != null && fullBodyAnim.IsCrouchMelee)
            BeginDashInputLock();
        else if (fullBodyAnim != null && fullBodyAnim.IsJumpDownAttack)
            BeginJumpDownAttack();
    }

    void TryStartSpecialAttack()
    {
        if (holdingDashInputLock)
            return;

        if (playerMovement != null && playerMovement.IsActionLocked)
            return;

        if (playerAnim == null || playerAnim.IsDead || playerAnim.IsSwitchingWeapon)
            return;

        if (!actions.Player.Ability1.WasPressedThisFrame())
            return;

        int weaponId = ResolveCurrentWeaponId();
        if (weaponId == 0 || !hasSpecialProfile || activeSpecialProfile.weaponId != weaponId)
            return;

        if (fullBodyAnim != null
            && (fullBodyAnim.AppliedWeaponDefinition == null
                || fullBodyAnim.AppliedWeaponDefinition.special == null))
            return;

        AmmoType ammoType = ResolveSpecialAmmoType(weaponId);
        if (specialAmmoCost > 0
            && (selfCharacter == null || !HasEnoughAmmo(ammoType, specialAmmoCost)))
            return;

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
        if (!playerAnim.TryPlaySpecialAnim())
            return;

        if (specialAmmoCost > 0 && selfCharacter != null)
            selfCharacter.TrySpendAmmo(ammoType, specialAmmoCost);

        ApplyActiveProfileToColliders();

        // 仅 rush（武器1）冲刺特技锁输入；whip/buzzsaw 不锁
        if (weaponId == 1)
            BeginDashInputLock();
    }

    static AmmoType ResolveSpecialAmmoType(int weaponId) => weaponId switch
    {
        1 => AmmoType.S,
        2 => AmmoType.M,
        3 => AmmoType.L,
        _ => AmmoType.S,
    };

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
            EndDashInputLock();
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
                ? activeSpecialProfile.damage
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
        ApplyHitboxShape(upward: false, special: true);

        if (meleeAttack != null)
        {
            meleeAttack.damage = activeSpecialProfile.damage;
            meleeAttack.enabled = false;
        }

        if (!playerAnim.TryGetMeleeAnimProgress(out float t))
        {
            if (meleeHitbox.activeSelf)
                meleeHitbox.SetActive(false);
            return;
        }

        bool frontWindow = t >= activeSpecialProfile.hitStart && t <= activeSpecialProfile.hitEnd;
        bool rearWindow = t >= activeSpecialProfile.rearHitStart && t <= activeSpecialProfile.rearHitEnd;

        if (frontWindow || rearWindow)
        {
            if (!meleeHitbox.activeSelf)
                meleeHitbox.SetActive(true);

            if (frontWindow)
            {
                ProcessSpecialBoxHits(
                    activeSpecialProfile.hitboxOffset,
                    activeSpecialProfile.hitboxSize,
                    activeSpecialProfile.damage,
                    swingHitTargets,
                    activeSpecialProfile.maxTargets);
            }

            if (rearWindow)
            {
                Vector2 rearOffset = new Vector2(
                    -activeSpecialProfile.hitboxOffset.x,
                    activeSpecialProfile.hitboxOffset.y);
                ProcessSpecialBoxHits(
                    rearOffset,
                    activeSpecialProfile.hitboxSize,
                    activeSpecialProfile.damage,
                    specialRearHitTargets,
                    activeSpecialProfile.maxTargets);
            }
        }
        else if (meleeHitbox.activeSelf)
        {
            meleeHitbox.SetActive(false);
        }
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
        int outerDamage = activeSpecialProfile.damage;
        int innerDamage = activeSpecialProfile.innerDamage > 0
            ? activeSpecialProfile.innerDamage
            : Mathf.Max(1, outerDamage / 2);
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
            target.TakeDamage(meleeAttack);
            swingHitTargets.Add(target);
            slots--;
        }
    }

    void ProcessSpecialBoxHits(
        Vector2 localOffset,
        Vector2 localSize,
        int damage,
        HashSet<Character> hitSet,
        int maxTargets)
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

        int count = Physics2D.OverlapBoxNonAlloc(center, worldSize, angle, overlapBuffer);
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
        for (int i = 0; i < overlapCharacters.Count && slots > 0; i++)
        {
            var target = overlapCharacters[i];
            if (hitSet.Contains(target))
                continue;

            target.TakeDamage(meleeAttack);
            hitSet.Add(target);
            slots--;
        }

        meleeAttack.damage = previousDamage;
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

        if (playerAnim == null || !playerAnim.IsMelee)
        {
            if (rushDashActive)
                rushDashActive = false;
            if (holdingDashInputLock)
            {
                // 锁定期但近战已结束：刹停水平速度，等待 Update 解锁
                rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
            }

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

        if (physicsCheck != null)
            physicsCheck.Check();

        // 锁定期全程接管水平速度：冲刺窗内加速，窗外刹停，避免其它输入改速度
        if (holdingDashInputLock || rushDash || shortDash)
        {
            bool wantDash = rushDash || shortDash;
            float speed = rushDash ? activeSpecialProfile.rushSpeed : shortMeleeDashSpeed;
            bool blocked = physicsCheck != null && physicsCheck.IsBlockedHorizontally(dir);

            if (wantDash && !blocked)
            {
                rushDashActive = true;
                rb.linearVelocity = new Vector2(dir * speed, rb.linearVelocity.y);
                if (rushDash)
                    PushEnemiesAlongRushPath(dir);
            }
            else
            {
                rushDashActive = false;
                rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
            }

            return;
        }

        if (rushDashActive)
            rushDashActive = false;
    }

    void BeginJumpDownAttack()
    {
        jumpDownAttackActive = true;
        jumpDownImpactApplied = false;
        BeginDashInputLock();

        if (rb != null)
            rb.linearVelocity = new Vector2(0f, -Mathf.Abs(jumpDownSlamSpeed));
    }

    void EndJumpDownAttack()
    {
        jumpDownAttackActive = false;
        jumpDownImpactApplied = false;
    }

    void UpdateJumpDownAttack()
    {
        if (!jumpDownAttackActive)
            return;

        if (playerAnim == null || !playerAnim.IsMelee || !IsCurrentSwingJumpDownAttack())
        {
            EndJumpDownAttack();
            return;
        }

        if (physicsCheck != null)
            physicsCheck.Check();

        bool grounded = physicsCheck != null && physicsCheck.isGround;
        if (!grounded)
        {
            rb.linearVelocity = new Vector2(0f, -Mathf.Abs(jumpDownSlamSpeed));
            return;
        }

        rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);

        if (!jumpDownImpactApplied)
        {
            ApplyJumpDownImpact();
            jumpDownImpactApplied = true;
            fullBodyAnim?.RestartCurrentMeleeAnim();
        }
    }

    void ApplyJumpDownImpact()
    {
        if (meleeAttack == null)
            return;

        Vector2 center = ResolveSpecialCenter() + jumpDownImpactOffset;
        float radius = Mathf.Max(0.1f, jumpDownImpactRadius);
        int count = Physics2D.OverlapCircleNonAlloc(center, radius, overlapBuffer);
        if (count <= 0)
            return;

        int previousDamage = meleeAttack.damage;
        meleeAttack.damage = Mathf.Max(1, jumpDownImpactDamage);
        meleeAttack.enabled = false;

        swingHitTargets.Clear();
        for (int i = 0; i < count; i++)
        {
            if (!TryResolveAttackTarget(overlapBuffer[i], swingHitTargets, out Character target))
                continue;

            target.TakeDamage(meleeAttack);
            swingHitTargets.Add(target);
        }

        meleeAttack.damage = previousDamage;
    }

    void MaintainDashLockAnimation()
    {
        if (physicsCheck != null)
            physicsCheck.Check();

        float velocityY = rb != null ? rb.linearVelocity.y : 0f;
        bool grounded = physicsCheck != null && physicsCheck.isGround;
        playerAnim?.UpdateAirState(grounded, velocityY);

        if (playerAnim == null || !playerAnim.IsMelee)
        {
            EndJumpDownAttack();
            EndDashInputLock();
        }
    }

    void BeginDashInputLock()
    {
        if (holdingDashInputLock)
            return;

        holdingDashInputLock = true;

        // 不改 PlayerMovement 逻辑：临时禁用组件以屏蔽移动/跳跃输入；
        // 近战完成改由 MaintainDashLockAnimation 驱动。
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

    void EndDashInputLock()
    {
        if (!holdingDashInputLock)
            return;

        holdingDashInputLock = false;
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
        if (holdingDashInputLock)
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

        if (fullBodyAnim != null)
            fullBodyAnim.PlayDoubleJumpAnim(hasHorizontal);
        else
            playerAnim?.PlayJumpAnim(hasHorizontal);
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
            Vector3 center = Application.isPlaying
                ? (Vector3)(ResolveSpecialCenter() + jumpDownImpactOffset)
                : (meleePoint1 != null ? meleePoint1.position : transform.position)
                    + (Vector3)jumpDownImpactOffset;
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

        // Whip 特技：额外画出后方镜像盒
        if (hasSpecialDraw && specialDraw.weaponId == 2)
        {
            Vector2 rearOffset = new Vector2(-specialDraw.hitboxOffset.x, specialDraw.hitboxOffset.y);
            Color rearColor = drawSpecial
                ? new Color(1f, 0.35f, 0.85f, 0.35f)
                : new Color(1f, 0.45f, 0.9f, 0.18f);
            DrawLocalBoxGizmo(hitMatrix, rearOffset, specialDraw.hitboxSize, rearColor, filled: false);
        }

        // 非向上挥击时额外用半透明线框标出上方判定，方便对照
        if (!drawUp && !drawSpecial)
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
