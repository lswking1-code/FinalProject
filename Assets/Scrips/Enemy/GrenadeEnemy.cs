using UnityEngine;

/// <summary>
/// 手雷远程敌人：AI 循环与 RangedEnemy 一致，Shot 时以玩家水平位置对应地面为落点投雷。
/// 精英可开启 Jump 替代蹲伏能力。
/// 勾选 enableRollGrenade 后改为投掷贴地滚雷。
/// </summary>
public class GrenadeEnemy : RangedEnemy
{
    /// <summary>
    /// 新像素精灵默认朝右，与旧 Metal Slug 朝左资源相反。
    /// </summary>
    protected override bool SpriteFacesRight => true;

    protected override bool AllowCrouchActions => false;

    [Header("手雷")]
    public EnemyGrenade grenadePrefab;
    public Transform throwPoint;
    [Tooltip("抛射角（度，相对水平向上；0=平抛，90=竖直向上）。落点可解时按此角反算速度")]
    [SerializeField] float throwAngle = 35.5f;
    [Tooltip("弹道不可解时的兜底参考速度，不再直接决定落点")]
    [SerializeField] float throwSpeed = 8.6f;
    [Tooltip("最小抛射水平距离；玩家更近时仍掷到该距离。不会大于 shootRange")]
    [SerializeField] float minThrowDistance = 2f;

    [Header("进阶能力")]
    [Tooltip("勾选后改为投掷滚雷，不再投抛物线手雷")]
    public bool enableRollGrenade;
    [Tooltip("滚雷预制体；为空则回退 grenadePrefab，并由 InitRoll 强制滚雷行为")]
    public EnemyGrenade rollGrenadePrefab;

    const float MinBallisticTime = 0.05f;
    const float FallbackMinFlightTime = 0.35f;
    const float FallbackMaxFlightTime = 0.9f;
    const float LandingRayStartOffsetY = 0.25f;
    const float LandingRayDistance = 10f;
    const float MinLandingDx = 0.2f;

    [Header("跃起")]
    [Tooltip("起跳目标高度，用于反算初速度")]
    public float jumpHeight = 2.5f;
    [Tooltip("跃起水平速度")]
    public float jumpHorizontalSpeed = 4f;
    [Tooltip("落地动画持续时间")]
    public float landDuration = 0.25f;

    protected override void Awake()
    {
        base.Awake();
        shotState = new GrenadeThrowState();
        jumpState = new GrenadeJumpState();
    }

    /// <summary>
    /// 以玩家水平位置为落点投出一枚手雷；高度取该处地面。
    /// 水平距离夹在 minThrowDistance 与 shootRange 之间。
    /// 开启 enableRollGrenade 时改为朝玩家投出贴地滚雷。
    /// </summary>
    public void ThrowGrenade()
    {
        if (player == null)
            return;

        if (enableRollGrenade)
        {
            ThrowRollGrenade();
            return;
        }

        if (grenadePrefab == null)
            return;

        Vector3 spawnPos = throwPoint != null ? throwPoint.position : transform.position;
        Vector2 origin = spawnPos;
        Vector2 landing = GetGrenadeLandingPoint(origin);
        Vector2 velocity = ComputeThrowVelocity(origin, landing);

        float dir = Mathf.Sign(velocity.x);
        if (dir == 0f)
            dir = Mathf.Sign(player.position.x - transform.position.x);
        if (dir == 0f)
            dir = faceDir.x;

        float speed = velocity.magnitude;
        float angle = speed > 0.001f
            ? Mathf.Atan2(velocity.y, Mathf.Abs(velocity.x)) * Mathf.Rad2Deg
            : throwAngle;

        var throwerCollider = GetComponent<Collider2D>();
        var grenade = Instantiate(grenadePrefab, spawnPos, Quaternion.identity);
        EnemySceneCleanup.PlaceInSourceScene(grenade.gameObject, this);
        grenade.Init(dir, Vector2.zero, throwerCollider, angle, speed);
        FacePlayer();
    }

    void ThrowRollGrenade()
    {
        EnemyGrenade prefab = rollGrenadePrefab != null ? rollGrenadePrefab : grenadePrefab;
        if (prefab == null)
            return;

        Vector3 spawnPos = throwPoint != null ? throwPoint.position : transform.position;
        float dir = Mathf.Sign(player.position.x - transform.position.x);
        if (dir == 0f)
            dir = faceDir.x != 0f ? Mathf.Sign(faceDir.x) : 1f;

        Vector2 throwerVelocity = Rb != null ? Rb.linearVelocity : Vector2.zero;
        var throwerCollider = GetComponent<Collider2D>();
        var grenade = Instantiate(prefab, spawnPos, Quaternion.identity);
        EnemySceneCleanup.PlaceInSourceScene(grenade.gameObject, this);
        grenade.InitRoll(dir, throwerVelocity, throwerCollider);
        FacePlayer();
    }

    Vector2 GetGrenadeLandingPoint(Vector2 origin)
    {
        float dx = player.position.x - origin.x;
        float minDx = Mathf.Max(MinLandingDx, minThrowDistance);
        if (shootRange > 0f)
            minDx = Mathf.Min(minDx, shootRange);

        if (Mathf.Abs(dx) < minDx)
        {
            float side = Mathf.Sign(dx);
            if (side == 0f)
                side = faceDir.x != 0f ? Mathf.Sign(faceDir.x) : 1f;
            dx = side * minDx;
        }

        if (shootRange > 0f)
            dx = Mathf.Clamp(dx, -shootRange, shootRange);

        float landingX = origin.x + dx;
        float landingY = TryRaycastLandingGround(landingX, out float groundY)
            ? groundY
            : origin.y;
        return new Vector2(landingX, landingY);
    }

    bool TryRaycastLandingGround(float landingX, out float groundY)
    {
        groundY = 0f;
        LayerMask mask = physicsCheck != null && physicsCheck.groundLayer.value != 0
            ? physicsCheck.groundLayer
            : (LayerMask)LayerMask.GetMask("Ground");
        if (mask.value == 0)
            return false;

        Vector2 origin = new Vector2(landingX, player.position.y + LandingRayStartOffsetY);
        RaycastHit2D hit = Physics2D.Raycast(origin, Vector2.down, LandingRayDistance, mask);
        if (hit.collider == null)
            return false;

        groundY = hit.point.y;
        return true;
    }

    Vector2 ComputeThrowVelocity(Vector2 origin, Vector2 landing)
    {
        if (TryComputeAngledThrowVelocity(origin, landing, out Vector2 velocity))
            return velocity;

        return ComputeFallbackThrowVelocity(origin, landing);
    }

    bool TryComputeAngledThrowVelocity(Vector2 origin, Vector2 landing, out Vector2 velocity)
    {
        velocity = Vector2.zero;

        float dx = landing.x - origin.x;
        float dy = landing.y - origin.y;
        float g = Physics2D.gravity.y;
        if (Mathf.Abs(dx) < 0.01f || Mathf.Abs(g) < 0.01f)
            return false;

        float angleRad = throwAngle * Mathf.Deg2Rad;
        float cos = Mathf.Cos(angleRad);
        if (Mathf.Abs(cos) < 0.01f)
            return false;

        float tanTheta = Mathf.Tan(angleRad);
        float rangeX = Mathf.Abs(dx);
        float tSq = 2f * (dy - rangeX * tanTheta) / g;
        if (tSq <= MinBallisticTime * MinBallisticTime)
            return false;

        float t = Mathf.Sqrt(tSq);
        float vxAbs = rangeX / t;
        float vy = vxAbs * tanTheta;
        velocity = new Vector2(Mathf.Sign(dx) * vxAbs, vy);
        return velocity.sqrMagnitude > 0.0001f;
    }

    Vector2 ComputeFallbackThrowVelocity(Vector2 origin, Vector2 landing)
    {
        float dx = landing.x - origin.x;
        float dy = landing.y - origin.y;
        float dist = Vector2.Distance(origin, landing);
        float range = Mathf.Max(0.01f, shootRange);
        float tMin = FallbackMinFlightTime;
        float tMax = FallbackMaxFlightTime;
        if (throwSpeed > 0.1f)
        {
            float cos = Mathf.Max(0.2f, Mathf.Abs(Mathf.Cos(throwAngle * Mathf.Deg2Rad)));
            tMax = Mathf.Clamp(range / (throwSpeed * cos), tMin + 0.05f, 1.25f);
        }

        float t = Mathf.Lerp(tMin, tMax, Mathf.Clamp01(dist / range));

        t = Mathf.Max(MinBallisticTime, t);
        float g = Physics2D.gravity.y;
        float vx = dx / t;
        float vy = (dy - 0.5f * g * t * t) / t;
        if (Mathf.Abs(vx) < 0.01f && Mathf.Abs(dx) < 0.01f)
            vx = (faceDir.x == 0f ? 1f : faceDir.x) * 0.01f;

        return new Vector2(vx, vy);
    }
}
