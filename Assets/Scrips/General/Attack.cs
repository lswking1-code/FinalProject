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
    [Tooltip("开启后对受击 Character 施加水平击退")]
    public bool enableKnockback;
    [Tooltip("击退冲量大小（Dynamic 为 Impulse；Kinematic 为位移总量）")]
    public float knockbackForce;
    [Tooltip("玩家水平移动门控时长 / Kinematic 位移时长（秒）")]
    public float knockbackDuration = 0.15f;

    readonly HashSet<Character> hitTargets = new();
    readonly Dictionary<Character, float> nextHitTime = new();

    void OnEnable() => hitTargets.Clear();

    void OnTriggerEnter2D(Collider2D collision) => TryDamage(collision);

    void OnTriggerStay2D(Collider2D collision) => TryDamage(collision);

    void TryDamage(Collider2D collision)
    {
        if (!string.IsNullOrEmpty(requireTag) && !collision.CompareTag(requireTag))
            return;
        if (!string.IsNullOrEmpty(ignoreTag) && collision.CompareTag(ignoreTag))
            return;

        var target = collision.GetComponentInParent<Character>();
        if (target == null)
            return;

        bool useRateLimit = attackRate > 0f;
        if (!useRateLimit && hitTargets.Contains(target))
            return;
        if (useRateLimit && nextHitTime.TryGetValue(target, out float nextHit) && Time.time < nextHit)
            return;

        target.TakeDamage(this);
        if (!useRateLimit)
            hitTargets.Add(target);
        if (useRateLimit)
            nextHitTime[target] = Time.time + 1f / attackRate;

        if (attackType == AttackType.Projectile)
            Destroy(gameObject);
    }
}
