using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 单向平台穿越：通过 Physics2D.IgnoreCollision 动态控制玩家与单向平台的碰撞。
/// 平台在「从下方/侧方上升且脚底未过顶面」时对玩家等同于空气；
/// 从上方落下、站在平台上、或在平台顶起跳时保持碰撞。
/// 由 PlayerMovement 在 FixedUpdate 物理步进前调用 UpdateCollisions()。
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(PhysicsCheck))]
[RequireComponent(typeof(CapsuleCollider2D))]
public class PlatformDropThrough : MonoBehaviour
{
    const int OverlapBufferSize = 16;

    [Header("单向平台判定")]
    [Tooltip("脚底需高于平台顶面多少才算「越过表面」，避免贴边抖动")]
    [SerializeField] float surfaceMargin = 0.05f;
    [Tooltip("仅扫描此层的碰撞体；留空则默认 Platform 层")]
    [SerializeField] LayerMask oneWayPlatformLayer;

    [Header("主动下穿（下 + 跳）")]
    [Tooltip("脚底低于平台底边多少时认为已完全穿过，恢复碰撞")]
    [SerializeField] float dropThroughResetMargin = 0.05f;
    [Tooltip("下穿超时兜底（秒），防止卡死时永久忽略碰撞")]
    [SerializeField] float dropThroughTimeout = 1f;

    PhysicsCheck physicsCheck;
    PlayerMovement playerMovement;
    PlayerAnimBase playerAnim;
    Rigidbody2D rb;
    CapsuleCollider2D capsuleCollider;

    readonly Collider2D[] overlapBuffer = new Collider2D[OverlapBufferSize];
    readonly HashSet<Collider2D> trackedPlatforms = new HashSet<Collider2D>();
    ContactFilter2D platformFilter;

    // 下+跳触发的强制下穿目标；期间对该平台始终忽略碰撞
    Collider2D activeDropPlatform;
    float dropThroughTimer;

    public bool IsDroppingThrough => activeDropPlatform != null;

    void Awake()
    {
        physicsCheck = GetComponent<PhysicsCheck>();
        playerMovement = GetComponent<PlayerMovement>();
        playerAnim = GetComponent<PlayerAnimBase>();
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

    /// <summary>
    /// 在 FixedUpdate 物理步进前调用：扫描与玩家重叠的单向平台，按规则忽略/恢复碰撞。
    /// </summary>
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

            if (!IsOneWayPlatform(col))
                continue;

            activeThisFrame.Add(col);
            SetCollisionIgnored(col, !ShouldCollide(col, playerFeet, feetPos, vy));
            trackedPlatforms.Add(col);
        }

        // 离开重叠范围的平台需恢复碰撞，避免 IgnoreCollision 残留
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

    /// <summary>
    /// 尝试从所站单向平台主动下落（下方向 + 跳跃）。成功时由 PlayerMovement 消费跳跃缓冲。
    /// </summary>
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

    /// <summary>
    /// 单向平台当前是否应对玩家产生碰撞（供 PhysicsCheck 过滤地面射线与贴墙判定）。
    /// 非单向平台始终返回 true，不影响普通 Ground。
    /// </summary>
    public bool ShouldCollideWith(Collider2D platform)
    {
        if (platform == null || !IsOneWayPlatform(platform))
            return true;

        return ShouldCollideForPhysics(platform);
    }

    /// <summary>
    /// 供 PhysicsCheck 地面射线过滤：斜坡在可站立范围内始终视为实体地面。
    /// </summary>
    public bool ShouldCountAsGround(Collider2D platform, Vector2 hitNormal)
    {
        if (platform == null || !IsOneWayPlatform(platform))
            return true;

        var slope = platform.GetComponent<SlopeOneWayPlatform>();
        if (slope == null)
            return ShouldCollideForPhysics(platform);

        if (hitNormal.y <= 0.5f)
            return false;

        Vector2 feetPos = GetFeetPosition();
        return slope.IsFeetAboveSurface(feetPos);
    }

    Vector2 GetFeetPosition() =>
        new Vector2(capsuleCollider.bounds.center.x, capsuleCollider.bounds.min.y);

    bool ShouldCollideForPhysics(Collider2D platform)
    {
        Vector2 feetPos = GetFeetPosition();
        return ShouldCollide(platform, capsuleCollider.bounds.min.y, feetPos, rb.linearVelocity.y);
    }

    /// <summary>
    /// 核心判定：区分「从下方/侧方穿过」与「从上方落下/站在平台上/在平台顶起跳」。
    /// 旧逻辑要求脚底始终高于顶面才碰撞，落地穿透顶面那一帧会关闭碰撞导致穿板。
    /// </summary>
    bool ShouldCollide(Collider2D platform, float playerFeet, Vector2 feetPos, float vy)
    {
        if (activeDropPlatform == platform)
            return false;

        var slope = platform.GetComponent<SlopeOneWayPlatform>();
        if (slope != null)
            return ShouldCollideWithSlope(slope, feetPos, vy);

        float platformTop = platform.bounds.max.y;
        float platformBottom = platform.bounds.min.y;

        // 在平台下方：视为空气（从底下往上跳、从侧下方接近）
        if (playerFeet < platformBottom - surfaceMargin)
            return false;

        // 上升且脚底尚未越过顶面：从下方/侧方上穿
        if (vy > 0f && playerFeet < platformTop - surfaceMargin)
            return false;

        // 下落且已进入平台垂直范围（含压过顶面的落地帧）
        if (vy <= 0f && playerFeet >= platformBottom - surfaceMargin)
            return true;

        // 在平台顶面起跳：保持碰撞，避免在平台上跳时穿板
        return playerFeet >= platformTop - surfaceMargin;
    }

    bool ShouldCollideWithSlope(
        SlopeOneWayPlatform slope,
        Vector2 feetPos,
        float vy)
    {
        float margin = slope.SurfaceMargin;
        float standMargin = slope.StandMargin;
        float signedDist = slope.GetSignedDistanceToSurface(feetPos);
        bool baseCollide = ComputeSlopeOneWayCollision(signedDist, vy, margin, standMargin);

        bool onHorizontalGround = physicsCheck.isGround && physicsCheck.groundNormal.y > 0.9f;
        if (!onHorizontalGround || playerMovement == null)
            return baseCollide;

        float inputThreshold = playerMovement.InputThreshold;
        Vector2 moveInput = playerMovement.MoveInput;
        float moveX = Mathf.Abs(moveInput.x) > inputThreshold ? Mathf.Sign(moveInput.x) : 0f;
        if (Mathf.Approximately(moveX, 0f))
            return baseCollide;

        bool isCrouching = playerAnim != null && playerAnim.IsCrouching;
        Vector2 horizontalMove = new Vector2(moveX, 0f);

        if (slope.IsInBottomJunction(feetPos))
        {
            float towardAscent = Vector2.Dot(horizontalMove, slope.AscentDirection);
            if (towardAscent > inputThreshold)
                return !isCrouching;
        }

        if (slope.IsInTopJunction(feetPos))
        {
            float towardDescent = Vector2.Dot(horizontalMove, -slope.AscentDirection);
            if (towardDescent > inputThreshold)
                return isCrouching;
        }

        return baseCollide;
    }

    /// <summary>
    /// 斜坡单向：可站立区始终碰撞（含起跳）；从下方上升可穿越；落下可站立。
    /// </summary>
    static bool ComputeSlopeOneWayCollision(float signedDist, float vy, float margin, float standMargin)
    {
        if (signedDist < -(margin + standMargin))
            return false;

        // 可站立区：保持碰撞，站上起跳不会穿坡
        if (signedDist >= -standMargin)
            return true;

        // 坡体内部：上升穿越，下落接住
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

        RaycastHit2D hit = Physics2D.CircleCast(
            origin, 0.1f, Vector2.down, castDistance, physicsCheck.groundLayer);

        if (hit.collider == null || hit.normal.y <= 0.5f)
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

        if (!IsColliderAlive(activeDropPlatform))
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

        float playerFeet = capsuleCollider.bounds.min.y;
        var slope = activeDropPlatform.GetComponent<SlopeOneWayPlatform>();
        if (slope != null)
        {
            Vector2 feetPos = new Vector2(capsuleCollider.bounds.center.x, playerFeet);
            float clearMargin = dropThroughResetMargin + slope.StandMargin * 0.5f;
            if (slope.GetSignedDistanceToSurface(feetPos) < -clearMargin)
                ResetDropThrough();
            return;
        }

        float platformBottom = activeDropPlatform.bounds.min.y;
        if (playerFeet < platformBottom - dropThroughResetMargin)
            ResetDropThrough();
    }

    void SetCollisionIgnored(Collider2D platform, bool ignore)
    {
        if (!IsColliderAlive(capsuleCollider) || !IsColliderAlive(platform))
            return;

        Physics2D.IgnoreCollision(capsuleCollider, platform, ignore);
    }

    static bool IsColliderAlive(Collider2D col) => col != null;

    /// <summary>
    /// 带 PlatformEffector2D 且启用 One Way 的碰撞体视为单向平台。
    /// </summary>
    static bool IsOneWayPlatform(Collider2D col)
    {
        var effector = col.GetComponent<PlatformEffector2D>();
        return effector != null && effector.useOneWay;
    }

    void ResetDropThrough()
    {
        if (IsColliderAlive(activeDropPlatform))
            SetCollisionIgnored(activeDropPlatform, false);

        activeDropPlatform = null;
        dropThroughTimer = 0f;
    }

    void OnDisable()
    {
        foreach (Collider2D tracked in trackedPlatforms)
        {
            if (IsColliderAlive(tracked))
                SetCollisionIgnored(tracked, false);
        }

        trackedPlatforms.Clear();
        ResetDropThrough();
    }
}
