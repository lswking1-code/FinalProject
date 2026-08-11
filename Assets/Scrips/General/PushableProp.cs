using UnityEngine;

/// <summary>
/// 可被击退的场景物（箱子等），无生命值，仅响应 Attack 击退。
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
public class PushableProp : MonoBehaviour, IKnockbackable
{
    [Tooltip("击退阻力，越大越难推；默认高于敌人(1)")]
    [SerializeField] float knockbackResistance = 2.5f;

    Rigidbody2D rb;

    public float KnockbackResistance => Mathf.Max(1f, knockbackResistance);

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        rb.constraints = RigidbodyConstraints2D.FreezeRotation;
        if (rb.bodyType == RigidbodyType2D.Kinematic)
            rb.bodyType = RigidbodyType2D.Dynamic;
    }

    public void ApplyKnockback(Attack attacker)
    {
        float force = Attack.EffectiveKnockbackForce(attacker, KnockbackResistance);
        if (force <= 0f || rb == null)
            return;

        Vector2 dir = Attack.ResolveKnockbackDir(attacker, transform.position);
        rb.WakeUp();
        rb.AddForce(dir * force, ForceMode2D.Impulse);
    }
}
