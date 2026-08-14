using UnityEngine;

/// <summary>
/// 斜坡：提供坡面几何，供 PhysicsCheck / PlayerMovement 贴合行走。
/// 可选单向：开启后由 PlatformEffector2D + PlatformDropThrough 处理从下穿过/下穿。
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

    [Header("单向平台")]
    [Tooltip("关闭：固体斜坡，仅可在上面行走。开启：可从下穿过，并支持下+跳下穿")]
    [SerializeField] bool oneWay = false;
    [Tooltip("PlatformEffector2D 表面弧角（度）；180 为常见单向顶部")]
    [SerializeField, Range(1f, 360f)] float surfaceArc = 180f;

    [Header("坡向")]
    [SerializeField] SlopeDirectionMode directionMode = SlopeDirectionMode.AutoFromTransform;
    [SerializeField] Vector2 manualAscentDirection = new Vector2(1f, 1f);

    [Header("判定")]
    [SerializeField] float surfaceMargin = 0.05f;
    [Tooltip("脚底允许低于坡面的容差，用于胶囊体嵌入与落地判定")]
    [SerializeField] float standMargin = 0.45f;

    BoxCollider2D boxCollider;
    PlatformEffector2D effector;

    public bool OneWay => oneWay;
    public float SurfaceMargin => surfaceMargin;
    public float StandMargin => standMargin;
    public Collider2D Collider => boxCollider;

    /// <summary>可行走面朝上的法线（世界空间）。</summary>
    public Vector2 SurfaceNormal => transform.up;

    void Awake()
    {
        boxCollider = GetComponent<BoxCollider2D>();
        ApplyOneWayMode();
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        if (boxCollider == null)
            boxCollider = GetComponent<BoxCollider2D>();
        ApplyOneWayMode();
    }
#endif

    void ApplyOneWayMode()
    {
        if (effector == null)
            effector = GetComponent<PlatformEffector2D>();
        if (boxCollider == null)
            boxCollider = GetComponent<BoxCollider2D>();

        if (effector == null)
            return;

        if (oneWay)
        {
            effector.enabled = true;
            effector.useOneWay = true;
            effector.surfaceArc = surfaceArc;
            effector.useSideFriction = true;
            effector.useOneWayGrouping = false;
            if (boxCollider != null)
                boxCollider.usedByEffector = true;
        }
        else
        {
            effector.useOneWay = false;
            effector.enabled = false;
            if (boxCollider != null)
                boxCollider.usedByEffector = false;
        }
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

    /// <summary>
    /// 脚底相对坡面（顶边可行走面）的有符号距离；正值表示在可行走面上方。
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

        Vector2 mid = (bottom + top) * 0.5f;
        Vector2 normal = SurfaceNormal * 0.5f;
        Gizmos.color = Color.green;
        Gizmos.DrawLine(mid, mid + normal);

        Gizmos.color = oneWay ? Color.magenta : Color.yellow;
        Vector2 ascent = AscentDirection * 0.6f;
        Gizmos.DrawLine(mid, mid + ascent);
    }
}
