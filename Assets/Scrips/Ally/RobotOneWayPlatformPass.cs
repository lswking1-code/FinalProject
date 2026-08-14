using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 友军机器人单向平台穿越：冲锋（ComboDashing）期间强制 IgnoreCollision；
/// 平时按与玩家相同的脚底 / 竖直速度规则上升穿透、下落站板。
/// 由 AllyRobot 在 FixedUpdate 物理步进前调用 UpdateCollisions()。
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(CapsuleCollider2D))]
[RequireComponent(typeof(AllyRobot))]
public class RobotOneWayPlatformPass : MonoBehaviour
{
    const int OverlapBufferSize = 16;

    [Header("单向平台判定")]
    [Tooltip("脚底需高于平台顶面多少才算「越过表面」，避免贴边抖动")]
    [SerializeField] float surfaceMargin = 0.05f;
    [Tooltip("仅扫描此层的碰撞体；留空则默认 Platform 层")]
    [SerializeField] LayerMask oneWayPlatformLayer;
    [Tooltip("冲锋时相对身体扩大的预扫描，避免高速撞板前未 Ignore")]
    [SerializeField] float dashScanPadding = 1.25f;

    AllyRobot allyRobot;
    Rigidbody2D rb;
    CapsuleCollider2D capsuleCollider;

    readonly Collider2D[] overlapBuffer = new Collider2D[OverlapBufferSize];
    readonly HashSet<Collider2D> trackedPlatforms = new HashSet<Collider2D>();
    readonly HashSet<Collider2D> ignoredPlatforms = new HashSet<Collider2D>();
    ContactFilter2D platformFilter;

    void Awake()
    {
        allyRobot = GetComponent<AllyRobot>();
        rb = GetComponent<Rigidbody2D>();
        capsuleCollider = GetComponent<CapsuleCollider2D>();

        if (oneWayPlatformLayer.value == 0)
            oneWayPlatformLayer = LayerMask.GetMask("Platform");

        platformFilter = new ContactFilter2D
        {
            useLayerMask = true,
            layerMask = oneWayPlatformLayer,
            useTriggers = false,
        };
    }

    /// <summary>
    /// 在 FixedUpdate 物理步进前调用。
    /// </summary>
    public void UpdateCollisions()
    {
        if (capsuleCollider == null || rb == null)
            return;

        platformFilter.layerMask = oneWayPlatformLayer;

        float feet = capsuleCollider.bounds.min.y;
        Vector2 feetPos = new Vector2(capsuleCollider.bounds.center.x, feet);
        float vy = rb.linearVelocity.y;
        bool forcePass = allyRobot != null && allyRobot.IsComboDashing;

        var activeThisFrame = new HashSet<Collider2D>();
        int count = CollectNearbyOneWayPlatforms(forcePass);

        for (int i = 0; i < count; i++)
        {
            Collider2D col = overlapBuffer[i];
            if (!IsColliderAlive(col) || col == capsuleCollider)
                continue;
            if (!IsOneWayPlatform(col))
                continue;
            // 分层斜坡不走单向板 Ignore，避免 AABB 规则在坡面上闪烁
            if (IsLayeredSlope(col))
                continue;

            activeThisFrame.Add(col);
            bool shouldCollide = !forcePass && ShouldCollide(col, feet, feetPos, vy);
            SetCollisionIgnored(col, !shouldCollide);
            trackedPlatforms.Add(col);
        }

        var toRemove = new List<Collider2D>();
        foreach (Collider2D tracked in trackedPlatforms)
        {
            if (!IsColliderAlive(tracked))
            {
                toRemove.Add(tracked);
                ignoredPlatforms.Remove(tracked);
                continue;
            }

            if (activeThisFrame.Contains(tracked))
                continue;

            SetCollisionIgnored(tracked, false);
            toRemove.Add(tracked);
        }

        for (int i = 0; i < toRemove.Count; i++)
            trackedPlatforms.Remove(toRemove[i]);
    }

    /// <summary>
    /// 供 PhysicsCheck 过滤侧墙：忽略中的单向板不当实体障碍。
    /// </summary>
    public bool ShouldCollideWith(Collider2D platform)
    {
        if (platform == null || !IsOneWayPlatform(platform))
            return true;

        if (IsLayeredSlope(platform))
            return true;

        if (allyRobot != null && allyRobot.IsComboDashing)
            return false;

        if (ignoredPlatforms.Contains(platform))
            return false;

        Vector2 feetPos = GetFeetPosition();
        return ShouldCollide(platform, capsuleCollider.bounds.min.y, feetPos, rb.linearVelocity.y);
    }

    /// <summary>
    /// 供 PhysicsCheck 地面射线过滤。
    /// </summary>
    public bool ShouldCountAsGround(Collider2D platform, Vector2 hitNormal)
    {
        if (platform == null || !IsOneWayPlatform(platform))
            return true;

        if (allyRobot != null && allyRobot.IsComboDashing)
            return false;

        if (ignoredPlatforms.Contains(platform))
            return false;

        if (hitNormal.y <= 0.5f)
            return false;

        var pathSlope = platform.GetComponent<SlopePathSegment>()
            ?? platform.GetComponentInParent<SlopePathSegment>();
        if (pathSlope != null)
            return pathSlope.IsFeetAboveSurface(GetFeetPosition());

        var slope = platform.GetComponent<SlopeOneWayPlatform>();
        if (slope != null)
            return slope.IsFeetAboveSurface(GetFeetPosition());

        Vector2 feetPos = GetFeetPosition();
        return ShouldCollide(platform, capsuleCollider.bounds.min.y, feetPos, rb.linearVelocity.y);
    }

    int CollectNearbyOneWayPlatforms(bool forcePass)
    {
        if (!forcePass || dashScanPadding <= 0.001f)
            return capsuleCollider.Overlap(platformFilter, overlapBuffer);

        Bounds bounds = capsuleCollider.bounds;
        Vector2 size = (Vector2)bounds.size + Vector2.one * (dashScanPadding * 2f);
        return Physics2D.OverlapBox(bounds.center, size, 0f, platformFilter, overlapBuffer);
    }

    Vector2 GetFeetPosition() =>
        new Vector2(capsuleCollider.bounds.center.x, capsuleCollider.bounds.min.y);

    bool ShouldCollide(Collider2D platform, float feet, Vector2 feetPos, float vy)
    {
        var slope = platform.GetComponent<SlopeOneWayPlatform>();
        if (slope != null)
        {
            float margin = slope.SurfaceMargin;
            float standMargin = slope.StandMargin;
            float signedDist = slope.GetSignedDistanceToSurface(feetPos);

            if (signedDist < -(margin + standMargin))
                return false;
            if (signedDist >= -standMargin)
                return true;
            if (vy > 0.15f)
                return false;
            return true;
        }

        float platformTop = platform.bounds.max.y;
        float platformBottom = platform.bounds.min.y;

        if (feet < platformBottom - surfaceMargin)
            return false;

        if (vy > 0f && feet < platformTop - surfaceMargin)
            return false;

        if (vy <= 0f && feet >= platformBottom - surfaceMargin)
            return true;

        return feet >= platformTop - surfaceMargin;
    }

    void SetCollisionIgnored(Collider2D platform, bool ignore)
    {
        if (!IsColliderAlive(capsuleCollider) || !IsColliderAlive(platform))
            return;

        Physics2D.IgnoreCollision(capsuleCollider, platform, ignore);
        if (ignore)
            ignoredPlatforms.Add(platform);
        else
            ignoredPlatforms.Remove(platform);
    }

    static bool IsColliderAlive(Collider2D col) => col != null;

    static bool IsOneWayPlatform(Collider2D col)
    {
        var effector = col.GetComponent<PlatformEffector2D>();
        return effector != null && effector.useOneWay;
    }

    static bool IsLayeredSlope(Collider2D col)
    {
        if (col == null)
            return false;
        return col.GetComponent<SlopePathSegment>() != null
            || col.GetComponentInParent<SlopePathSegment>() != null;
    }

    void OnDisable()
    {
        foreach (Collider2D tracked in trackedPlatforms)
        {
            if (IsColliderAlive(tracked))
                Physics2D.IgnoreCollision(capsuleCollider, tracked, false);
        }

        trackedPlatforms.Clear();
        ignoredPlatforms.Clear();
    }
}
