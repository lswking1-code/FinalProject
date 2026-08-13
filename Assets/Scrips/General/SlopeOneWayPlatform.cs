using UnityEngine;

/// <summary>
/// 斜坡单向平台：挂载于旋转的单向平台物体，提供坡面几何数据与交界 Trigger。
/// 玩家侧由 PlatformDropThrough / PlayerMovement 读取。
/// </summary>
[RequireComponent(typeof(BoxCollider2D))]
[RequireComponent(typeof(PlatformEffector2D))]
public class SlopeOneWayPlatform : MonoBehaviour
{
    public enum SlopeDirectionMode
    {
        AutoFromTransform,
        ManualVector
    }

    [Header("坡向")]
    [SerializeField] SlopeDirectionMode directionMode = SlopeDirectionMode.AutoFromTransform;
    [SerializeField] Vector2 manualAscentDirection = new Vector2(1f, 1f);

    [Header("判定")]
    [SerializeField] float junctionRadius = 0.55f;
    [SerializeField] float surfaceMargin = 0.05f;
    [Tooltip("脚底允许低于坡面的容差，用于胶囊体嵌入与落地判定")]
    [SerializeField] float standMargin = 0.45f;

    [Header("交界 Trigger")]
    [Tooltip("运行时自动在坡脚/坡顶生成 Trigger（合金弹头式蹲站路径闩锁）")]
    [SerializeField] bool autoCreateJunctionTriggers = false;
    [Tooltip("Trigger 世界空间边长（相对 junctionRadius 的倍率）")]
    [SerializeField] float junctionTriggerSizeScale = 2.2f;

    BoxCollider2D boxCollider;
    SlopeJunctionTrigger bottomTrigger;
    SlopeJunctionTrigger topTrigger;

    public float SurfaceMargin => surfaceMargin;
    public float StandMargin => standMargin;
    public float JunctionRadius => junctionRadius;
    public Collider2D Collider => boxCollider;
    public SlopeJunctionTrigger BottomTrigger => bottomTrigger;
    public SlopeJunctionTrigger TopTrigger => topTrigger;

    /// <summary>可行走面朝上的法线（世界空间）。</summary>
    public Vector2 SurfaceNormal => transform.up;

    void Awake()
    {
        boxCollider = GetComponent<BoxCollider2D>();
        var effector = GetComponent<PlatformEffector2D>();
        if (effector != null)
        {
            // SlopeOneWayPlatform 依赖单向碰撞 + IgnoreCollision 交界规则
            effector.useOneWay = true;
            effector.useSideFriction = true;
        }

        // 确保碰撞体受 PlatformEffector2D 控制（Ground 预制体默认 UsedByEffector=0）
        if (boxCollider != null)
            boxCollider.usedByEffector = true;

        CacheExistingTriggers();
        if (autoCreateJunctionTriggers)
            EnsureJunctionTriggers();
    }

    void LateUpdate()
    {
        if (!autoCreateJunctionTriggers)
            return;

        // 父级缩放/旋转变化时保持 Trigger 贴合端点
        if (bottomTrigger != null)
            PlaceTrigger(bottomTrigger, SlopeJunctionTrigger.JunctionKind.Bottom);
        if (topTrigger != null)
            PlaceTrigger(topTrigger, SlopeJunctionTrigger.JunctionKind.Top);
    }

    /// <summary>上坡方向（世界空间单位向量，从低端指向高端）。</summary>
    public Vector2 AscentDirection
    {
        get
        {
            GetSurfaceEndpoints(out Vector2 bottom, out Vector2 top);
            Vector2 dir = top - bottom;
            return dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector2.right;
        }
    }

    public Vector2 BottomJunctionWorld
    {
        get
        {
            GetSurfaceEndpoints(out Vector2 bottom, out _);
            return bottom;
        }
    }

    public Vector2 TopJunctionWorld
    {
        get
        {
            GetSurfaceEndpoints(out _, out Vector2 top);
            return top;
        }
    }

    public bool IsInBottomJunction(Vector2 feetPos) =>
        (feetPos - BottomJunctionWorld).sqrMagnitude <= junctionRadius * junctionRadius;

    public bool IsInTopJunction(Vector2 feetPos) =>
        (feetPos - TopJunctionWorld).sqrMagnitude <= junctionRadius * junctionRadius;

    /// <summary>
    /// 交界 Trigger 是否对玩家强制覆盖碰撞（在 Trigger 内时）。
    /// shouldCollide=true 表示走坡路径；false 表示走平地路径（忽略斜坡碰撞）。
    /// </summary>
    public bool TryGetJunctionCollisionOverride(Collider2D playerCol, out bool shouldCollide)
    {
        shouldCollide = false;

        if (bottomTrigger != null
            && bottomTrigger.TryGetCollisionOverride(playerCol, out shouldCollide))
            return true;

        if (topTrigger != null
            && topTrigger.TryGetCollisionOverride(playerCol, out shouldCollide))
            return true;

        return false;
    }

    /// <summary>
    /// 脚底相对坡面（顶边可行走面）的有符号距离；正值表示在可行走面上方。
    /// 使用顶边平面而非 ClosestPoint：脚在碰撞体内部/下方时 ClosestPoint 会归零或贴底面，
    /// 导致 standMargin 把「已钻穿平台」误判为「仍可站立」。
    /// </summary>
    public float GetSignedDistanceToSurface(Vector2 feetPos)
    {
        GetSurfaceEndpoints(out Vector2 endA, out Vector2 endB);
        Vector2 onSurface = (endA + endB) * 0.5f;
        return Vector2.Dot(feetPos - onSurface, SurfaceNormal);
    }

    /// <summary>脚底是否站在坡面可行走侧（含嵌入容差）。</summary>
    public bool IsFeetAboveSurface(Vector2 feetPos) =>
        GetSignedDistanceToSurface(feetPos) >= -standMargin;

    /// <summary>
    /// 与 moveX 同向的坡面切向（世界空间单位向量），无水平输入时按上坡方向对齐。
    /// </summary>
    public Vector2 GetSurfaceTangentAligned(float moveXSign)
    {
        Vector2 normal = SurfaceNormal;
        Vector2 tangent = new Vector2(-normal.y, normal.x).normalized;
        if (Mathf.Approximately(moveXSign, 0f))
        {
            if (Vector2.Dot(tangent, AscentDirection) < 0f)
                tangent = -tangent;
            return tangent;
        }

        if (Mathf.Sign(tangent.x) != Mathf.Sign(moveXSign))
            tangent = -tangent;
        return tangent;
    }

    void CacheExistingTriggers()
    {
        var triggers = GetComponentsInChildren<SlopeJunctionTrigger>(true);
        for (int i = 0; i < triggers.Length; i++)
        {
            SlopeJunctionTrigger t = triggers[i];
            if (t.Kind == SlopeJunctionTrigger.JunctionKind.Bottom)
                bottomTrigger = t;
            else
                topTrigger = t;
            t.Initialize(this, t.Kind);
        }
    }

    void EnsureJunctionTriggers()
    {
        if (bottomTrigger == null)
            bottomTrigger = CreateTrigger(SlopeJunctionTrigger.JunctionKind.Bottom, "BottomJunctionTrigger");
        if (topTrigger == null)
            topTrigger = CreateTrigger(SlopeJunctionTrigger.JunctionKind.Top, "TopJunctionTrigger");

        PlaceTrigger(bottomTrigger, SlopeJunctionTrigger.JunctionKind.Bottom);
        PlaceTrigger(topTrigger, SlopeJunctionTrigger.JunctionKind.Top);
    }

    SlopeJunctionTrigger CreateTrigger(SlopeJunctionTrigger.JunctionKind kind, string objectName)
    {
        var go = new GameObject(objectName);
        go.transform.SetParent(transform, false);
        go.layer = gameObject.layer;

        var box = go.AddComponent<BoxCollider2D>();
        box.isTrigger = true;
        box.usedByEffector = false;

        var trigger = go.AddComponent<SlopeJunctionTrigger>();
        trigger.Initialize(this, kind);
        return trigger;
    }

    void PlaceTrigger(SlopeJunctionTrigger trigger, SlopeJunctionTrigger.JunctionKind kind)
    {
        if (trigger == null || boxCollider == null)
            return;

        GetSurfaceEndpointsLocal(out Vector2 localBottom, out Vector2 localTop);
        Vector2 localPos = kind == SlopeJunctionTrigger.JunctionKind.Bottom ? localBottom : localTop;
        trigger.transform.localPosition = localPos;
        trigger.transform.localRotation = Quaternion.identity;

        // 抵消父级缩放，使 Trigger 在世界空间接近固定尺寸
        Vector3 lossy = transform.lossyScale;
        float worldSize = Mathf.Max(0.2f, junctionRadius * junctionTriggerSizeScale);
        float sx = Mathf.Abs(lossy.x) > 0.0001f ? worldSize / Mathf.Abs(lossy.x) : worldSize;
        float sy = Mathf.Abs(lossy.y) > 0.0001f ? worldSize / Mathf.Abs(lossy.y) : worldSize;
        trigger.transform.localScale = new Vector3(sx, sy, 1f);

        var box = trigger.GetComponent<BoxCollider2D>();
        if (box != null)
        {
            box.isTrigger = true;
            box.size = Vector2.one;
            box.offset = Vector2.zero;
        }
    }

    void GetSurfaceEndpoints(out Vector2 bottomEnd, out Vector2 topEnd)
    {
        GetSurfaceEndpointsLocal(out Vector2 localBottom, out Vector2 localTop);
        bottomEnd = transform.TransformPoint(localBottom);
        topEnd = transform.TransformPoint(localTop);
    }

    void GetSurfaceEndpointsLocal(out Vector2 localBottom, out Vector2 localTop)
    {
        if (boxCollider == null)
            boxCollider = GetComponent<BoxCollider2D>();

        Vector2 offset = boxCollider.offset;
        float halfW = boxCollider.size.x * 0.5f;
        float halfH = boxCollider.size.y * 0.5f;

        Vector2 localLeft = offset + new Vector2(-halfW, halfH);
        Vector2 localRight = offset + new Vector2(halfW, halfH);
        Vector2 worldLeft = transform.TransformPoint(localLeft);
        Vector2 worldRight = transform.TransformPoint(localRight);

        Vector2 ascentDir;
        if (directionMode == SlopeDirectionMode.ManualVector && manualAscentDirection.sqrMagnitude > 0.0001f)
            ascentDir = manualAscentDirection.normalized;
        else
            ascentDir = (worldRight - worldLeft).normalized;

        float dotLeft = Vector2.Dot(worldLeft, ascentDir);
        float dotRight = Vector2.Dot(worldRight, ascentDir);
        if (dotLeft <= dotRight)
        {
            localBottom = localLeft;
            localTop = localRight;
        }
        else
        {
            localBottom = localRight;
            localTop = localLeft;
        }
    }

    void OnDrawGizmosSelected()
    {
        if (boxCollider == null)
            boxCollider = GetComponent<BoxCollider2D>();
        if (boxCollider == null)
            return;

        GetSurfaceEndpoints(out Vector2 bottom, out Vector2 top);

        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(bottom, top);
        Gizmos.DrawWireSphere(bottom, junctionRadius);
        Gizmos.DrawWireSphere(top, junctionRadius);

        float triggerSize = junctionRadius * junctionTriggerSizeScale;
        Gizmos.color = new Color(1f, 0.6f, 0.1f, 0.85f);
        Gizmos.DrawWireCube(bottom, Vector3.one * triggerSize);
        Gizmos.DrawWireCube(top, Vector3.one * triggerSize);

        Vector2 mid = (bottom + top) * 0.5f;
        Vector2 normal = SurfaceNormal * 0.5f;
        Gizmos.color = Color.green;
        Gizmos.DrawLine(mid, mid + normal);

        Gizmos.color = Color.yellow;
        Vector2 ascent = AscentDirection * 0.6f;
        Gizmos.DrawLine(mid, mid + ascent);
    }
}
