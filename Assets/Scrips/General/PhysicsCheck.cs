using UnityEngine;

/// <summary>
/// 物理环境检测组件，用于检测地面与墙体碰撞状态。
/// 玩家与敌人均可挂载；敌人仅需地面/墙体检测，玩家可额外启用贴墙判定。
/// </summary>
public class PhysicsCheck : MonoBehaviour
{
    private CapsuleCollider2D coll;
    private Rigidbody2D rb;
    PlatformDropThrough platformDropThrough;

    [Header("检测参数")]
    [Tooltip("勾选后使用手动配置的偏移量，否则根据碰撞体自动计算左右偏移")]
    public bool manual;
    [Tooltip("是否为玩家角色，玩家会额外进行贴墙判定")]
    public bool isPlayer;
    public Vector2 bottomOffset;
    public Vector2 leftOffset;
    public Vector2 rightOffset;
    public float checkRaduis;
    public LayerMask groundLayer;

    [Header("状态")]
    public bool isGround;
    [Tooltip("当前帧脚下真实接触地面（不含土狼跳缓冲）")]
    public bool isSolidGround;
    public bool touchLeftWall;
    public bool touchRightWall;
    public bool onWall;
    public Vector2 groundNormal = Vector2.up;
    public bool isOnSlope;

    bool collisionTouchLeft;
    bool collisionTouchRight;
    bool collisionGround;
    Vector2 collisionGroundNormal;
    bool wasOnSlope;
    Vector2 lastGroundNormal;
    int lastGroundFrame = int.MinValue;
    int lastSlopeFrame = int.MinValue;
    const int GroundCoyoteFrames = 8;
    const int SlopeCoyoteFrames = 10;
    const float SlopeTransitionCastExtra = 0.45f;

    public bool WasOnSlopeRecently =>
        Time.frameCount - lastSlopeFrame <= SlopeCoyoteFrames;

    private void Awake()
    {
        coll = GetComponent<CapsuleCollider2D>();
        rb = GetComponent<Rigidbody2D>();
        if (isPlayer)
            platformDropThrough = GetComponent<PlatformDropThrough>();
        RecalculateOffsets();

        if (isPlayer && coll != null && coll.sharedMaterial == null)
        {
            var noFriction = new PhysicsMaterial2D("PlayerNoFriction")
            {
                friction = 0f,
                bounciness = 0f
            };
            coll.sharedMaterial = noFriction;
        }
    }

    private void Start()
    {
        Check();
    }

    private void Update()
    {
        if (!isPlayer)
            Check();
    }

    void RecalculateOffsets()
    {
        if (manual || coll == null)
            return;

        rightOffset = new Vector2((coll.bounds.size.x + coll.offset.x) / 2, coll.bounds.size.y / 2);
        leftOffset = new Vector2(-rightOffset.x, rightOffset.y);
        bottomOffset = new Vector2(coll.offset.x, coll.offset.y - coll.size.y * 0.5f);
    }

    public void RefreshOffsets() => RecalculateOffsets();

    /// <summary>
    /// 指定水平方向是否被 Ground 层阻挡（含已贴合与即将进入两种情况）。
    /// </summary>
    public bool IsBlockedHorizontally(float direction)
    {
        if (Mathf.Approximately(direction, 0f))
            return false;

        if (direction < 0f)
            return touchLeftWall;
        return touchRightWall;
    }

    void OnCollisionStay2D(Collision2D collision)
    {
        if (((1 << collision.gameObject.layer) & groundLayer) == 0)
            return;

        foreach (ContactPoint2D contact in collision.contacts)
        {
            if (contact.normal.y > 0.5f && TryRegisterCollisionGround(collision.collider, contact.normal))
            {
                collisionGround = true;
                collisionGroundNormal = contact.normal;
            }

            // 仅将接近竖直的法线视为墙体，避免斜坡接触误判为侧墙
            if (Mathf.Abs(contact.normal.y) >= 0.6f)
                continue;

            // 倾斜单向坡端面/棱角不得当作墙，否则坡脚入口会挡住水平移动
            if (IsSlopeSurfaceHit(collision.collider))
                continue;

            if (!CountsAsSolidObstacle(collision.collider))
                continue;

            if (contact.normal.x > 0.3f)
                collisionTouchLeft = true;
            if (contact.normal.x < -0.3f)
                collisionTouchRight = true;
        }
    }

    bool TryRegisterCollisionGround(Collider2D col, Vector2 normal)
    {
        var slope = col.GetComponent<SlopeOneWayPlatform>();
        if (slope != null)
        {
            Vector2 feetPos = new Vector2(coll.bounds.center.x, coll.bounds.min.y);
            return slope.IsFeetAboveSurface(feetPos);
        }

        if (platformDropThrough != null)
            return platformDropThrough.ShouldCountAsGround(col, normal);

        return true;
    }

    bool IsSlopeSurfaceHit(Collider2D hit)
    {
        if (hit == null)
            return false;

        if (hit.GetComponent<SlopeOneWayPlatform>() != null)
            return true;

        return false;
    }

    bool CheckSideOverlap(float direction)
    {
        Bounds bounds = coll.bounds;
        float skin = Mathf.Max(checkRaduis, 0.05f);
        float probeWidth = skin;
        float probeHeight = bounds.size.y * 0.9f;
        float centerX = direction > 0f
            ? bounds.max.x + probeWidth * 0.5f
            : bounds.min.x - probeWidth * 0.5f;

        Vector2 center = new Vector2(centerX, bounds.center.y);
        Vector2 size = new Vector2(probeWidth, probeHeight);
        Collider2D hit = Physics2D.OverlapBox(center, size, 0f, groundLayer);
        if (hit == null)
            return false;

        if (IsSlopeSurfaceHit(hit))
            return false;

        if (!CountsAsSolidObstacle(hit))
            return false;
        return true;
    }

    bool CountsAsSolidObstacle(Collider2D obstacle)
    {
        if (platformDropThrough == null || obstacle == null)
            return true;
        return platformDropThrough.ShouldCollideWith(obstacle);
    }

    bool CountsAsGroundHit(RaycastHit2D hit)
    {
        if (hit.collider == null || hit.normal.y <= 0.5f)
            return false;

        if (platformDropThrough == null)
            return true;

        return platformDropThrough.ShouldCountAsGround(hit.collider, hit.normal);
    }

    /// <summary>
    /// 执行地面、墙体及贴墙状态检测
    /// </summary>
    public void Check()
    {
        float facing = Mathf.Sign(transform.localScale.x);
        if (Mathf.Approximately(facing, 0f))
            facing = 1f;

        Vector2 groundOrigin = (Vector2)transform.position
            + new Vector2(bottomOffset.x * facing, bottomOffset.y);
        RaycastHit2D groundHit = Physics2D.CircleCast(
            groundOrigin, 0.08f, Vector2.down, checkRaduis, groundLayer);

        bool rayGround = groundHit.collider != null && CountsAsGroundHit(groundHit);
        bool rawGround = collisionGround || rayGround;
        Vector2 bridgeNormal = Vector2.zero;

        if (!rawGround && (wasOnSlope || WasOnSlopeRecently))
        {
            RaycastHit2D slopeExitHit = Physics2D.CircleCast(
                groundOrigin, 0.08f, Vector2.down, checkRaduis + SlopeTransitionCastExtra, groundLayer);
            if (slopeExitHit.collider != null && CountsAsGroundHit(slopeExitHit))
            {
                rawGround = true;
                bridgeNormal = slopeExitHit.normal;
            }
        }

        if (rawGround)
        {
            if (collisionGround)
                groundNormal = collisionGroundNormal;
            else if (rayGround)
                groundNormal = groundHit.normal;
            else
                groundNormal = bridgeNormal;

            lastGroundNormal = groundNormal;
            lastGroundFrame = Time.frameCount;
        }

        isSolidGround = rawGround;
        isGround = rawGround || Time.frameCount - lastGroundFrame <= GroundCoyoteFrames;
        if (isGround && !rawGround)
            groundNormal = lastGroundNormal;
        else if (!isGround)
            groundNormal = Vector2.up;

        isOnSlope = isGround && groundNormal.y > 0.5f && groundNormal.y < 0.99f;
        if (isOnSlope)
            lastSlopeFrame = Time.frameCount;
        wasOnSlope = isOnSlope;

        collisionGround = false;

        touchLeftWall = collisionTouchLeft || CheckSideOverlap(-1f);
        touchRightWall = collisionTouchRight || CheckSideOverlap(1f);
        collisionTouchLeft = false;
        collisionTouchRight = false;

        if (isPlayer && rb != null)
        {
            float inputX = Input.GetAxisRaw("Horizontal");
            onWall = (touchLeftWall && inputX < 0f || touchRightWall && inputX > 0f) && rb.linearVelocity.y < 0f;
        }
    }

    private void OnDrawGizmosSelected()
    {
        if (coll == null)
            coll = GetComponent<CapsuleCollider2D>();
        if (coll == null)
            return;

        float facing = Mathf.Sign(transform.localScale.x);
        if (Mathf.Approximately(facing, 0f))
            facing = 1f;

        Vector2 groundOrigin = (Vector2)transform.position
            + new Vector2(bottomOffset.x * facing, bottomOffset.y);
        Gizmos.color = Color.green;
        Gizmos.DrawLine(groundOrigin, groundOrigin + Vector2.down * checkRaduis);

        Bounds bounds = coll.bounds;
        float skin = Mathf.Max(checkRaduis, 0.05f);
        float probeHeight = bounds.size.y * 0.9f;
        Gizmos.color = Color.red;
        DrawSideProbeGizmo(bounds.min.x - skin * 0.5f, bounds.center.y, skin, probeHeight);
        DrawSideProbeGizmo(bounds.max.x + skin * 0.5f, bounds.center.y, skin, probeHeight);
    }

    static void DrawSideProbeGizmo(float centerX, float centerY, float width, float height)
    {
        Vector3 center = new Vector3(centerX, centerY, 0f);
        Vector3 size = new Vector3(width, height, 0f);
        Gizmos.DrawWireCube(center, size);
    }
}
