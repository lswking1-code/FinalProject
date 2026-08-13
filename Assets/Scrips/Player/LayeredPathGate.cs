using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 合金弹头式分层路径：不改玩家 Layer，按姿态切换与 Terrain_Upper / Terrain_Lower 的层碰撞。
/// <list type="bullet">
/// <item>站立 / 空中 → 只与 Terrain_Upper 碰撞</item>
/// <item>蹲下 / 下穿 → 只与 Terrain_Lower 碰撞</item>
/// </list>
/// 斜坡使用同形双碰撞体（Upper+Lower）：中途改姿势会换层但仍踩在同一几何上，不会掉落。
/// 交界闸门用 IgnoreCollision 补层规则无法表达的入口：
/// 坡脚蹲着不爬 Upper/Lower 坡面；坡顶站着不踏入 Upper 坡面。
/// </summary>
[RequireComponent(typeof(CapsuleCollider2D))]
[RequireComponent(typeof(PhysicsCheck))]
public class LayeredPathGate : MonoBehaviour
{
    [Header("层名")]
    [SerializeField] string upperLayerName = "Terrain_Upper";
    [SerializeField] string lowerLayerName = "Terrain_Lower";

    [Header("交界闸门")]
    [Tooltip("扫描附近 SlopePathSegment 的半径")]
    [SerializeField] float slopeSearchRadius = 1.2f;

    PhysicsCheck physicsCheck;
    PlayerMovement playerMovement;
    PlayerAnimBase playerAnim;
    PlatformDropThrough platformDropThrough;
    CapsuleCollider2D capsuleCollider;

    int playerLayer;
    int upperLayer = -1;
    int lowerLayer = -1;
    bool preferLowerPath;
    bool layersReady;

    readonly Collider2D[] searchBuffer = new Collider2D[16];
    readonly HashSet<Collider2D> gatedIgnored = new HashSet<Collider2D>();
    ContactFilter2D searchFilter;

    public bool PreferLowerPath => preferLowerPath;
    public bool PreferUpperPath => !preferLowerPath;

    void Awake()
    {
        physicsCheck = GetComponent<PhysicsCheck>();
        playerMovement = GetComponent<PlayerMovement>();
        playerAnim = PlayerAnimBase.Resolve(gameObject);
        platformDropThrough = GetComponent<PlatformDropThrough>();
        capsuleCollider = GetComponent<CapsuleCollider2D>();
        playerLayer = gameObject.layer;

        upperLayer = LayerMask.NameToLayer(upperLayerName);
        lowerLayer = LayerMask.NameToLayer(lowerLayerName);
        layersReady = upperLayer >= 0 && lowerLayer >= 0;

        searchFilter = new ContactFilter2D
        {
            useTriggers = true,
            useLayerMask = false
        };

        if (!layersReady)
            Debug.LogWarning("LayeredPathGate: 缺少 Terrain_Upper / Terrain_Lower 层，分层路径未启用。", this);
    }

    void OnEnable()
    {
        ApplyLayerMatrix(preferLower: false);
    }

    void OnDisable()
    {
        ClearAllGates();
        // 恢复层矩阵，避免退出 Play 后编辑器里层碰撞被改坏
        if (layersReady)
        {
            Physics2D.IgnoreLayerCollision(playerLayer, upperLayer, false);
            Physics2D.IgnoreLayerCollision(playerLayer, lowerLayer, false);
        }
    }

    void FixedUpdate()
    {
        if (!layersReady)
            return;

        bool wantLower = EvaluatePreferLower();
        if (wantLower != preferLowerPath)
        {
            preferLowerPath = wantLower;
            ApplyLayerMatrix(preferLowerPath);
        }

        UpdateJunctionGates();
    }

    bool EvaluatePreferLower()
    {
        if (platformDropThrough != null && platformDropThrough.IsDroppingThrough)
            return true;

        if (playerAnim != null && playerAnim.IsCrouching)
            return true;

        return false;
    }

    void ApplyLayerMatrix(bool preferLower)
    {
        // 站立/空中：只碰 Upper；蹲下/下穿：只碰 Lower
        Physics2D.IgnoreLayerCollision(playerLayer, upperLayer, preferLower);
        Physics2D.IgnoreLayerCollision(playerLayer, lowerLayer, !preferLower);
    }

    void UpdateJunctionGates()
    {
        var keepIgnored = new HashSet<Collider2D>();
        Vector2 feet = GetFeetPosition();
        bool onFlat = physicsCheck != null && physicsCheck.isGround
            && physicsCheck.groundNormal.y > 0.9f && !physicsCheck.isOnSlope;
        bool crouching = playerAnim != null && playerAnim.IsCrouching;

        int count = Physics2D.OverlapCircle(feet, slopeSearchRadius, searchFilter, searchBuffer);
        for (int i = 0; i < count; i++)
        {
            Collider2D hit = searchBuffer[i];
            if (hit == null)
                continue;

            var segment = hit.GetComponentInParent<SlopePathSegment>();
            if (segment == null)
                continue;

            // 已在该坡面上：清闸门，双层同形保证蹲/站切换不掉落
            if (physicsCheck != null && physicsCheck.isOnSlope
                && segment.IsFeetAboveSurface(feet))
            {
                continue;
            }

            if (!onFlat)
                continue;

            bool inBottom = segment.IsInBottomJunction(feet)
                || (segment.BottomTrigger != null && segment.BottomTrigger.PlayerInside);
            bool inTop = segment.IsInTopJunction(feet)
                || (segment.TopTrigger != null && segment.TopTrigger.PlayerInside);

            if (inBottom && crouching)
            {
                // 坡脚蹲走：下层路径，强制忽略坡面双碰撞体，避免误爬坡
                GateIgnore(segment.UpperCollider, keepIgnored);
                GateIgnore(segment.LowerCollider, keepIgnored);
            }

            if (inTop && !crouching)
            {
                // 坡顶站走：上层路径，忽略 Upper 坡面，避免站着走下坡
                GateIgnore(segment.UpperCollider, keepIgnored);
            }
        }

        // 恢复本帧不再需要忽略的碰撞体
        var toClear = new List<Collider2D>();
        foreach (Collider2D col in gatedIgnored)
        {
            if (col == null || keepIgnored.Contains(col))
                continue;
            toClear.Add(col);
        }

        for (int i = 0; i < toClear.Count; i++)
        {
            SetIgnored(toClear[i], false);
            gatedIgnored.Remove(toClear[i]);
        }
    }

    void GateIgnore(Collider2D col, HashSet<Collider2D> keep)
    {
        if (col == null)
            return;
        keep.Add(col);
        if (gatedIgnored.Add(col))
            SetIgnored(col, true);
        else
            SetIgnored(col, true);
    }

    void SetIgnored(Collider2D col, bool ignore)
    {
        if (capsuleCollider == null || col == null)
            return;
        Physics2D.IgnoreCollision(capsuleCollider, col, ignore);
    }

    void ClearAllGates()
    {
        foreach (Collider2D col in gatedIgnored)
        {
            if (col != null)
                SetIgnored(col, false);
        }
        gatedIgnored.Clear();
    }

    /// <summary>
    /// 坡脚入口：平地站立、朝上坡，供移动沿切向抬升。
    /// </summary>
    public bool TryGetBottomSlopeEntry(out SlopePathSegment segment, out Vector2 ascentTangent)
    {
        segment = null;
        ascentTangent = Vector2.right;

        if (physicsCheck == null || playerMovement == null)
            return false;
        if (!physicsCheck.isGround || physicsCheck.isOnSlope)
            return false;
        if (physicsCheck.groundNormal.y <= 0.9f)
            return false;
        if (playerAnim != null && playerAnim.IsCrouching)
            return false;

        float threshold = playerMovement.InputThreshold;
        Vector2 moveInput = playerMovement.MoveInput;
        float moveX = Mathf.Abs(moveInput.x) > threshold ? Mathf.Sign(moveInput.x) : 0f;
        if (Mathf.Approximately(moveX, 0f))
            return false;

        Vector2 feet = GetFeetPosition();
        if (!TryFindSegment(feet, preferTop: false, out segment))
            return false;
        if (!segment.IsInBottomJunction(feet)
            && (segment.BottomTrigger == null || !segment.BottomTrigger.PlayerInside))
            return false;

        float toward = Vector2.Dot(new Vector2(moveX, 0f), segment.AscentDirection);
        if (toward <= threshold)
            return false;

        ascentTangent = segment.GetSurfaceTangentAligned(moveX);
        return true;
    }

    /// <summary>
    /// 坡顶入口：平地蹲下、朝下坡。
    /// </summary>
    public bool TryGetTopSlopeEntry(out SlopePathSegment segment, out Vector2 descentTangent)
    {
        segment = null;
        descentTangent = Vector2.right;

        if (physicsCheck == null || playerMovement == null)
            return false;
        if (!physicsCheck.isGround || physicsCheck.isOnSlope)
            return false;
        if (physicsCheck.groundNormal.y <= 0.9f)
            return false;
        if (playerAnim == null || !playerAnim.IsCrouching)
            return false;
        if (platformDropThrough != null && platformDropThrough.IsDroppingThrough)
            return false;

        float threshold = playerMovement.InputThreshold;
        Vector2 moveInput = playerMovement.MoveInput;
        float moveX = Mathf.Abs(moveInput.x) > threshold ? Mathf.Sign(moveInput.x) : 0f;
        if (Mathf.Approximately(moveX, 0f))
            return false;

        Vector2 feet = GetFeetPosition();
        if (!TryFindSegment(feet, preferTop: true, out segment))
            return false;
        if (!segment.IsInTopJunction(feet)
            && (segment.TopTrigger == null || !segment.TopTrigger.PlayerInside))
            return false;

        float toward = Vector2.Dot(new Vector2(moveX, 0f), -segment.AscentDirection);
        if (toward <= threshold)
            return false;

        descentTangent = segment.GetSurfaceTangentAligned(moveX);
        return true;
    }

    bool TryFindSegment(Vector2 feet, bool preferTop, out SlopePathSegment segment)
    {
        segment = null;
        float best = float.MaxValue;
        int count = Physics2D.OverlapCircle(feet, slopeSearchRadius, searchFilter, searchBuffer);
        for (int i = 0; i < count; i++)
        {
            Collider2D hit = searchBuffer[i];
            if (hit == null)
                continue;
            var candidate = hit.GetComponentInParent<SlopePathSegment>();
            if (candidate == null)
                continue;

            Vector2 junction = preferTop ? candidate.TopJunctionWorld : candidate.BottomJunctionWorld;
            float d = (feet - junction).sqrMagnitude;
            if (d < best)
            {
                best = d;
                segment = candidate;
            }
        }

        return segment != null;
    }

    Vector2 GetFeetPosition()
    {
        Bounds b = capsuleCollider.bounds;
        return new Vector2(b.center.x, b.min.y);
    }
}
