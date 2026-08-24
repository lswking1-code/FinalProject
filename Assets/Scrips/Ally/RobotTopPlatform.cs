using UnityEngine;

/// <summary>
/// 机器人 / 装甲车顶部单向平台标记。玩家侧由 PlayerMovement 叠加本平台速度实现移动跟随。
/// 碰撞过滤依赖 RobotTop 层矩阵（仅与 Player 碰撞）。
/// </summary>
[RequireComponent(typeof(BoxCollider2D))]
[RequireComponent(typeof(PlatformEffector2D))]
public class RobotTopPlatform : MonoBehaviour, IPlatformVelocityProvider
{
    Rigidbody2D parentRb;

    /// <summary>父级机器人当前速度，供玩家移动平台携带使用。</summary>
    public Vector2 PlatformVelocity =>
        parentRb != null ? parentRb.linearVelocity : Vector2.zero;

    void Awake()
    {
        parentRb = GetComponentInParent<Rigidbody2D>();
    }
}
