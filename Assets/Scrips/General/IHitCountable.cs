/// <summary>
/// 按命中次数计数的目标（忽略伤害数值，如可破坏场景物）。
/// </summary>
public interface IHitCountable
{
    /// <summary>登记一次命中，忽略伤害数值。返回是否已接受本次命中。</summary>
    bool RegisterHit(Attack attacker);
}
