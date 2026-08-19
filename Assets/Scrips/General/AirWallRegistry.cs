using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 空气墙碰撞体注册表：遭遇锁区与电梯单向墙共用。
/// 双向墙命中即拦弹；单向墙仅销毁朝笼外飞的敌人弹。
/// </summary>
public static class AirWallRegistry
{
    struct Binding
    {
        public bool oneWay;
        public Collider2D cage;
    }

    static readonly Dictionary<Collider2D, Binding> s_walls = new();

    public static void Register(Collider2D wall, bool oneWay = false, Collider2D cage = null)
    {
        if (wall == null)
            return;

        s_walls[wall] = new Binding { oneWay = oneWay, cage = cage };
    }

    public static void Unregister(Collider2D wall)
    {
        if (wall == null)
            return;

        s_walls.Remove(wall);
    }

    public static bool IsAirWall(Collider2D col)
    {
        return col != null && s_walls.ContainsKey(col);
    }

    /// <summary>
    /// 子弹速度是否朝笼内。非单向墙恒为 false（命中即销毁）。
    /// </summary>
    public static bool IsInbound(Collider2D wall, Vector2 velocity, Vector2 worldPos)
    {
        if (wall == null || !s_walls.TryGetValue(wall, out Binding binding) || !binding.oneWay)
            return false;

        Vector2 center = binding.cage != null
            ? (Vector2)binding.cage.bounds.center
            : (Vector2)wall.bounds.center;
        Vector2 toCenter = center - worldPos;
        if (toCenter.sqrMagnitude < 0.0001f)
            return true;
        if (velocity.sqrMagnitude < 0.0001f)
            return false;

        return Vector2.Dot(velocity, toCenter) > 0f;
    }
}
