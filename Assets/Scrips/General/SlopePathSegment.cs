using UnityEngine;

/// <summary>
/// 斜坡路段：单碰撞体坡面几何，供 PhysicsCheck / PlayerMovement 贴合行走。
/// 可选单向：开启后由 PlatformEffector2D + PlatformDropThrough 处理从下穿过/下穿。
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class SlopePathSegment : MonoBehaviour
{
    [Header("单向平台")]
    [Tooltip("关闭：固体斜坡，仅可在上面行走。开启：可从下穿过，并支持下+跳下穿")]
    [SerializeField] bool oneWay = false;
    [Tooltip("PlatformEffector2D 表面弧角（度）；180 为常见单向顶部")]
    [SerializeField, Range(1f, 360f)] float surfaceArc = 180f;

    [Header("碰撞体")]
    [SerializeField] Collider2D surfaceCollider;
    [Tooltip("将厚盒压成薄面，减轻端面卡墙")]
    [SerializeField] bool flattenToThinSurface = true;
    [SerializeField] float thinSurfaceHeight = 0.15f;

    [Header("坡向")]
    [SerializeField] bool manualAscent;
    [SerializeField] Vector2 manualAscentDirection = new Vector2(1f, 1f);

    [Header("判定")]
    [SerializeField] float standMargin = 0.25f;

    Collider2D cachedCollider;
    PlatformEffector2D effector;

    public bool OneWay => oneWay;
    public Collider2D SurfaceCollider => surfaceCollider != null ? surfaceCollider : cachedCollider;
    /// <summary>兼容旧调用方：等同 SurfaceCollider。</summary>
    public Collider2D UpperCollider => SurfaceCollider;
    public float StandMargin => standMargin;

    public Vector2 SurfaceNormal => transform.up;

    public Vector2 AscentDirection
    {
        get
        {
            GetSurfaceEndpoints(out Vector2 bottom, out Vector2 top);
            if (manualAscent && manualAscentDirection.sqrMagnitude > 0.0001f)
                return manualAscentDirection.normalized;
            Vector2 dir = top - bottom;
            return dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector2.right;
        }
    }

    void Awake()
    {
        cachedCollider = GetComponent<Collider2D>();
        if (surfaceCollider == null)
            surfaceCollider = cachedCollider;

        if (flattenToThinSurface && SurfaceCollider is BoxCollider2D box
            && box.size.y > thinSurfaceHeight + 0.01f)
        {
            float oldH = box.size.y;
            box.size = new Vector2(box.size.x, thinSurfaceHeight);
            box.offset = new Vector2(
                box.offset.x,
                box.offset.y + (oldH - thinSurfaceHeight) * 0.5f);
        }

        ApplyOneWayMode();
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        if (cachedCollider == null)
            cachedCollider = GetComponent<Collider2D>();
        if (surfaceCollider == null)
            surfaceCollider = cachedCollider;
        ApplyOneWayMode();
    }
#endif

    void ApplyOneWayMode()
    {
        Collider2D col = SurfaceCollider;
        if (col == null)
            return;

        effector = GetComponent<PlatformEffector2D>();
        if (oneWay)
        {
            if (effector == null)
            {
                if (!Application.isPlaying)
                {
                    col.usedByEffector = false;
                    return;
                }
                effector = gameObject.AddComponent<PlatformEffector2D>();
            }

            effector.enabled = true;
            effector.useOneWay = true;
            effector.surfaceArc = surfaceArc;
            effector.useSideFriction = true;
            effector.useOneWayGrouping = false;
            col.usedByEffector = true;
        }
        else
        {
            if (effector != null)
            {
                effector.useOneWay = false;
                effector.enabled = false;
            }
            col.usedByEffector = false;
        }
    }

    public float GetSignedDistanceToSurface(Vector2 feetPos)
    {
        GetSurfaceEndpoints(out Vector2 endA, out Vector2 endB);
        Vector2 onSurface = (endA + endB) * 0.5f;
        return Vector2.Dot(feetPos - onSurface, SurfaceNormal);
    }

    public bool IsFeetAboveSurface(Vector2 feetPos) =>
        GetSignedDistanceToSurface(feetPos) >= -standMargin;

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

    void GetSurfaceEndpoints(out Vector2 bottomEnd, out Vector2 topEnd)
    {
        GetSurfaceEndpointsLocal(out Vector2 localBottom, out Vector2 localTop);
        bottomEnd = transform.TransformPoint(localBottom);
        topEnd = transform.TransformPoint(localTop);
    }

    void GetSurfaceEndpointsLocal(out Vector2 localBottom, out Vector2 localTop)
    {
        Collider2D col = SurfaceCollider;
        if (col is BoxCollider2D box)
        {
            Vector2 offset = box.offset;
            float halfW = box.size.x * 0.5f;
            float halfH = box.size.y * 0.5f;
            Vector2 localLeft = offset + new Vector2(-halfW, halfH);
            Vector2 localRight = offset + new Vector2(halfW, halfH);
            AssignBottomTop(localLeft, localRight, out localBottom, out localTop);
            return;
        }

        if (col is EdgeCollider2D edge && edge.pointCount >= 2)
        {
            Vector2 a = edge.points[0];
            Vector2 b = edge.points[edge.pointCount - 1];
            AssignBottomTop(a, b, out localBottom, out localTop);
            return;
        }

        localBottom = new Vector2(-0.5f, 0f);
        localTop = new Vector2(0.5f, 0f);
    }

    void AssignBottomTop(Vector2 localA, Vector2 localB, out Vector2 localBottom, out Vector2 localTop)
    {
        Vector2 worldA = transform.TransformPoint(localA);
        Vector2 worldB = transform.TransformPoint(localB);
        Vector2 ascent = manualAscent && manualAscentDirection.sqrMagnitude > 0.0001f
            ? manualAscentDirection.normalized
            : (worldB - worldA).normalized;

        if (Vector2.Dot(worldA, ascent) <= Vector2.Dot(worldB, ascent))
        {
            localBottom = localA;
            localTop = localB;
        }
        else
        {
            localBottom = localB;
            localTop = localA;
        }
    }

    void OnDrawGizmosSelected()
    {
        if (SurfaceCollider == null)
            cachedCollider = GetComponent<Collider2D>();

        GetSurfaceEndpoints(out Vector2 bottom, out Vector2 top);
        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(bottom, top);

        Vector2 mid = (bottom + top) * 0.5f;
        Gizmos.color = Color.green;
        Gizmos.DrawLine(mid, mid + SurfaceNormal * 0.5f);
        Gizmos.color = oneWay ? Color.magenta : Color.yellow;
        Gizmos.DrawLine(mid, mid + AscentDirection * 0.6f);
    }
}
