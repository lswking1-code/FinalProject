using UnityEngine;

/// <summary>
/// 手雷远程敌人：AI 循环与 RangedEnemy 一致，Shot 时向前投掷一枚手雷。
/// </summary>
public class GrenadeEnemy : RangedEnemy
{
    [Header("手雷")]
    public EnemyGrenade grenadePrefab;
    public Transform throwPoint;

    protected override void Awake()
    {
        base.Awake();
        shotState = new GrenadeThrowState();
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
        grenade.Init(dir, throwerVelocity, throwerCollider);
        FacePlayer();
    }
}
