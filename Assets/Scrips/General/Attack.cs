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

        var target = collision.GetComponent<Character>();
        if (target == null || hitTargets.Contains(target))
            return;
        if (attackRate > 0f && nextHitTime.TryGetValue(target, out float nextHit) && Time.time < nextHit)
            return;

        target.TakeDamage(this);
        hitTargets.Add(target);
        if (attackRate > 0f)
            nextHitTime[target] = Time.time + 1f / attackRate;

        if (attackType == AttackType.Projectile)
            Destroy(gameObject);
    }
}
