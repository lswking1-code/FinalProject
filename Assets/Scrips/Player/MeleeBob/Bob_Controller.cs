using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Melee_Player（Bob）专属能力管理。不改动其他玩家脚本，仅在本组件内扩展能力。
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(PhysicsCheck))]
public class Bob_Controller : MonoBehaviour
{
    [System.Serializable]
    public struct WeaponMeleeProfile
    {
        public int weaponId;
        public int damage;
        [Tooltip("攻击 Hitbox 本地尺寸")]
        public Vector2 hitboxSize;
        [Tooltip("攻击 Hitbox 本地偏移（面向右为正 X）")]
        public Vector2 hitboxOffset;
        [Tooltip("索敌区尺寸")]
        public Vector2 detectSize;
        [Tooltip("索敌区偏移")]
        public Vector2 detectOffset;
        [Tooltip("0 = 不限制命中数；>0 为单次挥击最多命中敌人数")]
        public int maxTargets;
        [Range(0f, 1f)] public float hitStart;
        [Range(0f, 1f)] public float hitEnd;
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
            detectSize = new Vector2(2f, 2f), detectOffset = new Vector2(0.5f, 0f),
            maxTargets = 0, hitStart = 0.15f, hitEnd = 0.45f,
        },
        new WeaponMeleeProfile
        {
            weaponId = 1, damage = 55,
            hitboxSize = new Vector2(2.6f, 1.25f), hitboxOffset = new Vector2(1.3f, 0f),
            detectSize = new Vector2(3.2f, 2f), detectOffset = new Vector2(1.4f, 0f),
            maxTargets = 0, hitStart = 0.12f, hitEnd = 0.5f,
        },
        new WeaponMeleeProfile
        {
            weaponId = 2, damage = 45,
            hitboxSize = new Vector2(3.8f, 0.4f), hitboxOffset = new Vector2(1.9f, 0f),
            detectSize = new Vector2(4.2f, 1.2f), detectOffset = new Vector2(2.0f, 0f),
            maxTargets = 0, hitStart = 0.1f, hitEnd = 0.55f,
        },
        new WeaponMeleeProfile
        {
            weaponId = 3, damage = 70,
            hitboxSize = new Vector2(1.1f, 1.1f), hitboxOffset = new Vector2(0.55f, 0f),
            detectSize = new Vector2(1.8f, 1.6f), detectOffset = new Vector2(0.7f, 0f),
            maxTargets = 2, hitStart = 0.15f, hitEnd = 0.45f,
        },
    };

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

    bool jumpPressedThisFrame;
    bool hasUsedDoubleJump;

    int activeWeaponId = -1;
    WeaponMeleeProfile activeProfile;
    readonly HashSet<Character> swingHitTargets = new();
    readonly List<Character> overlapCharacters = new();
    readonly Collider2D[] overlapBuffer = new Collider2D[24];

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        physicsCheck = GetComponent<PhysicsCheck>();
        playerMovement = GetComponent<PlayerMovement>();
        playerAnim = PlayerAnimBase.Resolve(gameObject);
        fullBodyAnim = playerAnim as PlayerFullBodyAnim;
        weaponController = GetComponent<PlayerWeaponController>();
        selfCharacter = GetComponent<Character>();
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
        actions.Player.Disable();
    }

    void OnDestroy()
    {
        actions?.Dispose();
    }

    void Update()
    {
        RefreshWeaponProfile(force: false);

        if (actions.Player.Jump.WasPressedThisFrame())
            jumpPressedThisFrame = true;

        TryStartMeleeAttack();
        UpdateMeleeHitbox();
    }

    void LateUpdate() => SyncDetectZoneAnchor();

    void FixedUpdate()
    {
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
        if (profile.detectSize.x <= 0.01f || profile.detectSize.y <= 0.01f)
            profile.detectSize = new Vector2(2f, 2f);
        if (profile.hitEnd <= profile.hitStart)
        {
            profile.hitStart = hitStart;
            profile.hitEnd = hitEnd;
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
                    detectSize = new Vector2(3.2f, 2f), detectOffset = new Vector2(1.4f, 0f),
                    maxTargets = 0, hitStart = 0.12f, hitEnd = 0.5f,
                };
            case 2:
                return new WeaponMeleeProfile
                {
                    weaponId = 2, damage = 45,
                    hitboxSize = new Vector2(3.8f, 0.4f), hitboxOffset = new Vector2(1.9f, 0f),
                    detectSize = new Vector2(4.2f, 1.2f), detectOffset = new Vector2(2.0f, 0f),
                    maxTargets = 0, hitStart = 0.1f, hitEnd = 0.55f,
                };
            case 3:
                return new WeaponMeleeProfile
                {
                    weaponId = 3, damage = 70,
                    hitboxSize = new Vector2(1.1f, 1.1f), hitboxOffset = new Vector2(0.55f, 0f),
                    detectSize = new Vector2(1.8f, 1.6f), detectOffset = new Vector2(0.7f, 0f),
                    maxTargets = 2, hitStart = 0.15f, hitEnd = 0.45f,
                };
            default:
                return new WeaponMeleeProfile
                {
                    weaponId = 0, damage = 40,
                    hitboxSize = new Vector2(1.2f, 1f), hitboxOffset = Vector2.zero,
                    detectSize = new Vector2(2f, 2f), detectOffset = new Vector2(0.5f, 0f),
                    maxTargets = 0, hitStart = 0.15f, hitEnd = 0.45f,
                };
        }
    }

    void ApplyActiveProfileToColliders()
    {
        if (meleeHitboxCollider != null)
        {
            meleeHitboxCollider.offset = activeProfile.hitboxOffset;
            meleeHitboxCollider.size = activeProfile.hitboxSize;
        }

        if (detectZoneCollider != null)
        {
            detectZoneCollider.offset = activeProfile.detectOffset;
            detectZoneCollider.size = activeProfile.detectSize;
        }

        if (meleeAttack != null)
        {
            meleeAttack.damage = activeProfile.damage > 0 ? activeProfile.damage : meleeDamage;
            meleeAttack.attackType = AttackType.Melee;
            meleeAttack.ignoreTag = "Player";
            // 有命中上限时改由本脚本结算，避免 Attack 触发器打满所有重叠目标
            meleeAttack.enabled = activeProfile.maxTargets <= 0;
        }
    }

    void TryStartMeleeAttack()
    {
        if (playerMovement != null && playerMovement.IsActionLocked)
            return;

        if (playerAnim == null || playerAnim.IsDead)
            return;

        if (!actions.Player.Attack.WasPressedThisFrame())
            return;

        if (detectZone != null && detectZone.HasValidTarget)
        {
            var target = detectZone.GetNearestTarget(transform.position);
            if (target != null && playerMovement != null)
                playerMovement.FaceTowardWorldX(target.position.x);
        }

        swingHitTargets.Clear();
        playerAnim.TryPlayMeleeAnim();
    }

    void UpdateMeleeHitbox()
    {
        if (meleeHitbox == null || playerAnim == null)
            return;

        if (!playerAnim.IsMelee)
        {
            if (meleeHitbox.activeSelf)
                meleeHitbox.SetActive(false);
            return;
        }

        SyncHitboxAnchor();

        float windowStart = activeProfile.hitStart;
        float windowEnd = activeProfile.hitEnd;
        bool inHitWindow = playerAnim.TryGetMeleeAnimProgress(out float t)
            && t >= windowStart && t <= windowEnd;

        if (inHitWindow)
        {
            if (!meleeHitbox.activeSelf)
                meleeHitbox.SetActive(true);

            if (activeProfile.maxTargets > 0)
                ProcessLimitedHitTargets();
        }
        else if (meleeHitbox.activeSelf)
        {
            meleeHitbox.SetActive(false);
        }
    }

    void ProcessLimitedHitTargets()
    {
        if (meleeAttack == null || meleeHitboxCollider == null)
            return;

        if (swingHitTargets.Count >= activeProfile.maxTargets)
            return;

        Bounds bounds = meleeHitboxCollider.bounds;
        int count = Physics2D.OverlapBoxNonAlloc(bounds.center, bounds.size, 0f, overlapBuffer);
        if (count <= 0)
            return;

        overlapCharacters.Clear();
        for (int i = 0; i < count; i++)
        {
            var col = overlapBuffer[i];
            if (col == null)
                continue;

            // Hitbox / DetectZone 等子碰撞体没有 Player Tag，但仍会 GetComponentInParent 到自己
            if (col.transform == transform || col.transform.IsChildOf(transform))
                continue;

            var target = col.GetComponentInParent<Character>();
            if (target == null || target == selfCharacter || swingHitTargets.Contains(target))
                continue;
            if (!string.IsNullOrEmpty(meleeAttack.ignoreTag) && target.CompareTag(meleeAttack.ignoreTag))
                continue;
            if (target.currentHealth <= 0f)
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

        int slots = activeProfile.maxTargets - swingHitTargets.Count;
        for (int i = 0; i < overlapCharacters.Count && slots > 0; i++)
        {
            var target = overlapCharacters[i];
            if (swingHitTargets.Contains(target))
                continue;

            target.TakeDamage(meleeAttack);
            swingHitTargets.Add(target);
            slots--;
        }
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
        if (Application.isPlaying)
        {
            drawProfile = activeProfile;
        }
        else
        {
            var wc = weaponController != null ? weaponController : GetComponent<PlayerWeaponController>();
            int wid = wc != null ? wc.CurrentWeaponId : 0;
            drawProfile = FindProfile(wid);
        }

        DrawLocalBoxGizmo(
            GetDetectDrawMatrix(),
            drawProfile.detectOffset,
            drawProfile.detectSize,
            detectZoneGizmoColor,
            filled: true);

        bool hitboxLive = Application.isPlaying && meleeHitbox != null && meleeHitbox.activeInHierarchy;
        Color hitColor = hitboxLive ? hitboxActiveGizmoColor : hitboxIdleGizmoColor;
        Matrix4x4 hitMatrix = GetHitboxDrawMatrix(meleeHitbox != null ? meleeHitbox.transform : null);

        DrawLocalBoxGizmo(hitMatrix, drawProfile.hitboxOffset, drawProfile.hitboxSize, hitColor, filled: true);
        DrawLocalBoxGizmo(
            hitMatrix,
            drawProfile.hitboxOffset,
            drawProfile.hitboxSize,
            new Color(hitColor.r, hitColor.g, hitColor.b, Mathf.Clamp01(hitColor.a + 0.35f)),
            filled: false);
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
}
