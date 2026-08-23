/// <summary>
/// 可被玩家近战/技能判定抵销的敌人飞行道具（子弹、导弹、手雷等）。
/// </summary>
public interface IEnemyProjectileCancelable
{
    /// <returns>true 表示本次已抵销并销毁（或等效处理）</returns>
    bool TryCancelByMelee(Attack attacker);
}
