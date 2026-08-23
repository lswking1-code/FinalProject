using UnityEngine;

/// <summary>
/// 升降平台表面标记。玩家侧由 PlayerMovement 叠加本平台速度实现移动跟随。
/// </summary>
public class ReciprocatingPlatformSurface : MonoBehaviour, IPlatformVelocityProvider
{
    ReciprocatingPlatform platform;

    public Vector2 PlatformVelocity =>
        platform != null ? platform.PlatformVelocity : Vector2.zero;

    void Awake()
    {
        platform = GetComponentInParent<ReciprocatingPlatform>();
    }
}
