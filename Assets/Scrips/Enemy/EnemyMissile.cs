using UnityEngine;

/// <summary>
/// 敌人直线导弹：沿指定方向飞行，命中玩家或地面后生成爆炸。
/// 可被玩家子弹击毁（引爆）；近战抵销则直接销毁。超时/空气墙不爆炸。
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(CircleCollider2D))]
public class EnemyMissile : MonoBehaviour, IEnemyProjectileCancelable
{
    [SerializeField] float speed = 8f;
    [SerializeField] float lifetime = 5f;
    [SerializeField] EnemyGrenadeExplosion explosionPrefab;

    Rigidbody2D rb;
    CircleCollider2D missileCollider;
    Vector2 direction = Vector2.right;
    bool hasExploded;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        missileCollider = GetComponent<CircleCollider2D>();
        rb.gravityScale = 0f;
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        rb.constraints = RigidbodyConstraints2D.FreezeRotation;
    }

    public void Init(Vector2 flyDirection, Collider2D throwerCollider)
    {
        direction = flyDirection.normalized;
        if (direction == Vector2.zero)
            direction = Vector2.right;

        ApplyVelocityAndRotation();

        if (throwerCollider != null && missileCollider != null)
            Physics2D.IgnoreCollision(missileCollider, throwerCollider);

        var thrower = throwerCollider != null ? throwerCollider.GetComponentInParent<Enemy>() : null;
        if (thrower != null && missileCollider != null)
        {
            var throwerColliders = thrower.GetComponentsInChildren<Collider2D>();
            for (int i = 0; i < throwerColliders.Length; i++)
            {
                if (throwerColliders[i] != null && throwerColliders[i] != throwerCollider)
                    Physics2D.IgnoreCollision(missileCollider, throwerColliders[i]);
            }
        }

        Invoke(nameof(Despawn), lifetime);
    }

    void FixedUpdate()
    {
        if (hasExploded)
            return;

        ApplyVelocityAndRotation();
    }

    void ApplyVelocityAndRotation()
    {
        if (direction.sqrMagnitude < 0.0001f)
            direction = Vector2.right;

        rb.linearVelocity = direction * speed;
        float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
        transform.rotation = Quaternion.Euler(0f, 0f, angle);
    }

    void OnTriggerEnter2D(Collider2D other) => TryResolveHit(other);

    void OnCollisionEnter2D(Collision2D collision)
    {
        if (collision != null)
            TryResolveHit(collision.collider);
    }

    void TryResolveHit(Collider2D other)
    {
        if (hasExploded || other == null)
            return;

        if (MeleeDetectZone.IsSensorCollider(other))
            return;

        if (IsRobotTopCollider(other))
        {
            if (missileCollider != null)
                Physics2D.IgnoreCollision(missileCollider, other, true);
            return;
        }

        if (AirWallRegistry.IsAirWall(other))
        {
            Vector2 velocity = rb != null ? rb.linearVelocity : direction;
            if (AirWallRegistry.IsInbound(other, velocity, transform.position))
            {
                if (missileCollider != null)
                    Physics2D.IgnoreCollision(missileCollider, other, true);
                return;
            }

            Despawn();
            return;
        }

        if (Attack.IsPlayerMeleeHitbox(other, out Attack meleeAttack))
        {
            if (meleeAttack.cancelEnemyProjectiles)
                TryCancelByMelee(meleeAttack);
            return;
        }

        if (IsPlayerAmmo(other))
        {
            Explode();
            TryDestroyPlayerProjectile(other);
            return;
        }

        if (IsPlayerCollider(other) || IsGround(other))
            Explode();
    }

    static bool IsPlayerAmmo(Collider2D collider)
    {
        if (collider == null)
            return false;

        if (collider.GetComponentInParent<IPlayerAmmo>() != null)
            return true;

        var attack = collider.GetComponentInParent<Attack>();
        return attack != null
            && attack.ignoreTag == "Player"
            && (attack.attackType == AttackType.Projectile || collider.GetComponentInParent<PlayerLaserBeam>() != null);
    }

    static void TryDestroyPlayerProjectile(Collider2D collider)
    {
        var attack = collider.GetComponentInParent<Attack>();
        if (attack == null || attack.attackType != AttackType.Projectile)
            return;

        Destroy(attack.gameObject);
    }

    static bool IsPlayerCollider(Collider2D collider)
    {
        if (collider == null || MeleeDetectZone.IsSensorCollider(collider))
            return false;

        if (collider.CompareTag("Player"))
            return true;

        var character = collider.GetComponentInParent<Character>();
        return character != null && character.CompareTag("Player");
    }

    static bool IsGround(Collider2D collider) =>
        LayerMask.LayerToName(collider.gameObject.layer) == "Ground";

    static bool IsRobotTopCollider(Collider2D collider)
    {
        if (collider == null)
            return false;

        if (collider.GetComponent<RobotTopPlatform>() != null)
            return true;

        int robotTopLayer = LayerMask.NameToLayer("RobotTop");
        return robotTopLayer >= 0 && collider.gameObject.layer == robotTopLayer;
    }

    void Explode()
    {
        if (hasExploded)
            return;

        hasExploded = true;
        CancelInvoke(nameof(Despawn));

        if (explosionPrefab != null)
        {
            var explosion = Instantiate(explosionPrefab, transform.position, Quaternion.identity);
            explosion.gameObject.name = "EnemyMissileExplosion";
            EnemySceneCleanup.PlaceInSourceScene(explosion.gameObject, this);
        }

        Destroy(gameObject);
    }

    void Despawn()
    {
        if (hasExploded)
            return;

        hasExploded = true;
        Destroy(gameObject);
    }

    public bool TryCancelByMelee(Attack attacker)
    {
        if (hasExploded || attacker == null)
            return false;

        // 抵销：直接销毁，不引爆
        CancelInvoke(nameof(Despawn));
        hasExploded = true;
        Destroy(gameObject);
        return true;
    }
}
