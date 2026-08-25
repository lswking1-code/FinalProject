using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 单向平台穿越：通过 Physics2D.IgnoreCollision 控制从下穿过/站上/主动下穿。
/// 识别条件为启用中的 PlatformEffector2D.useOneWay（含勾选 oneWay 的斜坡）。
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(PhysicsCheck))]
[RequireComponent(typeof(CapsuleCollider2D))]
public class PlatformDropThrough : MonoBehaviour
{
    const int OverlapBufferSize = 16;

    [Header("单向平台判定")]
    [SerializeField] float surfaceMargin = 0.05f;
    [SerializeField] LayerMask oneWayPlatformLayer;
    [Tooltip("钩锁等强制穿透时，相对身体扩大的预扫描，避免 MovePosition 撞板前未 Ignore")]
    [SerializeField] float forcePassScanPadding = 0.75f;

    [Header("主动下穿（下 + 跳）")]
    [SerializeField] float dropThroughResetMargin = 0.05f;
    [SerializeField] float dropThroughTimeout = 1f;

    PhysicsCheck physicsCheck;
    PlayerMovement playerMovement;
    Rigidbody2D rb;
    CapsuleCollider2D capsuleCollider;

    readonly Collider2D[] overlapBuffer = new Collider2D[OverlapBufferSize];
    readonly HashSet<Collider2D> trackedPlatforms = new HashSet<Collider2D>();
    readonly HashSet<Collider2D> forcePassLinger = new HashSet<Collider2D>();
    ContactFilter2D platformFilter;

    Collider2D activeDropPlatform;
    float dropThroughTimer;
    bool forcePassOneWay;

    public bool IsDroppingThrough => activeDropPlatform != null;
    public bool IsForcePassingOneWay => forcePassOneWay;

    void Awake()
    {
        physicsCheck = GetComponent<PhysicsCheck>();
        playerMovement = GetComponent<PlayerMovement>();
        rb = GetComponent<Rigidbody2D>();
        capsuleCollider = GetComponent<CapsuleCollider2D>();

        if (oneWayPlatformLayer.value == 0)
            oneWayPlatformLayer = LayerMask.GetMask("Platform");

        // Platform 层 + 地面层：斜坡可能不在 Platform 层上
        LayerMask mask = oneWayPlatformLayer;
        if (physicsCheck != null && physicsCheck.groundLayer.value != 0)
            mask |= physicsCheck.groundLayer;

        platformFilter = new ContactFilter2D
        {
            useLayerMask = true,
            layerMask = mask,
            useTriggers = false
        };
    }

    public void SetForcePassOneWay(bool forcePass)
    {
        if (forcePassOneWay == forcePass)
        {
            if (forcePass)
                UpdateCollisions();
            return;
        }

        if (!forcePass)
        {
            foreach (Collider2D tracked in trackedPlatforms)
            {
                if (tracked != null)
                    forcePassLinger.Add(tracked);
            }
        }
        else
        {
            forcePassLinger.Clear();
        }

        forcePassOneWay = forcePass;
        UpdateCollisions();
    }

    public void UpdateCollisions()
    {
        if (capsuleCollider == null)
            return;

        UpdateDropThroughState();

        float playerFeet = capsuleCollider.bounds.min.y;
        Vector2 feetPos = new Vector2(capsuleCollider.bounds.center.x, playerFeet);
        float vy = rb.linearVelocity.y;
        var activeThisFrame = new HashSet<Collider2D>();

        int count = CollectNearbyPlatforms();
        for (int i = 0; i < count; i++)
        {
            Collider2D col = overlapBuffer[i];
            if (col == null || col == capsuleCollider)
                continue;
            if (!IsOneWayPlatform(col))
                continue;

            activeThisFrame.Add(col);
            bool collide = ShouldCollideThisFrame(col, playerFeet, feetPos, vy);
            SetCollisionIgnored(col, !collide);
            trackedPlatforms.Add(col);
        }

        var toRemove = new List<Collider2D>();
        foreach (Collider2D tracked in trackedPlatforms)
        {
            if (tracked == null)
            {
                toRemove.Add(tracked);
                forcePassLinger.Remove(tracked);
                continue;
            }

            if (activeThisFrame.Contains(tracked))
                continue;

            SetCollisionIgnored(tracked, false);
            forcePassLinger.Remove(tracked);
            toRemove.Add(tracked);
        }

        for (int i = 0; i < toRemove.Count; i++)
            trackedPlatforms.Remove(toRemove[i]);
    }

    int CollectNearbyPlatforms()
    {
        if (!forcePassOneWay || forcePassScanPadding <= 0.001f)
            return capsuleCollider.Overlap(platformFilter, overlapBuffer);

        Bounds bounds = capsuleCollider.bounds;
        Vector2 size = (Vector2)bounds.size + Vector2.one * (forcePassScanPadding * 2f);
        return Physics2D.OverlapBox(bounds.center, size, 0f, platformFilter, overlapBuffer);
    }

    bool ShouldCollideThisFrame(Collider2D col, float playerFeet, Vector2 feetPos, float vy)
    {
        if (forcePassOneWay)
            return false;

        if (forcePassLinger.Contains(col) && IsEmbeddedInOneWay(col, playerFeet, feetPos))
            return false;

        forcePassLinger.Remove(col);
        return ShouldCollide(col, playerFeet, feetPos, vy);
    }

    public bool TryBeginDropThrough(Vector2 moveInput, float inputThreshold)
    {
        if (activeDropPlatform != null)
            return false;
        if (moveInput.y >= -inputThreshold)
            return false;
        if (!TryGetOneWayPlatformBelow(out Collider2D platformCollider))
            return false;

        activeDropPlatform = platformCollider;
        dropThroughTimer = dropThroughTimeout;
        SetCollisionIgnored(platformCollider, true);
        return true;
    }

    public bool ShouldCollideWith(Collider2D platform)
    {
        if (platform == null || !IsOneWayPlatform(platform))
            return true;

        if (forcePassOneWay)
            return false;

        Vector2 feetPos = GetFeetPosition();
        if (forcePassLinger.Contains(platform) && IsEmbeddedInOneWay(platform, capsuleCollider.bounds.min.y, feetPos))
            return false;

        return ShouldCollide(platform, capsuleCollider.bounds.min.y, feetPos, rb.linearVelocity.y);
    }

    public bool ShouldCountAsGround(Collider2D platform, Vector2 hitNormal)
    {
        if (platform == null || !IsOneWayPlatform(platform))
            return true;

        if (forcePassOneWay)
            return false;

        if (hitNormal.y <= 0.5f)
            return false;

        var pathSlope = platform.GetComponent<SlopePathSegment>()
            ?? platform.GetComponentInParent<SlopePathSegment>();
        if (pathSlope != null)
            return pathSlope.IsFeetAboveSurface(GetFeetPosition());

        var legacySlope = platform.GetComponent<SlopeOneWayPlatform>()
            ?? platform.GetComponentInParent<SlopeOneWayPlatform>();
        if (legacySlope != null)
            return legacySlope.IsFeetAboveSurface(GetFeetPosition());

        return ShouldCollideWith(platform);
    }

    Vector2 GetFeetPosition() =>
        new Vector2(capsuleCollider.bounds.center.x, capsuleCollider.bounds.min.y);

    bool IsEmbeddedInOneWay(Collider2D platform, float playerFeet, Vector2 feetPos)
    {
        var pathSlope = platform.GetComponent<SlopePathSegment>()
            ?? platform.GetComponentInParent<SlopePathSegment>();
        if (pathSlope != null)
            return !pathSlope.IsFeetAboveSurface(feetPos);

        var legacySlope = platform.GetComponent<SlopeOneWayPlatform>()
            ?? platform.GetComponentInParent<SlopeOneWayPlatform>();
        if (legacySlope != null)
            return !legacySlope.IsFeetAboveSurface(feetPos);

        return playerFeet < platform.bounds.max.y - surfaceMargin;
    }

    bool ShouldCollide(Collider2D platform, float playerFeet, Vector2 feetPos, float vy)
    {
        if (activeDropPlatform == platform)
            return false;

        var pathSlope = platform.GetComponent<SlopePathSegment>()
            ?? platform.GetComponentInParent<SlopePathSegment>();
        if (pathSlope != null)
            return ShouldCollideWithSlopeSurface(pathSlope.IsFeetAboveSurface(feetPos), vy);

        var legacySlope = platform.GetComponent<SlopeOneWayPlatform>()
            ?? platform.GetComponentInParent<SlopeOneWayPlatform>();
        if (legacySlope != null)
            return ShouldCollideWithSlopeSurface(legacySlope.IsFeetAboveSurface(feetPos), vy);

        float platformTop = platform.bounds.max.y;
        float platformBottom = platform.bounds.min.y;

        if (playerFeet < platformBottom - surfaceMargin)
            return false;
        if (vy > 0f && playerFeet < platformTop - surfaceMargin)
            return false;
        if (vy <= 0f && playerFeet >= platformBottom - surfaceMargin)
            return true;
        return playerFeet >= platformTop - surfaceMargin;
    }

    /// <summary>
    /// 从下穿过仍要求 vy≤0；已经站在坡上时切向速度会带正 vy，不能当成穿过。
    /// </summary>
    bool ShouldCollideWithSlopeSurface(bool feetAboveSurface, float vy)
    {
        if (!feetAboveSurface)
            return false;

        bool slopeAttached = playerMovement == null || !playerMovement.IsSlopeDetached;
        if (slopeAttached
            && physicsCheck != null
            && (physicsCheck.isOnSlope || physicsCheck.WasOnSlopeRecently))
            return true;

        return vy <= 0.01f;
    }

    bool TryGetOneWayPlatformBelow(out Collider2D platformCollider)
    {
        platformCollider = null;

        float facing = Mathf.Sign(transform.localScale.x);
        if (Mathf.Approximately(facing, 0f))
            facing = 1f;

        Vector2 origin = (Vector2)transform.position
            + new Vector2(physicsCheck.bottomOffset.x * facing, physicsCheck.bottomOffset.y);

        float castDistance = physicsCheck.checkRaduis + 0.05f;
        if (physicsCheck.isOnSlope)
            castDistance += 0.4f;

        LayerMask mask = platformFilter.layerMask;
        RaycastHit2D hit = Physics2D.CircleCast(
            origin, 0.1f, Vector2.down, castDistance, mask);

        if (hit.collider == null || hit.normal.y <= 0.5f)
            return false;
        if (!IsOneWayPlatform(hit.collider))
            return false;

        // 站在斜坡下方重叠的平地上时，向下扫到的可能是斜坡本身，不能当成下穿。
        if (!IsFeetOnWalkableSide(hit.collider, GetFeetPosition()))
            return false;

        platformCollider = hit.collider;
        return true;
    }

    void UpdateDropThroughState()
    {
        if (activeDropPlatform == null)
            return;

        if (!activeDropPlatform)
        {
            activeDropPlatform = null;
            dropThroughTimer = 0f;
            return;
        }

        dropThroughTimer -= Time.fixedDeltaTime;
        if (dropThroughTimer <= 0f)
        {
            ResetDropThrough();
            return;
        }

        Vector2 feetPos = GetFeetPosition();
        var pathSlope = activeDropPlatform.GetComponent<SlopePathSegment>()
            ?? activeDropPlatform.GetComponentInParent<SlopePathSegment>();
        if (pathSlope != null)
        {
            // 旋转斜坡的 AABB 远大于薄面，用包围盒底边永远等不到复位。
            if (pathSlope.GetSignedDistanceToSurface(feetPos) < -pathSlope.StandMargin)
                ResetDropThrough();
            return;
        }

        var legacySlope = activeDropPlatform.GetComponent<SlopeOneWayPlatform>()
            ?? activeDropPlatform.GetComponentInParent<SlopeOneWayPlatform>();
        if (legacySlope != null)
        {
            if (legacySlope.GetSignedDistanceToSurface(feetPos) < -legacySlope.StandMargin)
                ResetDropThrough();
            return;
        }

        float playerFeet = feetPos.y;
        float platformBottom = activeDropPlatform.bounds.min.y;
        if (playerFeet < platformBottom - dropThroughResetMargin)
            ResetDropThrough();
    }

    static bool IsFeetOnWalkableSide(Collider2D platform, Vector2 feetPos)
    {
        var pathSlope = platform.GetComponent<SlopePathSegment>()
            ?? platform.GetComponentInParent<SlopePathSegment>();
        if (pathSlope != null)
            return pathSlope.IsFeetAboveSurface(feetPos);

        var legacySlope = platform.GetComponent<SlopeOneWayPlatform>()
            ?? platform.GetComponentInParent<SlopeOneWayPlatform>();
        if (legacySlope != null)
            return legacySlope.IsFeetAboveSurface(feetPos);

        return true;
    }

    void SetCollisionIgnored(Collider2D platform, bool ignore)
    {
        if (capsuleCollider == null || platform == null)
            return;
        Physics2D.IgnoreCollision(capsuleCollider, platform, ignore);
    }

    static bool IsOneWayPlatform(Collider2D col) =>
        FallingPlatform.IsOneWayPlatformCollider(col);

    void ResetDropThrough()
    {
        if (activeDropPlatform != null)
            SetCollisionIgnored(activeDropPlatform, false);
        activeDropPlatform = null;
        dropThroughTimer = 0f;
    }

    void OnDisable()
    {
        foreach (Collider2D tracked in trackedPlatforms)
        {
            if (tracked != null)
                SetCollisionIgnored(tracked, false);
        }
        trackedPlatforms.Clear();
        forcePassLinger.Clear();
        forcePassOneWay = false;
        ResetDropThrough();
    }
}

































