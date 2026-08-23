using UnityEngine;

/// <summary>
/// 物理环境检测组件，用于检测地面与墙体碰撞状态。
/// 玩家与敌人均可挂载；敌人仅需地面/墙体检测，玩家可额外启用贴墙判定。
/// 碰撞体优先 CapsuleCollider2D，否则回退到任意 Collider2D（如飞行敌人的 CircleCollider2D）。
/// </summary>
public class PhysicsCheck : MonoBehaviour
{
    private Collider2D coll;
    private CapsuleCollider2D capsuleColl;
    private Rigidbody2D rb;
    PlatformDropThrough platformDropThrough;
    RobotOneWayPlatformPass robotOneWayPlatformPass;

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
    [Tooltip("脚下主要接触单向/Platform 层平台（非斜坡）。平台上不做贴边拦截，可走下去。")]
    public bool isOnPlatform;
    public bool touchLeftWall;
    public bool touchRightWall;
    public bool onWall;
    public Vector2 groundNormal = Vector2.up;
    public bool isOnSlope;

    /// <summary>
    /// 实心地面上拦截悬崖；刚离开实心地面且仍在下落的短窗口内也拦截，避免迈出后空中继续追击。
    /// 单向平台上不拦截，允许走下去。
    /// </summary>
    public bool ShouldRespectLedge
    {
        get
        {
            if (isOnPlatform)
                return false;
            if (isGround)
                return true;
            if (!lastStandingWasSolid)
                return false;
            if (Time.time - lastSolidGroundTime > AirLedgeHold)
                return false;
            if (rb != null && rb.linearVelocity.y > 0.15f)
                return false;
            return true;
        }
    }

    bool collisionTouchLeft;
    bool collisionTouchRight;
    bool collisionGround;
    bool collisionOnSolidGround;
    bool collisionOnPlatform;
    Vector2 collisionGroundNormal;
    bool wasOnSlope;
    Vector2 lastGroundNormal;
    int lastGroundFrame = int.MinValue;
    int lastSlopeFrame = int.MinValue;
    bool lastStandingWasSolid;
    float lastSolidGroundTime = -999f;
    const int GroundCoyoteFrames = 8;
    const int SlopeCoyoteFrames = 10;
    const float SlopeTransitionCastExtra = 0.45f;
    const float AirLedgeHold = 0.35f;
    const float FlatWalkableDrop = 1.25f;
    const float SlopeWalkableDrop = 1.35f;
    const float LedgeProbeLift = 0.35f;
    const float LedgeProbeContinue = 0.05f;
    const int LedgeProbeMaxSteps = 8;
    readonly RaycastHit2D[] hazardProbeHits = new RaycastHit2D[8];
    readonly Collider2D[] sideOverlapHits = new Collider2D[8];
    Collider2D[] ledgeColliders;

    public bool WasOnSlopeRecently =>
        Time.frameCount - lastSlopeFrame <= SlopeCoyoteFrames;

    private void Awake()
    {
        ResolveCollider();
        rb = GetComponent<Rigidbody2D>();
        if (isPlayer)
            platformDropThrough = GetComponent<PlatformDropThrough>();
        else
            robotOneWayPlatformPass = GetComponent<RobotOneWayPlatformPass>();
        RecalculateOffsets();
        CacheLedgeColliders();

        // 玩家与机器人都去掉摩擦/弹性，避免斜面和平台接缝把胶囊绊住
        if (capsuleColl != null && capsuleColl.sharedMaterial == null
            && (isPlayer || robotOneWayPlatformPass != null))
        {
            var noFriction = new PhysicsMaterial2D("PlayerNoFriction")
            {
                friction = 0f,
                bounciness = 0f
            };
            capsuleColl.sharedMaterial = noFriction;
        }
    }

    void ResolveCollider()
    {
        capsuleColl = GetComponent<CapsuleCollider2D>();
        coll = capsuleColl != null ? capsuleColl : GetComponent<Collider2D>();
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

        Vector2 size = GetColliderLocalSize();
        rightOffset = new Vector2((size.x + coll.offset.x) / 2f, size.y / 2f);
        leftOffset = new Vector2(-rightOffset.x, rightOffset.y);
        bottomOffset = new Vector2(coll.offset.x, coll.offset.y - size.y * 0.5f);
    }

    Vector2 GetColliderLocalSize()
    {
        if (capsuleColl != null)
            return capsuleColl.size;

        if (coll is BoxCollider2D box)
            return box.size;

        if (coll is CircleCollider2D circle)
        {
            float diameter = circle.radius * 2f;
            return new Vector2(diameter, diameter);
        }

        // Polygon 等：用相对缩放的世界包围盒近似本地尺寸
        Vector3 lossy = transform.lossyScale;
        Vector2 worldSize = coll.bounds.size;
        float sx = Mathf.Abs(lossy.x) > 0.0001f ? worldSize.x / Mathf.Abs(lossy.x) : worldSize.x;
        float sy = Mathf.Abs(lossy.y) > 0.0001f ? worldSize.y / Mathf.Abs(lossy.y) : worldSize.y;
        return new Vector2(sx, sy);
    }

    public void RefreshOffsets() => RecalculateOffsets();

    public void RefreshLedgeColliders() => CacheLedgeColliders();

    void CacheLedgeColliders()
    {
        ledgeColliders = GetComponentsInChildren<Collider2D>(false);
    }

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

    /// <summary>
    /// 指定水平方向前方脚底是否仍有可走地面。
    /// 只用身体碰撞体探测，忽略攻击/索敌 Trigger，避免把台阶前方误判成悬崖。
    /// 一格高的向下台阶视为可走，两格以上的落差仍拦截。
    /// </summary>
    public bool HasGroundAhead(float direction, float lookAheadPadding = -1f)
    {
        if (coll == null)
            ResolveCollider();
        if (coll == null || groundLayer.value == 0)
            return true;
        if (Mathf.Approximately(direction, 0f))
            return true;

        float dir = Mathf.Sign(direction);
        Bounds body = coll.bounds;
        float footY = body.min.y;
        float frontX = dir > 0f ? body.max.x : body.min.x;
        float maxDrop = (isOnSlope || WasOnSlopeRecently) ? SlopeWalkableDrop : FlatWalkableDrop;
        float pad = lookAheadPadding >= 0f ? lookAheadPadding : 0.12f;

        // 在身体前缘附近采几点：近点仍在当前地面则继续走，跨上台阶则检查落差
        if (TryGetWalkableGroundY(frontX + dir * pad, footY, maxDrop, out _))
            return true;
        if (TryGetWalkableGroundY(frontX + dir * (pad + 0.14f), footY, maxDrop, out _))
            return true;
        return TryGetWalkableGroundY(frontX + dir * (pad + 0.28f), footY, maxDrop, out _);
    }

    /// <summary>
    /// 在指定 X 向下寻找朝上的可走表面。跳过台阶立面 / 自身碰撞体，避免把小落差判成悬崖。
    /// </summary>
    bool TryGetWalkableGroundY(float x, float footY, float maxDrop, out float groundY)
    {
        groundY = footY;
        Vector2 origin = new Vector2(x, footY + LedgeProbeLift);
        float remaining = LedgeProbeLift + maxDrop;

        for (int i = 0; i < LedgeProbeMaxSteps && remaining > 0.01f; i++)
        {
            RaycastHit2D hit = Physics2D.Raycast(origin, Vector2.down, remaining, groundLayer);
            if (hit.collider == null)
                return false;

            if (IsLedgeSelfCollider(hit.collider) || hit.collider.isTrigger || IsPickupCollider(hit.collider))
            {
                AdvanceLedgeProbe(ref origin, ref remaining, hit);
                continue;
            }

            if (CountsAsGroundHit(hit))
            {
                if (footY - hit.point.y > maxDrop)
                    return false;
                groundY = hit.point.y;
                return true;
            }

            AdvanceLedgeProbe(ref origin, ref remaining, hit);
        }

        return false;
    }

    static void AdvanceLedgeProbe(ref Vector2 origin, ref float remaining, RaycastHit2D hit)
    {
        float advance = Mathf.Max(LedgeProbeContinue, hit.distance) + LedgeProbeContinue;
        origin += Vector2.down * advance;
        remaining -= advance;
    }

    bool IsLedgeSelfCollider(Collider2D other)
    {
        if (other == null)
            return false;
        if (other == coll)
            return true;
        if (ledgeColliders == null)
            return false;

        for (int i = 0; i < ledgeColliders.Length; i++)
        {
            if (ledgeColliders[i] == other)
                return true;
        }

        return false;
    }

    /// <summary>
    /// 贴边探测包围盒：根碰撞体 + 同刚体上的身体碰撞体（含盾牌），排除攻击判定盒。
    /// </summary>
    Bounds GetLedgeProbeBounds()
    {
        if (coll == null)
            ResolveCollider();
        if (coll == null)
            return new Bounds(transform.position, Vector3.one * 0.1f);

        Bounds bounds = coll.bounds;
        if (ledgeColliders == null || ledgeColliders.Length == 0)
            CacheLedgeColliders();

        if (ledgeColliders == null)
            return bounds;

        for (int i = 0; i < ledgeColliders.Length; i++)
        {
            Collider2D extra = ledgeColliders[i];
            if (extra == null || extra == coll || !extra.enabled || extra.isTrigger)
                continue;
            if (extra.GetComponent<Attack>() != null)
                continue;
            bounds.Encapsulate(extra.bounds);
        }

        return bounds;
    }

    /// <summary>
    /// 沿水平方向从 fromX 扫到 toX，检测是否存在无法贴地走过的悬崖/缺口。
    /// 会跟随已探测到的地面高度，避免把连续斜坡误判为悬崖。
    /// </summary>
    public bool HasCliffGapAlongX(float fromX, float toX, float footY, float maxWalkableDrop = 0.85f)
    {
        if (groundLayer.value == 0)
            return false;

        float dx = toX - fromX;
        if (Mathf.Abs(dx) < 0.12f)
            return false;

        const float step = 0.35f;
        float dir = Mathf.Sign(dx);
        float dist = Mathf.Abs(dx);
        float groundY = footY;
        float drop = Mathf.Max(maxWalkableDrop, FlatWalkableDrop);

        int steps = Mathf.Max(1, Mathf.CeilToInt(dist / step));
        for (int i = 1; i <= steps; i++)
        {
            float x = fromX + dir * Mathf.Min(dist, i * step);
            if (!TryGetWalkableGroundY(x, groundY, drop, out float nextY))
                return true;
            if (groundY - nextY > drop)
                return true;
            groundY = nextY;
        }

        return false;
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
                if (IsSolidGroundSurface(collision.collider))
                    collisionOnSolidGround = true;
                else if (IsDropOffPlatform(collision.collider))
                    collisionOnPlatform = true;
            }

            // 仅将接近竖直的法线视为墙体，避免斜坡接触误判为侧墙
            if (Mathf.Abs(contact.normal.y) >= 0.6f)
                continue;

            // 倾斜单向坡端面/棱角不得当作墙，否则坡脚入口会挡住水平移动
            if (IsSlopeSurfaceHit(collision.collider))
                continue;

            // 可推物顶面在 Ground 层供站立，侧面接触不能当墙，否则站立推箱会被清速度
            if (IsPushablePropSurface(collision.collider))
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
        if (IsCollisionIgnored(col))
            return false;

        var pathSlope = col.GetComponent<SlopePathSegment>()
            ?? col.GetComponentInParent<SlopePathSegment>();
        if (pathSlope != null)
        {
            if (coll == null)
                return false;
            Vector2 feetPos = new Vector2(coll.bounds.center.x, coll.bounds.min.y);
            return pathSlope.IsFeetAboveSurface(feetPos);
        }

        var slope = col.GetComponent<SlopeOneWayPlatform>();
        if (slope != null)
        {
            if (coll == null)
                return false;

            Vector2 feetPos = new Vector2(coll.bounds.center.x, coll.bounds.min.y);
            return slope.IsFeetAboveSurface(feetPos);
        }

        if (platformDropThrough != null)
            return platformDropThrough.ShouldCountAsGround(col, normal);

        if (robotOneWayPlatformPass != null)
            return robotOneWayPlatformPass.ShouldCountAsGround(col, normal);

        return true;
    }

    bool IsSlopeSurfaceHit(Collider2D hit)
    {
        if (hit == null)
            return false;

        if (hit.GetComponent<SlopePathSegment>() != null
            || hit.GetComponentInParent<SlopePathSegment>() != null)
            return true;

        if (hit.GetComponent<SlopeOneWayPlatform>() != null)
            return true;

        return false;
    }

    static bool IsOneWayPlatformCollider(Collider2D hit)
    {
        if (hit == null)
            return false;

        var effector = hit.GetComponent<PlatformEffector2D>();
        if (effector == null)
            effector = hit.GetComponentInParent<PlatformEffector2D>();

        return effector != null && effector.enabled && effector.useOneWay;
    }

    static bool IsPushablePropSurface(Collider2D col)
    {
        return col != null && col.GetComponentInParent<PushableProp>() != null;
    }

    void UpdateOnPlatform(bool rawGround, bool rayGround, RaycastHit2D groundHit)
    {
        if (rawGround)
        {
            bool rayOnSolid = rayGround && IsSolidGroundSurface(groundHit.collider);
            bool rayOnPlatform = rayGround && IsDropOffPlatform(groundHit.collider);
            bool onSolid = collisionOnSolidGround || rayOnSolid;
            isOnPlatform = !onSolid && (collisionOnPlatform || rayOnPlatform);
            return;
        }

        if (!isGround)
            isOnPlatform = false;
    }

    bool IsSolidGroundSurface(Collider2D col)
    {
        if (col == null)
            return false;
        if (IsSlopeSurfaceHit(col))
            return true;

        int ground = LayerMask.NameToLayer("Ground");
        return ground >= 0 && col.gameObject.layer == ground;
    }

    bool IsDropOffPlatform(Collider2D col)
    {
        if (col == null || IsSlopeSurfaceHit(col))
            return false;

        int platform = LayerMask.NameToLayer("Platform");
        if (platform >= 0 && col.gameObject.layer == platform)
            return true;

        return IsOneWayPlatformCollider(col);
    }

    bool CheckSideOverlap(float direction)
    {
        if (coll == null)
            return false;

        Bounds bounds = coll.bounds;
        float skin = Mathf.Max(checkRaduis, 0.05f);
        float probeWidth = skin;
        float probeHeight = bounds.size.y * 0.9f;
        float centerX = direction > 0f
            ? bounds.max.x + probeWidth * 0.5f
            : bounds.min.x - probeWidth * 0.5f;

        Vector2 center = new Vector2(centerX, bounds.center.y);
        Vector2 size = new Vector2(probeWidth, probeHeight);
        int count = Physics2D.OverlapBoxNonAlloc(center, size, 0f, sideOverlapHits, groundLayer);
        for (int i = 0; i < count; i++)
        {
            Collider2D hit = sideOverlapHits[i];
            if (hit == null)
                continue;

            if (IsSlopeSurfaceHit(hit))
                continue;

            // 单向板（含爆炸/掉落平台）侧面不是墙；Overlap 扫到邻块体积时不能当成侧挡
            if (IsOneWayPlatformCollider(hit))
                continue;

            if (IsPushablePropSurface(hit))
                continue;

            if (!CountsAsSolidObstacle(hit))
                continue;

            return true;
        }

        return false;
    }

    bool CountsAsSolidObstacle(Collider2D obstacle)
    {
        if (obstacle == null)
            return true;

        if (IsPickupCollider(obstacle))
            return false;

        if (platformDropThrough != null)
            return platformDropThrough.ShouldCollideWith(obstacle);

        if (robotOneWayPlatformPass != null)
            return robotOneWayPlatformPass.ShouldCollideWith(obstacle);

        return true;
    }

    bool CountsAsGroundHit(RaycastHit2D hit)
    {
        if (hit.collider == null || hit.normal.y <= 0.5f)
            return false;

        if (IsCollisionIgnored(hit.collider))
            return false;

        if (platformDropThrough != null)
            return platformDropThrough.ShouldCountAsGround(hit.collider, hit.normal);

        if (robotOneWayPlatformPass != null)
            return robotOneWayPlatformPass.ShouldCountAsGround(hit.collider, hit.normal);

        return true;
    }

    bool IsCollisionIgnored(Collider2D other)
    {
        if (IsPickupCollider(other))
            return true;
        return coll != null && other != null && Physics2D.GetIgnoreCollision(coll, other);
    }

    static bool IsPickupCollider(Collider2D other)
    {
        if (other == null)
            return false;

        return other.GetComponent<BulletBox>() != null
            || other.GetComponent<HealthPack>() != null
            || other.GetComponent<LifePack>() != null;
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
        UpdateOnPlatform(rawGround, rayGround, groundHit);
        if (rawGround)
        {
            lastStandingWasSolid = !isOnPlatform;
            if (lastStandingWasSolid)
                lastSolidGroundTime = Time.time;
        }
        TryNotifyElectrifiedPlatform(groundOrigin);
        if (isGround && !rawGround)
            groundNormal = lastGroundNormal;
        else if (!isGround)
            groundNormal = Vector2.up;

        isOnSlope = isGround && groundNormal.y > 0.5f && groundNormal.y < 0.99f;
        if (isOnSlope)
            lastSlopeFrame = Time.frameCount;
        wasOnSlope = isOnSlope;

        touchLeftWall = collisionTouchLeft || CheckSideOverlap(-1f);
        touchRightWall = collisionTouchRight || CheckSideOverlap(1f);

        // 玩家 Update 也会 Check；只在 FixedUpdate 消费碰撞标记，避免 Stay 结果被 Update 吃掉后物理帧判空中。
        if (!isPlayer || Time.inFixedTimeStep)
        {
            collisionGround = false;
            collisionOnSolidGround = false;
            collisionOnPlatform = false;
            collisionTouchLeft = false;
            collisionTouchRight = false;
        }

        if (isPlayer && rb != null)
        {
            float inputX = Input.GetAxisRaw("Horizontal");
            onWall = (touchLeftWall && inputX < 0f || touchRightWall && inputX > 0f) && rb.linearVelocity.y < 0f;
        }
    }

    void TryNotifyElectrifiedPlatform(Vector2 groundOrigin)
    {
        if (!isSolidGround)
            return;

        var character = GetComponent<Character>();
        if (character == null)
            return;

        int count = Physics2D.CircleCastNonAlloc(
            groundOrigin, 0.08f, Vector2.down, hazardProbeHits, checkRaduis);
        for (int i = 0; i < count; i++)
        {
            Collider2D hitCol = hazardProbeHits[i].collider;
            if (hitCol == null || hitCol.isTrigger)
                continue;
            if (hitCol == coll || hitCol.transform.IsChildOf(transform) || transform.IsChildOf(hitCol.transform))
                continue;

            var plat = hitCol.GetComponentInParent<ElectrifiedPlatform>();
            if (plat != null)
                plat.NotifyStanding(character);
            break;
        }
    }

    private void OnDrawGizmosSelected()
    {
        if (coll == null)
            ResolveCollider();
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
