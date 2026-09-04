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
    const int ProbeHitCapacity = 24;

    static readonly Collider2D[] s_probeHits = new Collider2D[ProbeHitCapacity];

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
    /// 收集敌人用于穿墙忽略的全部实体碰撞，以及用于封门的主体碰撞（根节点，排除顶板/攻击盒）。
    /// </summary>
    public static void CollectEnemyBodyColliders(
        GameObject root,
        List<Collider2D> allBodies,
        List<Collider2D> lockBodies)
    {
        allBodies?.Clear();
        lockBodies?.Clear();
        if (root == null)
            return;

        var cols = root.GetComponentsInChildren<Collider2D>(true);
        for (int i = 0; i < cols.Length; i++)
        {
            Collider2D col = cols[i];
            if (col == null || !col.enabled || col.isTrigger)
                continue;

            allBodies?.Add(col);
        }

        var rootCols = root.GetComponents<Collider2D>();
        for (int i = 0; i < rootCols.Length; i++)
        {
            Collider2D col = rootCols[i];
            if (col == null || !col.enabled || col.isTrigger || IsAuxiliaryBodyCollider(col))
                continue;

            lockBodies?.Add(col);
        }

        if (lockBodies != null && lockBodies.Count == 0 && allBodies != null)
        {
            for (int i = 0; i < allBodies.Count; i++)
            {
                Collider2D col = allBodies[i];
                if (col == null || IsAuxiliaryBodyCollider(col))
                    continue;

                lockBodies.Add(col);
                break;
            }
        }
    }

    public static Vector2 GetLockBodyCenter(IReadOnlyList<Collider2D> lockBodies, Vector2 fallback)
    {
        if (lockBodies == null)
            return fallback;

        for (int i = 0; i < lockBodies.Count; i++)
        {
            Collider2D body = lockBodies[i];
            if (body == null || !body.enabled)
                continue;
            return body.bounds.center;
        }

        return fallback;
    }

    public static Vector2 ResolveCageCenter(IReadOnlyList<Collider2D> walls, Collider2D cage, Vector2 fallback)
    {
        return GetCageCenter(walls, cage, fallback);
    }

    /// <summary>
    /// 主体中心尚未越过该面墙内沿时，允许 IgnoreCollision 以便从区外穿入。
    /// 中心一旦进笼即应恢复碰撞，宽体单位不必等整圈 AABB 完全过线。
    /// </summary>
    public static bool ShouldIgnoreWallForEntry(Vector2 bodyCenter, Collider2D wall, Vector2 cageCenter)
    {
        if (wall == null || !wall.enabled)
            return false;

        return !IsPointPastWall(bodyCenter, wall, cageCenter);
    }

    public static bool IsOverlappingWall(IReadOnlyList<Collider2D> bodies, Collider2D wall)
    {
        if (bodies == null || wall == null || !wall.enabled)
            return false;

        for (int i = 0; i < bodies.Count; i++)
        {
            Collider2D body = bodies[i];
            if (body == null || !body.enabled)
                continue;

            var distance = Physics2D.Distance(body, wall);
            if (distance.isOverlapped || distance.distance <= OverlapDistance)
                return true;
        }

        return false;
    }

    public static bool IsPointPastWall(Vector2 point, Collider2D wall, Vector2 cageCenter)
    {
        if (wall == null)
            return true;

        return IsBoundsPastWall(new Bounds(point, Vector3.zero), wall.bounds, cageCenter);
    }

    /// <summary>
    /// 将点拉回已封死墙的内侧，防止高速冲撞在 IgnoreCollision 窗口穿出后停在区外。
    /// </summary>
    public static Vector2 ClampPointPastWalls(
        Vector2 point,
        IReadOnlyList<Collider2D> walls,
        Vector2 cageCenter,
        ICollection<Collider2D> sealedWalls)
    {
        if (walls == null || walls.Count == 0)
            return point;

        Vector2 result = point;
        for (int i = 0; i < walls.Count; i++)
        {
            Collider2D wall = walls[i];
            if (wall == null || !wall.enabled)
                continue;
            if (sealedWalls != null && !sealedWalls.Contains(wall))
                continue;
            if (IsPointPastWall(result, wall, cageCenter))
                continue;

            result = ProjectPointPastWall(result, wall.bounds, cageCenter);
        }

        return result;
    }

    /// <summary>
    /// 探测盒是否碰到空气墙，且速度是朝笼外。进场穿墙（朝笼心）不阻挡。
    /// </summary>
    public static bool IsOutboundAirWallAhead(Vector2 probeCenter, Vector2 probeSize, Vector2 velocity)
    {
        if (s_walls.Count == 0 || velocity.sqrMagnitude < 0.0001f)
            return false;

        int count = Physics2D.OverlapBoxNonAlloc(probeCenter, probeSize, 0f, s_probeHits);
        for (int i = 0; i < count; i++)
        {
            Collider2D hit = s_probeHits[i];
            if (!IsAirWall(hit))
                continue;
            if (IsMovementTowardCage(hit, velocity, probeCenter))
                continue;
            return true;
        }

        return false;
    }

    static bool IsMovementTowardCage(Collider2D wall, Vector2 velocity, Vector2 worldPos)
    {
        if (wall == null || !s_walls.TryGetValue(wall, out Binding binding))
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

    static bool IsAuxiliaryBodyCollider(Collider2D col)
    {
        if (col == null)
            return true;

        if (col.GetComponent<RobotTopPlatform>() != null)
            return true;
        if (col.GetComponent<PlatformEffector2D>() != null)
            return true;
        if (col.GetComponent<Attack>() != null)
            return true;

        return false;
    }

    static Vector2 ProjectPointPastWall(Vector2 point, Bounds wall, Vector2 cageCenter)
    {
        Vector2 toWall = (Vector2)wall.center - cageCenter;
        bool treatAsVertical = wall.size.y > wall.size.x
            || (Mathf.Abs(wall.size.y - wall.size.x) < 0.01f && Mathf.Abs(toWall.x) >= Mathf.Abs(toWall.y));

        if (treatAsVertical)
        {
            if (toWall.x < 0f)
                return new Vector2(Mathf.Max(point.x, wall.max.x), point.y);
            return new Vector2(Mathf.Min(point.x, wall.min.x), point.y);
        }

        if (toWall.y < 0f)
            return new Vector2(point.x, Mathf.Max(point.y, wall.max.y));
        return new Vector2(point.x, Mathf.Min(point.y, wall.min.y));
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
