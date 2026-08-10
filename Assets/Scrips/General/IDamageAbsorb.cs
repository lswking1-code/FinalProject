/// <summary>
/// 伤害吸收钩子：在 Character 扣血前拦截（如敌人护盾）。
/// </summary>
public interface IDamageAbsorb
{
    /// <returns>true 表示已吸收，不再对 Character 造成伤害</returns>
    bool TryAbsorb(Attack attacker);
}
