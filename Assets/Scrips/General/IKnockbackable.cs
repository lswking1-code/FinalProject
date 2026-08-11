using UnityEngine;

/// <summary>
/// 可被 Attack 击退的目标（含无 Character 的场景物）。
/// </summary>
public interface IKnockbackable
{
    /// <summary>阻力系数，越大越难推；应 ≥ 1。</summary>
    float KnockbackResistance { get; }

    void ApplyKnockback(Attack attacker);
}
