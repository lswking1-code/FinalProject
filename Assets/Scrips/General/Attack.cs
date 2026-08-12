using System.Collections.Generic;
using UnityEngine;

public enum AttackType { Melee, Projectile }

public class Attack : MonoBehaviour
{
    public int damage;
    public float attackRange;
    public float attackRate;
    public AttackType attackType = AttackType.Melee;

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

    [Header("命中横向震屏（opt-in）")]
    [Tooltip("仅对 Character 成功造成伤害时触发；与动画事件旧震屏独立")]
    [SerializeField] bool enableHitCameraShake;
    [SerializeField] FloatEventSO hitCameraShakeEvent;
    [SerializeField] float hitCameraShakeForce = 0.12f;

    readonly HashSet<Character> hitTargets = new();
    readonly Dictionary<Character, float> nextHitTime = new();
    readonly HashSet<IHitCountable> hitCountables = new();
    readonly Dictionary<IHitCountable, float> nextHitCountableTime = new();
    readonly HashSet<IKnockbackable> knockbackTargets = new();
    readonly List<Collider2D> overlapBuffer = new();

    void OnEnable()
    {
        hitTargets.Clear();
        nextHitTime.Clear();
        hitCountables.Clear();
        nextHitCountableTime.Clear();
        knockbackTargets.Clear();
        WakeRelatedRigidbodies();
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
        var filter = new ContactFilter2D { useTriggers = true };
        filter.SetLayerMask(Physics2D.GetLayerCollisionMask(gameObject.layer));
        col.Overlap(filter, overlapBuffer);

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

    void OnTriggerEnter2D(Collider2D collision) => TryDamage(collision);

    void OnTriggerStay2D(Collider2D collision) => TryDamage(collision);

    void TryDamage(Collider2D collision)
    {
        if (attackType == AttackType.Projectile
            && LayerMask.LayerToName(collision.gameObject.layer) == "Ground")
        {
            Destroy(gameObject);
            return;
        }

        if (!string.IsNullOrEmpty(requireTag) && !collision.CompareTag(requireTag))
            return;
        if (!string.IsNullOrEmpty(ignoreTag) && collision.CompareTag(ignoreTag))
            return;

        var target = collision.GetComponentInParent<Character>();
        bool hitSomething = false;

        if (target != null)
        {
            bool useRateLimit = attackRate > 0f;
            if (!useRateLimit && hitTargets.Contains(target))
                return;
            if (useRateLimit && nextHitTime.TryGetValue(target, out float nextHit) && Time.time < nextHit)
                return;

            bool damaged = target.TakeDamage(this);
            if (!useRateLimit)
                hitTargets.Add(target);
            if (useRateLimit)
                nextHitTime[target] = Time.time + 1f / attackRate;

            if (damaged)
                RaiseHitCameraShakeIfEnabled();

            hitSomething = true;
        }
        else
        {
            var hitCountable = collision.GetComponentInParent<IHitCountable>();
            if (hitCountable != null && CanHitCountable(hitCountable))
            {
                if (hitCountable.RegisterHit(this))
                {
                    MarkHitCountable(hitCountable);
                    hitSomething = true;
                }
            }

            if (enableKnockback && knockbackForce > 0f)
            {
                var knockable = collision.GetComponentInParent<IKnockbackable>();
                if (knockable != null && !knockbackTargets.Contains(knockable))
                {
                    knockable.ApplyKnockback(this);
                    knockbackTargets.Add(knockable);
                    hitSomething = true;
                }
            }
        }

        if (hitSomething && attackType == AttackType.Projectile)
            Destroy(gameObject);
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
