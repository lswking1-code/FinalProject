using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 平地单向平台穿越：Physics2D.IgnoreCollision 控制与 Platform 层单向板的碰撞。
/// 分层斜坡（Terrain_Upper）由 LayeredPathGate 负责，本组件不再处理姿势交界。
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

    [Header("主动下穿（下 + 跳）")]
    [SerializeField] float dropThroughResetMargin = 0.05f;
    [SerializeField] float dropThroughTimeout = 1f;

    PhysicsCheck physicsCheck;
    Rigidbody2D rb;
    CapsuleCollider2D capsuleCollider;

    readonly Collider2D[] overlapBuffer = new Collider2D[OverlapBufferSize];
    readonly HashSet<Collider2D> trackedPlatforms = new HashSet<Collider2D>();
    ContactFilter2D platformFilter;

    Collider2D activeDropPlatform;
    float dropThroughTimer;

    public bool IsDroppingThrough => activeDropPlatform != null;

    void Awake()
    {
        physicsCheck = GetComponent<PhysicsCheck>();
        rb = GetComponent<Rigidbody2D>();
        capsuleCollider = GetComponent<CapsuleCollider2D>();

        if (oneWayPlatformLayer.value == 0)
            oneWayPlatformLayer = LayerMask.GetMask("Platform");

        platformFilter = new ContactFilter2D
        {
            useLayerMask = true,
            layerMask = oneWayPlatformLayer,
            useTriggers = false
        };
    }

    public void UpdateCollisions()
    {
        UpdateDropThroughState();

        float playerFeet = capsuleCollider.bounds.min.y;
        Vector2 feetPos = new Vector2(capsuleCollider.bounds.center.x, playerFeet);
        float vy = rb.linearVelocity.y;
        var activeThisFrame = new HashSet<Collider2D>();

        int count = capsuleCollider.Overlap(platformFilter, overlapBuffer);
        for (int i = 0; i < count; i++)
        {
            Collider2D col = overlapBuffer[i];
            if (col == null || col == capsuleCollider)
                continue;

            // 分层斜坡交给 LayeredPathGate
            if (col.GetComponent<SlopePathSegment>() != null
                || col.GetComponentInParent<SlopePathSegment>() != null)
                continue;

            if (!IsOneWayPlatform(col))
                continue;

            activeThisFrame.Add(col);
            SetCollisionIgnored(col, !ShouldCollide(col, playerFeet, feetPos, vy));
            trackedPlatforms.Add(col);
        }

        var toRemove = new List<Collider2D>();
        foreach (Collider2D tracked in trackedPlatforms)
        {
            if (tracked == null)
            {
                toRemove.Add(tracked);
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
        if (platform == null)
            return true;

        if (platform.GetComponent<SlopePathSegment>() != null
            || platform.GetComponentInParent<SlopePathSegment>() != null)
            return !Physics2D.GetIgnoreCollision(capsuleCollider, platform);

        if (!IsOneWayPlatform(platform))
            return true;

        return ShouldCollideForPhysics(platform);
    }

    public bool ShouldCountAsGround(Collider2D platform, Vector2 hitNormal)
    {
        if (platform == null)
            return true;

        var pathSlope = platform.GetComponent<SlopePathSegment>()
            ?? platform.GetComponentInParent<SlopePathSegment>();
        if (pathSlope != null)
        {
            if (hitNormal.y <= 0.5f)
                return false;
            if (Physics2D.GetIgnoreCollision(capsuleCollider, pathSlope.UpperCollider))
                return false;
            return pathSlope.IsFeetAboveSurface(GetFeetPosition());
        }

        if (!IsOneWayPlatform(platform))
            return true;

        if (hitNormal.y <= 0.5f)
            return false;

        var legacySlope = platform.GetComponent<SlopeOneWayPlatform>();
        if (legacySlope != null)
            return legacySlope.IsFeetAboveSurface(GetFeetPosition());

        return ShouldCollideForPhysics(platform);
    }

    Vector2 GetFeetPosition() =>
        new Vector2(capsuleCollider.bounds.center.x, capsuleCollider.bounds.min.y);

    bool ShouldCollideForPhysics(Collider2D platform)
    {
        Vector2 feetPos = GetFeetPosition();
        return ShouldCollide(platform, capsuleCollider.bounds.min.y, feetPos, rb.linearVelocity.y);
    }

    bool ShouldCollide(Collider2D platform, float playerFeet, Vector2 feetPos, float vy)
    {
        if (activeDropPlatform == platform)
            return false;

        // 遗留厚盒斜坡：仅单向基线，无姿势交界
        var legacySlope = platform.GetComponent<SlopeOneWayPlatform>();
        if (legacySlope != null)
        {
            float signedDist = legacySlope.GetSignedDistanceToSurface(feetPos);
            return ComputeSlopeOneWayCollision(
                signedDist, vy, legacySlope.SurfaceMargin, legacySlope.StandMargin);
        }

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

    static bool ComputeSlopeOneWayCollision(float signedDist, float vy, float margin, float standMargin)
    {
        if (signedDist < -(margin + standMargin))
            return false;
        if (signedDist >= -standMargin)
            return true;
        if (vy > 0.15f)
            return false;
        return true;
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

        LayerMask mask = oneWayPlatformLayer.value != 0
            ? oneWayPlatformLayer
            : physicsCheck.groundLayer;
        RaycastHit2D hit = Physics2D.CircleCast(
            origin, 0.1f, Vector2.down, castDistance, mask);

        if (hit.collider == null || hit.normal.y <= 0.5f)
            return false;

        if (hit.collider.GetComponent<SlopePathSegment>() != null
            || hit.collider.GetComponentInParent<SlopePathSegment>() != null)
            return false;

        if (!IsOneWayPlatform(hit.collider))
            return false;

        platformCollider = hit.collider;
        return true;
    }

    void UpdateDropThroughState()
    {
        if (activeDropPlatform == null)
            return;

        dropThroughTimer -= Time.fixedDeltaTime;
        if (dropThroughTimer <= 0f)
        {
            ResetDropThrough();
            return;
        }

        float playerFeet = capsuleCollider.bounds.min.y;
        float platformBottom = activeDropPlatform.bounds.min.y;
        bool clearOfPlatform = playerFeet < platformBottom - dropThroughResetMargin;

        var legacySlope = activeDropPlatform.GetComponent<SlopeOneWayPlatform>();
        if (legacySlope != null)
        {
            Vector2 feetPos = GetFeetPosition();
            float signedDist = legacySlope.GetSignedDistanceToSurface(feetPos);
            float airClear = legacySlope.SurfaceMargin + legacySlope.StandMargin + dropThroughResetMargin;
            clearOfPlatform = clearOfPlatform || signedDist < -airClear;
        }

        if (clearOfPlatform)
            ResetDropThrough();
    }

    void SetCollisionIgnored(Collider2D platform, bool ignore)
    {
        if (capsuleCollider == null || platform == null)
            return;
        Physics2D.IgnoreCollision(capsuleCollider, platform, ignore);
    }

    static bool IsOneWayPlatform(Collider2D col)
    {
        var effector = col.GetComponent<PlatformEffector2D>();
        return effector != null && effector.useOneWay;
    }

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
        ResetDropThrough();
    }
}
