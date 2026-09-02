using UnityEngine;

/// <summary>
/// 手雷远程敌人：AI 循环与 RangedEnemy 一致，Shot 时以玩家当前位置为落点投雷。
/// 精英可开启 Jump 替代蹲伏能力。
/// </summary>
public class GrenadeEnemy : RangedEnemy
{
    /// <summary>
    /// 新像素精灵默认朝右，与旧 Metal Slug 朝左资源相反。
    /// </summary>
    protected override bool SpriteFacesRight => true;

    [Header("手雷")]
    public EnemyGrenade grenadePrefab;
    public Transform throwPoint;
    [Tooltip("抛射角（度，相对水平向上；0=平抛，90=竖直向上）。落点可解时按此角反算速度")]
    [SerializeField] float throwAngle = 35.5f;
    [Tooltip("弹道不可解时的兜底参考速度，不再直接决定落点")]
    [SerializeField] float throwSpeed = 8.6f;

    const float MinBallisticTime = 0.05f;
    const float FallbackMinFlightTime = 0.35f;
    const float FallbackMaxFlightTime = 0.9f;

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
    /// 以玩家当前坐标为落点投出一枚手雷；超出 shootRange 则夹到攻击距离边缘。
    /// </summary>
    public void ThrowGrenade()
    {
        if (grenadePrefab == null || player == null)
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

    Vector2 GetGrenadeLandingPoint(Vector2 origin)
    {
        Vector2 target = player.position;
        Vector2 offset = target - origin;
        float dist = offset.magnitude;
        if (shootRange <= 0f || dist <= shootRange)
            return target;

        return origin + offset / dist * shootRange;
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
