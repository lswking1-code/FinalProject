using UnityEngine;

/// <summary>
/// 斜坡单向平台：挂载于旋转的单向平台物体，提供坡面几何数据与交界点判定。
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
    [SerializeField] float junctionRadius = 0.4f;
    [SerializeField] float surfaceMargin = 0.05f;
    [Tooltip("脚底允许低于坡面的容差，用于胶囊体嵌入与落地判定")]
    [SerializeField] float standMargin = 0.45f;

    BoxCollider2D boxCollider;

    public float SurfaceMargin => surfaceMargin;
    public float StandMargin => standMargin;
    public float JunctionRadius => junctionRadius;
    public Collider2D Collider => boxCollider;

    /// <summary>可行走面朝上的法线（世界空间）。</summary>
    public Vector2 SurfaceNormal => transform.up;

    void Awake()
    {
        boxCollider = GetComponent<BoxCollider2D>();
        var effector = GetComponent<PlatformEffector2D>();
        if (effector != null)
            effector.useSideFriction = true;
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
    /// 脚底相对坡面的有符号距离；正值表示在可行走面上方。
    /// </summary>
    public float GetSignedDistanceToSurface(Vector2 feetPos)
    {
        Vector2 closest = boxCollider.ClosestPoint(feetPos);
        return Vector2.Dot(feetPos - closest, SurfaceNormal);
    }

    /// <summary>脚底是否站在坡面可行走侧（含嵌入容差）。</summary>
    public bool IsFeetAboveSurface(Vector2 feetPos) =>
        GetSignedDistanceToSurface(feetPos) >= -standMargin;

    void GetSurfaceEndpoints(out Vector2 bottomEnd, out Vector2 topEnd)
    {
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
            bottomEnd = worldLeft;
            topEnd = worldRight;
        }
        else
        {
            bottomEnd = worldRight;
            topEnd = worldLeft;
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

        Vector2 mid = (bottom + top) * 0.5f;
        Vector2 normal = SurfaceNormal * 0.5f;
        Gizmos.color = Color.green;
        Gizmos.DrawLine(mid, mid + normal);

        Gizmos.color = Color.yellow;
        Vector2 ascent = AscentDirection * 0.6f;
        Gizmos.DrawLine(mid, mid + ascent);
    }
}
