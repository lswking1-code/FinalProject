using UnityEngine;

/// <summary>
/// 火箭兵进阶索敌导弹：生成后立刻锁定玩家追踪；命中玩家或地面后爆炸。
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(CircleCollider2D))]
public class EnemyRocketHomingMissile : MonoBehaviour, IHitCountable, IEnemyProjectileCancelable
{
    [SerializeField] float speed = 5.5f;
    [SerializeField] float homingDuration = 0.7f;
    [SerializeField] float lifetime = 5f;
    [SerializeField] EnemyGrenadeExplosion explosionPrefab;

    Rigidbody2D rb;
    CircleCollider2D missileCollider;
    Transform target;
    Collider2D targetBody;
    Vector2 lastTargetPos;
    Vector2 flyDirection = Vector2.right;
    float spawnTime;
    bool lockedOn;
    bool homingEnded;
    bool hasExploded;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        missileCollider = GetComponent<CircleCollider2D>();
        rb.gravityScale = 0f;
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        rb.constraints = RigidbodyConstraints2D.FreezeRotation;
    }

    public void Init(Collider2D throwerCollider, Transform playerTarget)
    {
        IgnoreThrower(throwerCollider);

        spawnTime = Time.time;
        SetTarget(playerTarget);
        lastTargetPos = GetTargetAimPoint();
        LockOnPlayer();
        ApplyVelocityAndRotation();
        Invoke(nameof(Despawn), lifetime);
    }

    void IgnoreThrower(Collider2D throwerCollider)
    {
        if (throwerCollider == null || missileCollider == null)
            return;

        Physics2D.IgnoreCollision(missileCollider, throwerCollider);

        var thrower = throwerCollider.GetComponentInParent<Enemy>();
        if (thrower == null)
            return;

        var throwerColliders = thrower.GetComponentsInChildren<Collider2D>(true);
        for (int i = 0; i < throwerColliders.Length; i++)
        {
            if (throwerColliders[i] != null && throwerColliders[i] != throwerCollider)
                Physics2D.IgnoreCollision(missileCollider, throwerColliders[i]);
        }
    }

    void FixedUpdate()
    {
        if (hasExploded)
            return;

        float elapsed = Time.time - spawnTime;
        if (!homingEnded && elapsed >= homingDuration)
            homingEnded = true;

        if (!homingEnded && lockedOn)
            UpdateHoming();

        ApplyVelocityAndRotation();
    }

    void LockOnPlayer()
    {
        if (IsTargetValid())
        {
            lockedOn = true;
            lastTargetPos = GetTargetAimPoint();
            flyDirection = DirectionTo(lastTargetPos);
            return;
        }

        SetTarget(FindPlayer());
        if (IsTargetValid())
        {
            lockedOn = true;
            lastTargetPos = GetTargetAimPoint();
            flyDirection = DirectionTo(lastTargetPos);
        }
    }

    void UpdateHoming()
    {
        if (!IsTargetValid())
        {
            flyDirection = DirectionTo(lastTargetPos);
            return;
        }

        lastTargetPos = GetTargetAimPoint();
        flyDirection = DirectionTo(lastTargetPos);
    }

    void ApplyVelocityAndRotation()
    {
        if (flyDirection.sqrMagnitude < 0.0001f)
            flyDirection = Vector2.right;

        rb.linearVelocity = flyDirection * speed;
        float angle = Mathf.Atan2(flyDirection.y, flyDirection.x) * Mathf.Rad2Deg;
        transform.rotation = Quaternion.Euler(0f, 0f, angle);
    }

    Vector2 DirectionTo(Vector2 point)
    {
        Vector2 delta = point - (Vector2)transform.position;
        if (delta.sqrMagnitude < 0.0001f)
            return flyDirection.sqrMagnitude > 0.0001f ? flyDirection : Vector2.right;

        return delta.normalized;
    }

    void SetTarget(Transform playerTarget)
    {
        target = playerTarget;
        targetBody = null;
        if (target == null)
            return;

        targetBody = target.GetComponent<CapsuleCollider2D>();
        if (targetBody == null)
            targetBody = target.GetComponent<Collider2D>();
    }

    Vector2 GetTargetAimPoint()
    {
        if (target == null)
            return (Vector2)transform.position + Vector2.right;

        if (targetBody != null)
            return targetBody.bounds.center;

        return target.position;
    }

    Transform FindPlayer()
    {
        GameObject playerGo = GameObject.FindGameObjectWithTag("Player");
        return playerGo != null ? playerGo.transform : null;
    }

    bool IsTargetValid()
    {
        return target != null && target.gameObject.activeInHierarchy;
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

        if (EncounterZone.IsAirWallCollider(other))
        {
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

        if (IsPlayerCollider(other) || Attack.IsProjectileBlockingCollider(other))
            Explode();
    }

    public bool RegisterHit(Attack attacker)
    {
        return TryCancelByMelee(attacker);
    }

    public bool TryCancelByMelee(Attack attacker)
    {
        if (hasExploded || attacker == null)
            return false;

        if (!string.IsNullOrEmpty(attacker.requireTag) && !CompareTag(attacker.requireTag))
            return false;

        if (attacker.ignoreTag == "Enemy")
            return false;

        CancelInvoke(nameof(Despawn));
        hasExploded = true;
        Destroy(gameObject);
        return true;
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
            explosion.gameObject.name = "EnemyRocketHomingMissileExplosion";
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
}
