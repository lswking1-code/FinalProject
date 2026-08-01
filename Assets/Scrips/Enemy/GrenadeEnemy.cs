using UnityEngine;

/// <summary>
/// 手雷远程敌人：AI 循环与 RangedEnemy 一致，Shot 时向前投掷一枚手雷；
/// 精英可开启 Jump 替代蹲伏能力。
/// </summary>
public class GrenadeEnemy : RangedEnemy
{
    [Header("手雷")]
    public EnemyGrenade grenadePrefab;
    public Transform throwPoint;
    [Tooltip("抛射角（度，相对水平向上；0=平抛，90=竖直向上）")]
    [SerializeField] float throwAngle = 35.5f;
    [Tooltip("抛射速度（越大飞得越远/越高）")]
    [SerializeField] float throwSpeed = 8.6f;

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
    /// 在 throwPoint 向前投掷一枚手雷（仅站立抛点）。
    /// </summary>
    public void ThrowGrenade()
    {
        if (grenadePrefab == null || player == null)
            return;

        Vector3 spawnPos = throwPoint != null ? throwPoint.position : transform.position;
        float dir = Mathf.Sign(player.position.x - transform.position.x);
        if (dir == 0f)
            dir = faceDir.x;

        Vector2 throwerVelocity = Rb != null ? Rb.linearVelocity : Vector2.zero;
        var throwerCollider = GetComponent<Collider2D>();

        var grenade = Instantiate(grenadePrefab, spawnPos, Quaternion.identity);
        EnemySceneCleanup.PlaceInSourceScene(grenade.gameObject, this);
        grenade.Init(dir, throwerVelocity, throwerCollider, throwAngle, throwSpeed);
        FacePlayer();
    }
}
