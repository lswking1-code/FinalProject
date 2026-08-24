using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 空气墙碰撞体注册表：遭遇锁区与电梯单向墙共用。
/// 双向墙命中即拦弹；单向墙仅销毁朝笼外飞的敌人弹。
/// </summary>
public static class AirWallRegistry
{
    const float OverlapDistance = 0.01f;
    const float InsideSkin = 0.02f;

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

    /// <summary>
    /// 敌人身体是否已完全进入空气墙笼子内侧。
    /// 用碰撞体世界包围盒相对每面墙内沿判定，叠墙时永不视为进入。
    /// </summary>
    public static bool IsBodyFullyInsideCage(
        IReadOnlyList<Collider2D> bodies,
        IReadOnlyList<Collider2D> walls,
        Collider2D cage,
        Vector2 fallbackPoint)
    {
        if (walls == null || walls.Count == 0)
            return true;

        if (IsOverlappingAnyWall(bodies, walls))
            return false;

        Vector2 cageCenter = GetCageCenter(walls, cage, fallbackPoint);
        if (bodies == null || bodies.Count == 0)
            return IsPointPastAllWalls(fallbackPoint, walls, cageCenter);

        bool anyBody = false;
        for (int i = 0; i < bodies.Count; i++)
        {
            Collider2D body = bodies[i];
            if (body == null || !body.enabled)
                continue;

            anyBody = true;
            if (!IsBoundsPastAllWalls(body.bounds, walls, cageCenter))
                return false;
        }

        if (!anyBody)
            return IsPointPastAllWalls(fallbackPoint, walls, cageCenter);

        return true;
    }

    public static bool IsOverlappingAnyWall(IReadOnlyList<Collider2D> bodies, IReadOnlyList<Collider2D> walls)
    {
        if (bodies == null || walls == null)
            return false;

        for (int b = 0; b < bodies.Count; b++)
        {
            Collider2D body = bodies[b];
            if (body == null || !body.enabled)
                continue;

            for (int w = 0; w < walls.Count; w++)
            {
                Collider2D wall = walls[w];
                if (wall == null || !wall.enabled)
                    continue;

                var distance = Physics2D.Distance(body, wall);
                if (distance.isOverlapped || distance.distance <= OverlapDistance)
                    return true;
            }
        }

        return false;
    }

    static Vector2 GetCageCenter(IReadOnlyList<Collider2D> walls, Collider2D cage, Vector2 fallback)
    {
        if (cage != null)
            return cage.bounds.center;

        Vector2 sum = Vector2.zero;
        int count = 0;
        for (int i = 0; i < walls.Count; i++)
        {
            if (walls[i] == null)
                continue;
            sum += (Vector2)walls[i].bounds.center;
            count++;
        }

        return count > 0 ? sum / count : fallback;
    }

    static bool IsBoundsPastAllWalls(Bounds body, IReadOnlyList<Collider2D> walls, Vector2 cageCenter)
    {
        for (int i = 0; i < walls.Count; i++)
        {
            Collider2D wall = walls[i];
            if (wall == null || !wall.enabled)
                continue;
            if (!IsBoundsPastWall(body, wall.bounds, cageCenter))
                return false;
        }

        return true;
    }

    static bool IsPointPastAllWalls(Vector2 point, IReadOnlyList<Collider2D> walls, Vector2 cageCenter)
    {
        var pointBounds = new Bounds(point, Vector3.zero);
        return IsBoundsPastAllWalls(pointBounds, walls, cageCenter);
    }

    static bool IsBoundsPastWall(Bounds body, Bounds wall, Vector2 cageCenter)
    {
        Vector2 toWall = (Vector2)wall.center - cageCenter;
        bool treatAsVertical = wall.size.y > wall.size.x
            || (Mathf.Abs(wall.size.y - wall.size.x) < 0.01f && Mathf.Abs(toWall.x) >= Mathf.Abs(toWall.y));

        if (treatAsVertical)
        {
            if (toWall.x < 0f)
                return body.min.x >= wall.max.x - InsideSkin;
            return body.max.x <= wall.min.x + InsideSkin;
        }

        if (toWall.y < 0f)
            return body.min.y >= wall.max.y - InsideSkin;
        return body.max.y <= wall.min.y + InsideSkin;
    }
}
