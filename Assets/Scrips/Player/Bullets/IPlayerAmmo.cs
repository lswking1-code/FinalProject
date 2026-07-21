/// <summary>
/// 玩家弹药统一初始化接口；各武器可挂不同脚本，只要实现本接口即可被射击逻辑生成。
/// </summary>
public interface IPlayerAmmo
{
    void Init(FireDir dir, float faceY, Character owner);
}
