using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 合金弹头式分层路径：根据蹲/站与坡脚/坡顶交界闩锁，
/// 动态 IgnoreCollision 玩家与 Terrain_Upper（及可选 Terrain_Lower）。
/// </summary>
[RequireComponent(typeof(CapsuleCollider2D))]
[RequireComponent(typeof(PhysicsCheck))]
public class LayeredPathGate : MonoBehaviour
{
    const int OverlapBufferSize = 24;

    [SerializeField] float nearbyScanRadius = 2.5f;
    [SerializeField] LayerMask upperLayerMask;
    [SerializeField] LayerMask lowerLayerMask;

    PhysicsCheck physicsCheck;
    PlayerMovement playerMovement;
    PlayerAnimBase playerAnim;
    Rigidbody2D rb;
    CapsuleCollider2D capsuleCollider;

    readonly Collider2D[] overlapBuffer = new Collider2D[OverlapBufferSize];
    readonly HashSet<Collider2D> trackedUppers = new HashSet<Collider2D>();
    readonly HashSet<Collider2D> trackedLowers = new HashSet<Collider2D>();
    readonly Dictionary<SlopePathSegment, bool> upperPathLatch = new Dictionary<SlopePathSegment, bool>();
    /// <summary>坡脚交界内已选定下层路径，离开交界前站起不上坡。</summary>
    readonly HashSet<SlopePathSegment> bottomLowerLocked = new HashSet<SlopePathSegment>();
    /// <summary>坡顶交界内已选定上层平地路径，离开交界前不能仅靠蹲下切入斜坡。</summary>
    readonly HashSet<SlopePathSegment> topUpperFlatLocked = new HashSet<SlopePathSegment>();

    ContactFilter2D upperFilter;
    ContactFilter2D lowerFilter;

    void Awake()
    {
        physicsCheck = GetComponent<PhysicsCheck>();
        playerMovement = GetComponent<PlayerMovement>();
        playerAnim = PlayerAnimBase.Resolve(gameObject);
        rb = GetComponent<Rigidbody2D>();
        capsuleCollider = GetComponent<CapsuleCollider2D>();

        if (upperLayerMask.value == 0)
            upperLayerMask = LayerMask.GetMask("Terrain_Upper");
        if (lowerLayerMask.value == 0)
            lowerLayerMask = LayerMask.GetMask("Terrain_Lower");

        upperFilter = new ContactFilter2D { useLayerMask = true, layerMask = upperLayerMask, useTriggers = false };
        lowerFilter = new ContactFilter2D { useLayerMask = true, layerMask = lowerLayerMask, useTriggers = false };
    }

    /// <summary>由 PlayerMovement 在 FixedUpdate 物理步进前调用。</summary>
    public void UpdatePathCollisions()
    {
        Vector2 feetPos = GetFeetPosition();
        bool crouching = playerAnim != null && playerAnim.IsCrouching;
        float threshold = playerMovement != null ? playerMovement.InputThreshold : 0.5f;
        Vector2 moveInput = playerMovement != null ? playerMovement.MoveInput : Vector2.zero;
        float moveX = Mathf.Abs(moveInput.x) > threshold ? Mathf.Sign(moveInput.x) : 0f;
        float vy = rb != null ? rb.linearVelocity.y : 0f;
        bool jumpUp = !(physicsCheck != null && physicsCheck.isOnSlope) && vy > 0.15f;
        bool onSlope = physicsCheck != null && physicsCheck.isOnSlope;
        bool onFlat = physicsCheck != null && physicsCheck.isGround
            && physicsCheck.groundNormal.y > 0.9f && !onSlope;

        var activeUppers = new HashSet<Collider2D>();
        var activeLowers = new HashSet<Collider2D>();
        var seenSegments = new HashSet<SlopePathSegment>();

        int upperCount = Physics2D.OverlapCircle(feetPos, nearbyScanRadius, upperFilter, overlapBuffer);
        for (int i = 0; i < upperCount; i++)
        {
            Collider2D col = overlapBuffer[i];
            if (col == null || col == capsuleCollider)
                continue;

            activeUppers.Add(col);
            trackedUppers.Add(col);

            var segment = col.GetComponent<SlopePathSegment>()
                ?? col.GetComponentInParent<SlopePathSegment>();
            if (segment == null)
            {
                // 无 Segment 的纯 Upper：站立碰撞，蹲下忽略
                SetIgnored(col, crouching);
                continue;
            }

            if (!seenSegments.Add(segment))
                continue;

            bool wantCollide = EvaluateWantCollideUpper(
                segment, feetPos, crouching, moveX, threshold, jumpUp, onSlope, onFlat);
            upperPathLatch[segment] = wantCollide;
            SetIgnored(segment.UpperCollider, !wantCollide);

            if (segment.LowerCollider != null)
            {
                activeLowers.Add(segment.LowerCollider);
                trackedLowers.Add(segment.LowerCollider);
                // 下层：始终可走（蹲站都碰）；若有桥底板语义可再扩展
                SetIgnored(segment.LowerCollider, false);
            }
        }

        int lowerCount = Physics2D.OverlapCircle(feetPos, nearbyScanRadius, lowerFilter, overlapBuffer);
        for (int i = 0; i < lowerCount; i++)
        {
            Collider2D col = overlapBuffer[i];
            if (col == null || col == capsuleCollider)
                continue;
            activeLowers.Add(col);
            trackedLowers.Add(col);
            SetIgnored(col, false);
        }

        RestoreLeft(trackedUppers, activeUppers);
        RestoreLeft(trackedLowers, activeLowers);
    }

    bool EvaluateWantCollideUpper(
        SlopePathSegment segment,
        Vector2 feetPos,
        bool crouching,
        float moveX,
        float threshold,
        bool jumpUp,
        bool onSlope,
        bool onFlat)
    {
        bool inBottom = segment.IsInBottomJunction(feetPos)
            || (segment.BottomTrigger != null && segment.BottomTrigger.PlayerInside);
        bool inTop = segment.IsInTopJunction(feetPos)
            || (segment.TopTrigger != null && segment.TopTrigger.PlayerInside);

        if (!inBottom)
            bottomLowerLocked.Remove(segment);
        if (!inTop)
            topUpperFlatLocked.Remove(segment);

        bool feetOnThis = segment.IsFeetAboveSurface(feetPos)
            && onSlope
            && Vector2.Dot(physicsCheck.groundNormal.normalized, segment.SurfaceNormal.normalized) > 0.75f;

        // 已在该坡上：闩锁保持碰撞（蹲不掉 / 站不挤飞）
        if (feetOnThis)
        {
            bottomLowerLocked.Remove(segment);
            topUpperFlatLocked.Remove(segment);
            return true;
        }

        Vector2 horizontal = new Vector2(moveX, 0f);
        float towardAscent = Vector2.Dot(horizontal, segment.AscentDirection);
        float towardDescent = Vector2.Dot(horizontal, -segment.AscentDirection);

        if (inBottom)
        {
            // 蹲进交界 → 锁下层；未离开前站起也不上坡
            if (crouching && !jumpUp)
            {
                bottomLowerLocked.Add(segment);
                return false;
            }

            if (bottomLowerLocked.Contains(segment) && onFlat)
                return false;

            if (towardAscent > threshold || jumpUp)
                return true;

            if (onFlat)
                return false;

            return upperPathLatch.TryGetValue(segment, out bool latched) && latched;
        }

        if (inTop)
        {
            // 站走进交界 → 锁上层平地；未离开前不能只靠蹲下切入（须蹲+朝下坡）
            if (!crouching || jumpUp)
            {
                if (towardDescent > threshold || jumpUp || onFlat)
                    topUpperFlatLocked.Add(segment);
            }

            if (crouching && towardDescent > threshold && !jumpUp)
            {
                topUpperFlatLocked.Remove(segment);
                return true;
            }

            if (topUpperFlatLocked.Contains(segment) && onFlat)
                return false;

            if (towardDescent > threshold || jumpUp)
                return false;

            if (onFlat)
                return false;

            return upperPathLatch.TryGetValue(segment, out bool latched) && latched;
        }

        // 非交界：脚在坡面下方时始终忽略，避免站起被薄面挤上
        if (!segment.IsFeetAboveSurface(feetPos))
            return false;

        if (onFlat)
            return !crouching;

        if (jumpUp)
            return false;
        return !crouching;
    }

    /// <summary>平地坡脚切向入口。</summary>
    public bool TryGetBottomSlopeEntry(out SlopePathSegment slope, out Vector2 ascentTangent)
    {
        slope = null;
        ascentTangent = Vector2.right;

        if (physicsCheck == null || playerMovement == null)
            return false;
        if (!physicsCheck.isGround || physicsCheck.groundNormal.y <= 0.9f || physicsCheck.isOnSlope)
            return false;

        float threshold = playerMovement.InputThreshold;
        Vector2 moveInput = playerMovement.MoveInput;
        float moveX = Mathf.Abs(moveInput.x) > threshold ? Mathf.Sign(moveInput.x) : 0f;
        if (Mathf.Approximately(moveX, 0f))
            return false;
        if (playerAnim != null && playerAnim.IsCrouching)
            return false;

        Vector2 feetPos = GetFeetPosition();
        if (!TryFindNearbySegment(feetPos, preferTop: false, out slope))
            return false;
        if (!slope.IsInBottomJunction(feetPos)
            && !(slope.BottomTrigger != null && slope.BottomTrigger.PlayerInside))
            return false;

        float towardAscent = Vector2.Dot(new Vector2(moveX, 0f), slope.AscentDirection);
        if (towardAscent <= threshold)
            return false;

        // 交界下层锁 / 闩锁不允许上层碰撞时不能抬升上坡
        if (bottomLowerLocked.Contains(slope))
            return false;
        if (upperPathLatch.TryGetValue(slope, out bool want) && !want)
            return false;

        ascentTangent = slope.GetSurfaceTangentAligned(moveX);
        return true;
    }

    /// <summary>平地坡顶蹲行切向入口。</summary>
    public bool TryGetTopSlopeEntry(out SlopePathSegment slope, out Vector2 descentTangent)
    {
        slope = null;
        descentTangent = Vector2.right;

        if (physicsCheck == null || playerMovement == null)
            return false;
        if (!physicsCheck.isGround || physicsCheck.groundNormal.y <= 0.9f || physicsCheck.isOnSlope)
            return false;
        if (playerAnim == null || !playerAnim.IsCrouching)
            return false;

        float threshold = playerMovement.InputThreshold;
        Vector2 moveInput = playerMovement.MoveInput;
        float moveX = Mathf.Abs(moveInput.x) > threshold ? Mathf.Sign(moveInput.x) : 0f;
        if (Mathf.Approximately(moveX, 0f))
            return false;

        Vector2 feetPos = GetFeetPosition();
        if (!TryFindNearbySegment(feetPos, preferTop: true, out slope))
            return false;
        if (!slope.IsInTopJunction(feetPos)
            && !(slope.TopTrigger != null && slope.TopTrigger.PlayerInside))
            return false;

        float towardDescent = Vector2.Dot(new Vector2(moveX, 0f), -slope.AscentDirection);
        if (towardDescent <= threshold)
            return false;

        if (upperPathLatch.TryGetValue(slope, out bool want) && !want)
            return false;

        descentTangent = slope.GetSurfaceTangentAligned(moveX);
        return true;
    }

    bool TryFindNearbySegment(Vector2 feetPos, bool preferTop, out SlopePathSegment slope)
    {
        slope = null;
        float best = float.MaxValue;
        foreach (Collider2D col in trackedUppers)
        {
            if (col == null)
                continue;
            var seg = col.GetComponent<SlopePathSegment>()
                ?? col.GetComponentInParent<SlopePathSegment>();
            if (seg == null)
                continue;
            Vector2 j = preferTop ? seg.TopJunctionWorld : seg.BottomJunctionWorld;
            float d = (feetPos - j).sqrMagnitude;
            if (d < best)
            {
                best = d;
                slope = seg;
            }
        }

        if (slope != null)
            return true;

        int count = Physics2D.OverlapCircle(feetPos, 0.8f, upperFilter, overlapBuffer);
        for (int i = 0; i < count; i++)
        {
            Collider2D col = overlapBuffer[i];
            if (col == null)
                continue;
            var seg = col.GetComponent<SlopePathSegment>()
                ?? col.GetComponentInParent<SlopePathSegment>();
            if (seg == null)
                continue;
            Vector2 j = preferTop ? seg.TopJunctionWorld : seg.BottomJunctionWorld;
            float d = (feetPos - j).sqrMagnitude;
            if (d < best)
            {
                best = d;
                slope = seg;
            }
        }

        return slope != null;
    }

    Vector2 GetFeetPosition() =>
        new Vector2(capsuleCollider.bounds.center.x, capsuleCollider.bounds.min.y);

    void SetIgnored(Collider2D other, bool ignore)
    {
        if (capsuleCollider == null || other == null)
            return;
        Physics2D.IgnoreCollision(capsuleCollider, other, ignore);
    }

    void RestoreLeft(HashSet<Collider2D> tracked, HashSet<Collider2D> active)
    {
        var remove = new List<Collider2D>();
        foreach (Collider2D col in tracked)
        {
            if (col == null || active.Contains(col))
            {
                if (col == null)
                    remove.Add(col);
                continue;
            }
            SetIgnored(col, false);
            remove.Add(col);
        }

        for (int i = 0; i < remove.Count; i++)
            tracked.Remove(remove[i]);
    }

    void OnDisable()
    {
        foreach (Collider2D col in trackedUppers)
        {
            if (col != null)
                SetIgnored(col, false);
        }
        foreach (Collider2D col in trackedLowers)
        {
            if (col != null)
                SetIgnored(col, false);
        }
        trackedUppers.Clear();
        trackedLowers.Clear();
        upperPathLatch.Clear();
        bottomLowerLocked.Clear();
        topUpperFlatLocked.Clear();
    }
}
