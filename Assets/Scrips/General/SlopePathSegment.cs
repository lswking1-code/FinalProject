using UnityEngine;

/// <summary>
/// 分层斜坡路段：同形双碰撞体（Terrain_Upper + Terrain_Lower）。
/// 站立踩 Upper、蹲下踩 Lower，几何一致故中途改姿势不会掉落。
/// 姿势层切换由 LayeredPathGate；交界入口闸门也由 Gate 处理。
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class SlopePathSegment : MonoBehaviour
{
    public enum JunctionKind
    {
        Bottom,
        Top
    }

    [Header("碰撞体")]
    [SerializeField] Collider2D upperCollider;
    [SerializeField] Collider2D lowerCollider;
    [Tooltip("若未指定 lowerCollider，运行时复制 Upper 为同形子物体并设为 Terrain_Lower")]
    [SerializeField] bool autoCreateLowerCollider = true;
    [Tooltip("将厚盒压成薄面，减轻端面卡墙")]
    [SerializeField] bool flattenToThinSurface = true;
    [SerializeField] float thinSurfaceHeight = 0.15f;

    [Header("坡向")]
    [SerializeField] bool manualAscent;
    [SerializeField] Vector2 manualAscentDirection = new Vector2(1f, 1f);

    [Header("交界")]
    [SerializeField] float junctionRadius = 0.55f;
    [SerializeField] float standMargin = 0.25f;
    [SerializeField] float junctionTriggerWorldSize = 1.2f;
    [SerializeField] bool autoCreateJunctionTriggers = true;

    Collider2D cachedCollider;
    SlopePathJunctionTrigger bottomTrigger;
    SlopePathJunctionTrigger topTrigger;

    public Collider2D UpperCollider => upperCollider != null ? upperCollider : cachedCollider;
    public Collider2D LowerCollider => lowerCollider;
    public float JunctionRadius => junctionRadius;
    public float StandMargin => standMargin;
    public SlopePathJunctionTrigger BottomTrigger => bottomTrigger;
    public SlopePathJunctionTrigger TopTrigger => topTrigger;

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

    void Awake()
    {
        cachedCollider = GetComponent<Collider2D>();
        if (upperCollider == null)
            upperCollider = cachedCollider;

        int upperLayer = LayerMask.NameToLayer("Terrain_Upper");
        int lowerLayer = LayerMask.NameToLayer("Terrain_Lower");

        if (flattenToThinSurface && upperCollider is BoxCollider2D upperBox
            && upperBox.size.y > thinSurfaceHeight + 0.01f)
        {
            float oldH = upperBox.size.y;
            upperBox.size = new Vector2(upperBox.size.x, thinSurfaceHeight);
            // 顶边对齐：原顶边 y = offset.y + oldH/2，新顶边相同
            upperBox.offset = new Vector2(
                upperBox.offset.x,
                upperBox.offset.y + (oldH - thinSurfaceHeight) * 0.5f);
        }

        if (upperLayer >= 0 && UpperCollider != null)
            UpperCollider.gameObject.layer = upperLayer;

        if (lowerCollider == null && autoCreateLowerCollider && UpperCollider != null)
            lowerCollider = CreateMirroredLowerCollider(lowerLayer);
        else if (lowerCollider != null && lowerLayer >= 0)
            lowerCollider.gameObject.layer = lowerLayer;

        // 斜坡本体不要再用 PlatformEffector 单向，改由层矩阵分流
        var effector = GetComponent<PlatformEffector2D>();
        if (effector != null)
            effector.enabled = false;
        if (UpperCollider != null)
            UpperCollider.usedByEffector = false;
        if (LowerCollider != null)
            LowerCollider.usedByEffector = false;

        CacheExistingTriggers();
        if (autoCreateJunctionTriggers)
            EnsureJunctionTriggers();
    }

    Collider2D CreateMirroredLowerCollider(int lowerLayer)
    {
        var go = new GameObject("LowerSurface");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;
        if (lowerLayer >= 0)
            go.layer = lowerLayer;

        if (UpperCollider is BoxCollider2D srcBox)
        {
            var dst = go.AddComponent<BoxCollider2D>();
            dst.offset = srcBox.offset;
            dst.size = srcBox.size;
            dst.edgeRadius = srcBox.edgeRadius;
            dst.isTrigger = false;
            dst.usedByEffector = false;
            dst.sharedMaterial = srcBox.sharedMaterial;
            return dst;
        }

        if (UpperCollider is EdgeCollider2D srcEdge)
        {
            var dst = go.AddComponent<EdgeCollider2D>();
            dst.points = srcEdge.points;
            dst.edgeRadius = srcEdge.edgeRadius;
            dst.isTrigger = false;
            dst.sharedMaterial = srcEdge.sharedMaterial;
            return dst;
        }

        // 回退：复制 bounds 为薄盒
        var box = go.AddComponent<BoxCollider2D>();
        box.size = new Vector2(1f, thinSurfaceHeight);
        box.offset = new Vector2(0f, thinSurfaceHeight * 0.5f);
        return box;
    }

    void LateUpdate()
    {
        if (!autoCreateJunctionTriggers)
            return;
        if (bottomTrigger != null)
            PlaceTrigger(bottomTrigger, JunctionKind.Bottom);
        if (topTrigger != null)
            PlaceTrigger(topTrigger, JunctionKind.Top);
    }

    public bool IsInBottomJunction(Vector2 feetPos) =>
        (feetPos - BottomJunctionWorld).sqrMagnitude <= junctionRadius * junctionRadius;

    public bool IsInTopJunction(Vector2 feetPos) =>
        (feetPos - TopJunctionWorld).sqrMagnitude <= junctionRadius * junctionRadius;

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

    void CacheExistingTriggers()
    {
        var triggers = GetComponentsInChildren<SlopePathJunctionTrigger>(true);
        for (int i = 0; i < triggers.Length; i++)
        {
            SlopePathJunctionTrigger t = triggers[i];
            if (t.Kind == JunctionKind.Bottom)
                bottomTrigger = t;
            else
                topTrigger = t;
            t.Initialize(this, t.Kind);
        }
    }

    void EnsureJunctionTriggers()
    {
        if (bottomTrigger == null)
            bottomTrigger = CreateTrigger(JunctionKind.Bottom, "BottomJunctionTrigger");
        if (topTrigger == null)
            topTrigger = CreateTrigger(JunctionKind.Top, "TopJunctionTrigger");
        PlaceTrigger(bottomTrigger, JunctionKind.Bottom);
        PlaceTrigger(topTrigger, JunctionKind.Top);
    }

    SlopePathJunctionTrigger CreateTrigger(JunctionKind kind, string objectName)
    {
        var go = new GameObject(objectName);
        go.transform.SetParent(transform, false);
        go.layer = gameObject.layer;
        var box = go.AddComponent<BoxCollider2D>();
        box.isTrigger = true;
        var trigger = go.AddComponent<SlopePathJunctionTrigger>();
        trigger.Initialize(this, kind);
        return trigger;
    }

    void PlaceTrigger(SlopePathJunctionTrigger trigger, JunctionKind kind)
    {
        if (trigger == null)
            return;

        GetSurfaceEndpointsLocal(out Vector2 localBottom, out Vector2 localTop);
        trigger.transform.localPosition = kind == JunctionKind.Bottom ? localBottom : localTop;
        trigger.transform.localRotation = Quaternion.identity;

        Vector3 lossy = transform.lossyScale;
        float worldSize = Mathf.Max(0.3f, junctionTriggerWorldSize);
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
        Collider2D col = UpperCollider;
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
        if (UpperCollider == null)
            cachedCollider = GetComponent<Collider2D>();

        GetSurfaceEndpoints(out Vector2 bottom, out Vector2 top);
        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(bottom, top);
        Gizmos.DrawWireSphere(bottom, junctionRadius);
        Gizmos.DrawWireSphere(top, junctionRadius);
        Gizmos.color = new Color(0.2f, 0.9f, 0.4f, 0.9f);
        Gizmos.DrawWireCube(bottom, Vector3.one * junctionTriggerWorldSize);
        Gizmos.color = new Color(1f, 0.55f, 0.1f, 0.9f);
        Gizmos.DrawWireCube(top, Vector3.one * junctionTriggerWorldSize);
    }
}
