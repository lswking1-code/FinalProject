using UnityEngine;

/// <summary>
/// 火箭兵：AI 循环与 RangedEnemy 一致，战斗射程为二维全向圆；
/// 射击时在八向中选取最接近玩家的方向发射直线导弹。
/// 可选专注模式：MOVE 时原地停留，时长与 actionDuration 一致。
/// </summary>
public class RocketEnemy : RangedEnemy
{
    /// <summary>
    /// 新像素精灵默认朝右，与旧 Metal Slug 朝左资源相反。
    /// </summary>
    protected override bool SpriteFacesRight => true;

    static readonly Vector2[] Dirs8 =
    {
        Vector2.right,
        new Vector2(1f, 1f).normalized,
        Vector2.up,
        new Vector2(-1f, 1f).normalized,
        Vector2.left,
        new Vector2(-1f, -1f).normalized,
        Vector2.down,
        new Vector2(1f, -1f).normalized,
    };

    [Header("导弹")]
    public EnemyMissile missilePrefab;
    [Tooltip("开火点绕敌人中心的半径；<=0 时用 FirePoint 初始局部位移长度")]
    [SerializeField] float firePointRadius;
    [Tooltip("发射圆心相对敌人 transform 的偏移，用来把八向发射环整体上移")]
    [SerializeField] Vector2 fireOriginOffset = new Vector2(0f, 0.4f);
    [Tooltip("水平已贴齐但仍因高度差超出射程时，视为可射击，避免 GetClose 卡死")]
    [SerializeField] float heightDeadlockSlack = 0.5f;

    float cachedFirePointRadius = -1f;

    void Reset()
    {
        shootRange = 10f;
        reloadDuration = 1.5f;
    }

    protected override void Awake()
    {
        base.Awake();
        CacheFirePointRadius();
    }

    public override float GetCombatDistanceToPlayer()
    {
        EnsurePlayerReference();
        if (player == null)
            return float.MaxValue;

        float euclidean = Vector2.Distance(transform.position, player.position);
        float dx = Mathf.Abs(transform.position.x - player.position.x);
        if (euclidean > shootRange && dx <= heightDeadlockSlack)
            return shootRange;

        return euclidean;
    }

    public override void FireProjectile()
    {
        if (missilePrefab == null || player == null)
            return;

        FacePlayer();

        Vector2 origin = GetFireOrigin();
        Vector2 toPlayer = (Vector2)player.position - origin;
        Vector2 dir = SnapToNearest8Dir(toPlayer);
        if (dir == Vector2.zero)
            dir = new Vector2(faceDir.x >= 0f ? 1f : -1f, 0f);

        Vector3 spawnPos = origin + dir * GetFirePointRadius();
        spawnPos.z = transform.position.z;
        if (firePoint != null)
            firePoint.position = spawnPos;

        var throwerCollider = GetComponent<Collider2D>();
        var missile = Instantiate(missilePrefab, spawnPos, Quaternion.identity);
        EnemySceneCleanup.PlaceInSourceScene(missile.gameObject, this);
        missile.Init(dir, throwerCollider);
    }

    Vector2 GetFireOrigin()
    {
        return (Vector2)transform.position + fireOriginOffset;
    }

    float GetFirePointRadius()
    {
        if (cachedFirePointRadius < 0f)
            CacheFirePointRadius();

        return cachedFirePointRadius;
    }

    void CacheFirePointRadius()
    {
        if (firePointRadius > 0f)
        {
            cachedFirePointRadius = firePointRadius;
            return;
        }

        if (firePoint != null)
            cachedFirePointRadius = ((Vector2)firePoint.localPosition).magnitude;

        if (cachedFirePointRadius <= 0.001f)
            cachedFirePointRadius = 1.5f;
    }

    static Vector2 SnapToNearest8Dir(Vector2 desired)
    {
        if (desired.sqrMagnitude < 0.0001f)
            return Vector2.zero;

        desired.Normalize();
        Vector2 best = Dirs8[0];
        float bestDot = Vector2.Dot(desired, best);
        for (int i = 1; i < Dirs8.Length; i++)
        {
            float d = Vector2.Dot(desired, Dirs8[i]);
            if (d > bestDot)
            {
                bestDot = d;
                best = Dirs8[i];
            }
        }

        return best;
    }

    protected override void DrawShootRangeGizmo()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, shootRange);

        float radius = firePointRadius > 0f
            ? firePointRadius
            : (firePoint != null ? ((Vector2)firePoint.localPosition).magnitude : 1.5f);
        Vector3 origin = GetFireOrigin();
        origin.z = transform.position.z;
        Gizmos.color = Color.magenta;
        Gizmos.DrawWireSphere(origin, 0.1f);
        Gizmos.color = Color.cyan;
        for (int i = 0; i < Dirs8.Length; i++)
            Gizmos.DrawWireSphere(origin + (Vector3)(Dirs8[i] * radius), 0.08f);
    }
}
