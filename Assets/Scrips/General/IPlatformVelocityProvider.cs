using UnityEngine;

/// <summary>
/// 移动平台表面：向玩家移动逻辑暴露当前平台速度。
/// </summary>
public interface IPlatformVelocityProvider
{
    Vector2 PlatformVelocity { get; }
}
