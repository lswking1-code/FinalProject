using System.Collections.Generic;
using UnityEngine;

public enum AttackType { Melee, Projectile }

public class Attack : MonoBehaviour
{
    public int damage;
    public float attackRange;
    public float attackRate;
    public AttackType attackType = AttackType.Melee;

    [Tooltip("为 true 时充能机关将其视为 M 弹 / M 武器攻击")]
    public bool chargesEnergyNode;

    [Header("目标过滤（子弹可选）")]
    [Tooltip("仅伤害带有此 Tag 的目标，留空则不限制")]
    public string requireTag;
    [Tooltip("忽略带有此 Tag 的目标，留空则不忽略")]
    public string ignoreTag;

    [Header("推动")]
    [Tooltip("开启后对受击目标施加击退（方向为攻击面朝）")]
    public bool enableKnockback;
    [Tooltip("击退冲量大小（Dynamic 为 Impulse；Kinematic 为位移总量）")]
    public float knockbackForce;
    [Tooltip("玩家水平移动门控时长 / Kinematic 位移时长（秒）")]
    public float knockbackDuration = 0.15f;

    [Header("护盾")]
    [Tooltip("对 IDamageAbsorb（敌人盾牌）的伤害倍率；1 = 不额外加成。不影响打到本体的伤害")]
    public float shieldDamageMultiplier = 1f;

    [Header("抵销敌人飞行道具")]
    [Tooltip("为 true 时，近战判定可抵销敌人子弹/导弹/手雷等")]
    public bool cancelEnemyProjectiles;

    [Header("命中横向震屏（opt-in）")]
    [Tooltip("仅对 Character 成功造成伤害时触发；与动画事件旧震屏独立")]
    [SerializeField] bool enableHitCameraShake;
    [SerializeField] FloatEventSO hitCameraShakeEvent;
    [SerializeField] float hitCameraShakeForce = 0.12f;

    [Header("机械师命中特效")]
    [Tooltip("Auto 自动识别机械师子弹/近战/机器人；None 关闭；其他值手动指定风格")]
    public MachinistImpactKind impactKind = MachinistImpactKind.Auto;
    [Min(0.1f)] public float impactScale = 1f;
    readonly Dictionary<int, float> nextImpactTime = new();
    bool projectileImpactShown;

    readonly HashSet<Character> hitTargets = new();
    readonly Dictionary<Character, float> nextHitTime = new();
    readonly HashSet<IHitCountable> hitCountables = new();
    readonly Dictionary<IHitCountable, float> nextHitCountableTime = new();
    readonly HashSet<IKnockbackable> knockbackTargets = new();
    readonly List<Collider2D> overlapBuffer = new();

    const string BlastTag = "Blast";
    const float DefaultBlastPropKnockback = 8f;
    const float DefaultLightPropKnockback = 2f;

    /// <summary>成功对 Character 造成伤害时广播（含 Trigger 与外部 TakeDamage 无关）。</summary>
    public event System.Action<Character, int> CharacterDamaged;

    /// <summary>
    /// 玩家近战判定盒（挂在 Player 角色下）。不含霰弹/激光/持续弹等弹药型 Melee。
    /// </summary>
    public static bool IsPlayerMeleeHitbox(Collider2D collider, out Attack meleeAttack)
    {
        meleeAttack = null;
        if (collider == null)
            return false;

        meleeAttack = collider.GetComponentInParent<Attack>();
        if (meleeAttack == null || meleeAttack.attackType != AttackType.Melee)
        {
            meleeAttack = null;
            return false;
        }

        if (MeleeDetectZone.IsSensorCollider(collider))
        {
            meleeAttack = null;
            return false;
        }

        if (collider.GetComponentInParent<IPlayerAmmo>() != null
            || collider.GetComponentInParent<PlayerLaserBeam>() != null
            || collider.GetComponentInParent<GrenadeExplosion>() != null)
        {
            meleeAttack = null;
            return false;
        }

        var character = collider.GetComponentInParent<Character>();
        if (character == null || !character.CompareTag("Player"))
        {
            meleeAttack = null;
            return false;
        }

        return true;
    }

    void OnEnable()
    {
        nextImpactTime.Clear();
        projectileImpactShown = false;
        hitTargets.Clear();
        nextHitTime.Clear();
        hitCountables.Clear();
        nextHitCountableTime.Clear();
        knockbackTargets.Clear();
        WakeRelatedRigidbodies();
        ProcessOverlapHits();
    }

    /// <summary>
    /// 击退方向：优先攻击面朝（transform.right），失败则用目标相对攻击者的水平方向。
    /// </summary>
    public static Vector2 ResolveKnockbackDir(Attack attacker, Vector2 targetPos)
    {
        if (attacker != null)
        {
            Vector2 facing = attacker.transform.right;
            if (facing.sqrMagnitude > 0.0001f)
                return facing.normalized;
        }

        if (attacker != null)
        {
            Vector2 fallback = new Vector2(targetPos.x - attacker.transform.position.x, 0f);
            if (fallback.sqrMagnitude > 0.0001f)
                return fallback.normalized;
        }

        return Vector2.right;
    }

    /// <summary>有效击退力 = knockbackForce / max(1, resistance)。</summary>
    public static float EffectiveKnockbackForce(Attack attacker, float resistance)
    {
        if (attacker == null || !attacker.enableKnockback || attacker.knockbackForce <= 0f)
            return 0f;

        float res = Mathf.Max(1f, resistance);
        return attacker.knockbackForce / res;
    }

    public static bool HasBlastTag(Transform root)
    {
        for (Transform t = root; t != null; t = t.parent)
        {
            if (t.CompareTag(BlastTag))
                return true;
        }

        return false;
    }

    public static bool ShouldKnockbackProp(Attack attacker)
    {
        if (attacker == null)
            return false;
        if (HasBlastTag(attacker.transform))
            return true;
        if (attacker.enableKnockback && attacker.knockbackForce > 0f)
            return true;
        return IsPlayerLightPropAttack(attacker);
    }

    /// <summary>
    /// 玩家普通射击/近战可轻推场景物。不含镭射、手雷；霰弹等 Blast 走滑行。
    /// </summary>
    public static bool IsPlayerLightPropAttack(Attack attacker)
    {
        if (attacker == null)
            return false;

        if (attacker.GetComponentInParent<PlayerLaserBeam>() != null)
            return false;
        if (attacker.GetComponentInParent<GrenadeExplosion>() != null)
            return false;

        if (attacker.GetComponentInParent<IPlayerAmmo>() != null)
            return true;

        if (attacker.attackType != AttackType.Melee)
            return false;

        var character = attacker.GetComponentInParent<Character>();
        return character != null && character.CompareTag("Player");
    }

    /// <summary>
    /// 投射物撞墙销毁：Ground，以及箱子/压力板所在的 InteractableObject。
    /// </summary>
    public static bool IsProjectileBlockingLayer(int layer)
    {
        string name = LayerMask.LayerToName(layer);
        return name == "Ground" || name == "InteractableObject";
    }

    public static bool IsProjectileBlockingCollider(Collider2D collider)
    {
        return collider != null && IsProjectileBlockingLayer(collider.gameObject.layer);
    }

    /// <summary>
    /// 场景物击退：Blast 用默认大冲量；已勾击退用 knockbackForce；其余玩家射击/近战用轻推。
    /// </summary>
    public static float EffectivePropKnockbackForce(Attack attacker, float resistance)
    {
        if (!ShouldKnockbackProp(attacker))
            return 0f;

        float force;
        if (HasBlastTag(attacker.transform))
        {
            force = attacker.knockbackForce;
            if (force <= 0f)
                force = DefaultBlastPropKnockback;
        }
        else if (attacker.enableKnockback && attacker.knockbackForce > 0f)
        {
            force = attacker.knockbackForce;
        }
        else
        {
            force = DefaultLightPropKnockback;
        }

        return force / Mathf.Max(1f, resistance);
    }

    /// <summary>
    /// Hitbox 启用时唤醒自身所属刚体，以及当前已重叠碰撞体上的刚体，
    /// 避免双方休眠时 Trigger 不产生 Enter/Stay。
    /// </summary>
    void WakeRelatedRigidbodies()
    {
        var selfRb = GetComponentInParent<Rigidbody2D>();
        if (selfRb != null)
            selfRb.WakeUp();

        var col = GetComponent<Collider2D>();
        if (col == null || !col.enabled)
            return;

        Physics2D.SyncTransforms();

        overlapBuffer.Clear();
        col.Overlap(CreateOverlapFilter(), overlapBuffer);

        for (int i = 0; i < overlapBuffer.Count; i++)
        {
            var other = overlapBuffer[i];
            if (other == null)
                continue;

            var otherRb = other.attachedRigidbody;
            if (otherRb != null)
                otherRb.WakeUp();
        }
    }

    /// <summary>
    /// 动画关键帧才打开判定时调用：唤醒已重叠刚体并对当前重叠立即结算。
    /// </summary>
    public void NotifyHitboxEnabled()
    {
        WakeRelatedRigidbodies();
        for (int i = 0; i < overlapBuffer.Count; i++)
        {
            if (overlapBuffer[i] != null)
                TryDamage(overlapBuffer[i]);
        }
    }

    public void ProcessOverlapHits()
    {
        var col = GetComponent<Collider2D>();
        if (col == null || !col.enabled)
            return;

        Physics2D.SyncTransforms();

        overlapBuffer.Clear();
        col.Overlap(CreateOverlapFilter(), overlapBuffer);

        for (int i = 0; i < overlapBuffer.Count; i++)
        {
            if (overlapBuffer[i] != null)
                TryDamage(overlapBuffer[i]);
        }
    }

    ContactFilter2D CreateOverlapFilter()
    {
        var filter = new ContactFilter2D { useTriggers = true };
        filter.SetLayerMask(Physics2D.GetLayerCollisionMask(gameObject.layer));
        return filter;
    }

    void OnTriggerEnter2D(Collider2D collision) => TryDamage(collision);

    void OnTriggerStay2D(Collider2D collision) => TryDamage(collision);

    void TryDamage(Collider2D collision)
    {
        if (collision == null || MeleeDetectZone.IsSensorCollider(collision))
            return;

        if (TryApplyPropKnockback(collision))
        {
            ReportImpact(collision, MachinistImpactKind.Surface);
            if (attackType == AttackType.Projectile)
                Destroy(gameObject);
            return;
        }

        if (attackType == AttackType.Projectile
            && IsProjectileBlockingCollider(collision))
        {
            // Ground / InteractableObject 上的可破坏物（如 BreakableDoor）需先计次/闪红，再销毁子弹
            var groundHitCountable = collision.GetComponentInParent<IHitCountable>();
            if (groundHitCountable != null && CanHitCountable(groundHitCountable))
            {
                if (groundHitCountable.RegisterHit(this))
                    MarkHitCountable(groundHitCountable);
            }
            ReportImpact(collision, MachinistImpactKind.Surface);
            Destroy(gameObject);
            return;
        }

        if (!string.IsNullOrEmpty(requireTag) && !collision.CompareTag(requireTag))
            return;
        if (!string.IsNullOrEmpty(ignoreTag) && collision.CompareTag(ignoreTag))
            return;

        var selfBody = GetComponentInParent<Rigidbody2D>();
        if (selfBody != null && collision.attachedRigidbody == selfBody)
            return;

        var target = collision.GetComponentInParent<Character>();
        bool hitSomething = false;

        if (target != null)
        {
            if (!string.IsNullOrEmpty(ignoreTag) && target.CompareTag(ignoreTag))
                return;

            var selfCharacter = GetComponentInParent<Character>();
            if (selfCharacter != null && target == selfCharacter)
                return;
            bool useRateLimit = attackRate > 0f;
            if (!useRateLimit && hitTargets.Contains(target))
                return;
            if (useRateLimit && nextHitTime.TryGetValue(target, out float nextHit) && Time.time < nextHit)
                return;

            // TakeDamage may disable a collider on a killing blow; preserve the live contact first.
            Vector2? impactPoint = MachinistImpactVfx.ResolveKind(this) != MachinistImpactKind.None
                ? MachinistImpactVfx.ContactPoint(this, collision) : (Vector2?)null;
            bool damaged = target.TakeDamage(this);
            if (!useRateLimit)
                hitTargets.Add(target);
            if (useRateLimit)
                nextHitTime[target] = Time.time + 1f / attackRate;

            if (damaged)
            {
                ReportImpact(collision, point: impactPoint);
                RaiseHitCameraShakeIfEnabled();
                CharacterDamaged?.Invoke(target, damage);
            }

            hitSomething = true;
        }
        else
        {
            var cancelable = collision.GetComponentInParent<IEnemyProjectileCancelable>();
            if (cancelable != null)
            {
                if (cancelEnemyProjectiles && cancelable.TryCancelByMelee(this))
                {
                    ReportImpact(collision, MachinistImpactKind.Shield);
                    hitSomething = true;
                }
            }
            else
            {
                var hitCountable = collision.GetComponentInParent<IHitCountable>();
                if (hitCountable != null && CanHitCountable(hitCountable))
                {
                    if (hitCountable.RegisterHit(this))
                    {
                        MarkHitCountable(hitCountable);
                        ReportImpact(collision);
                        hitSomething = true;
                    }
                }
            }

            if (TryApplyPropKnockback(collision))
                hitSomething = true;
        }

        if (hitSomething && attackType == AttackType.Projectile)
            Destroy(gameObject);
    }

    /// <summary>Call only after accepted damage, absorption or a blocking contact. Cosmetic deduplication only.</summary>
    public void ReportImpact(Collider2D collision, MachinistImpactKind kind = MachinistImpactKind.Auto,
        Vector2? point = null, Vector2? direction = null)
    {
        var sourceKind = MachinistImpactVfx.ResolveKind(this);
        if (sourceKind == MachinistImpactKind.None || collision == null) return;
        if (attackType == AttackType.Projectile && projectileImpactShown) return;
        var target = collision.GetComponentInParent<Character>();
        int key = target != null ? target.GetInstanceID()
            : collision.attachedRigidbody != null ? collision.attachedRigidbody.GetInstanceID()
            : collision.GetInstanceID();
        if (nextImpactTime.TryGetValue(key, out float next) && Time.time < next) return;
        nextImpactTime[key] = Time.time + 0.075f;
        if (attackType == AttackType.Projectile) projectileImpactShown = true;
        var body = GetComponent<Rigidbody2D>();
        Vector2 facing = body != null && body.linearVelocity.sqrMagnitude > 0.001f
            ? body.linearVelocity.normalized : (Vector2)transform.right;
        MachinistImpactVfx.Play(kind == MachinistImpactKind.Auto ? sourceKind : kind,
            point ?? MachinistImpactVfx.ContactPoint(this, collision), direction ?? facing, impactScale, sourceKind);
    }

    bool TryApplyPropKnockback(Collider2D collision)
    {
        if (collision == null || !ShouldKnockbackProp(this))
            return false;
        if (collision.GetComponentInParent<Character>() != null)
            return false;

        var knockable = collision.GetComponentInParent<IKnockbackable>();
        if (knockable == null || knockbackTargets.Contains(knockable))
            return false;

        knockable.ApplyKnockback(this);
        knockbackTargets.Add(knockable);
        return true;
    }

    bool CanHitCountable(IHitCountable target)
    {
        bool useRateLimit = attackRate > 0f;
        if (!useRateLimit)
            return !hitCountables.Contains(target);

        if (nextHitCountableTime.TryGetValue(target, out float next) && Time.time < next)
            return false;

        return true;
    }

    void MarkHitCountable(IHitCountable target)
    {
        if (attackRate > 0f)
            nextHitCountableTime[target] = Time.time + 1f / attackRate;
        else
            hitCountables.Add(target);
    }

    /// <summary>
    /// 成功命中 Character 后触发横向震屏。Trigger 路径与 Bob 手动 Overlap 共用。
    /// </summary>
    public void RaiseHitCameraShakeIfEnabled()
    {
        if (!enableHitCameraShake || hitCameraShakeEvent == null || hitCameraShakeForce <= 0f)
            return;

        hitCameraShakeEvent.RaiseEvent(hitCameraShakeForce);
    }
}
