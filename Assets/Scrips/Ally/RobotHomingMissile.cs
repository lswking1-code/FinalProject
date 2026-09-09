using UnityEngine;

/// <summary>
/// 机器人加速模式连携导弹：先带扩散角上升，再追踪指定敌人，最后沿末方向直线飞行。
/// 命中敌人或阻挡物后以玩家爆炸结算；忽略玩家与机器人。
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(CircleCollider2D))]
public class RobotHomingMissile : MonoBehaviour
{
    const string AirEnemyTag = "AirEnemy";
    const string EnemyTag = "Enemy";

    [SerializeField] float speed = 8f;
    [SerializeField] float ascentDuration = 0.45f;
    [SerializeField] float homingDuration = 1.5f;
    [SerializeField] float lifetime = 5f;
    [SerializeField] float ascentSpreadAngle = 12f;
    [Tooltip("追踪阶段每秒最大转角（度）。升空结束后按此速率弧线对准目标")]
    [SerializeField] float maxTurnRate = 270f;
    [SerializeField] GrenadeExplosion explosionPrefab;

    Rigidbody2D rb;
    CircleCollider2D missileCollider;
    Transform target;
    Collider2D targetBody;
    Vector2 lastTargetPos;
    Vector2 flyDirection = Vector2.up;
    float spawnTime;
    bool lockedOn;
    bool homingEnded;
    bool hasExploded;
    bool ascentEnded;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        missileCollider = GetComponent<CircleCollider2D>();
        rb.gravityScale = 0f;
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        rb.constraints = RigidbodyConstraints2D.FreezeRotation;
    }

    public void Init(Collider2D throwerCollider, Transform enemyTarget, Collider2D playerCollider = null)
    {
        IgnoreFriendly(throwerCollider);
        IgnoreFriendly(playerCollider);

        var robot = throwerCollider != null
            ? throwerCollider.GetComponentInParent<AllyRobot>()
            : null;
        if (robot != null)
            IgnoreColliders(robot.GetComponentsInChildren<Collider2D>(true));

        if (playerCollider != null)
        {
            var player = playerCollider.GetComponentInParent<Character>();
            if (player != null)
                IgnoreColliders(player.GetComponentsInChildren<Collider2D>(true));
        }

        spawnTime = Time.time;
        SetTarget(enemyTarget);
        lastTargetPos = GetTargetAimPoint();

        float spread = Random.Range(-ascentSpreadAngle, ascentSpreadAngle);
        flyDirection = Quaternion.Euler(0f, 0f, spread) * Vector2.up;

        ApplyVelocityAndRotation();
        Invoke(nameof(Despawn), lifetime);
    }

    void IgnoreFriendly(Collider2D other)
    {
        if (other == null || missileCollider == null)
            return;

        Physics2D.IgnoreCollision(missileCollider, other);
    }

    void IgnoreColliders(Collider2D[] colliders)
    {
        if (missileCollider == null || colliders == null)
            return;

        for (int i = 0; i < colliders.Length; i++)
        {
            if (colliders[i] != null && colliders[i] != missileCollider)
                Physics2D.IgnoreCollision(missileCollider, colliders[i]);
        }
    }

    void FixedUpdate()
    {
        if (hasExploded)
            return;

        float elapsed = Time.time - spawnTime;
        if (!ascentEnded && elapsed >= ascentDuration)
        {
            ascentEnded = true;
            LockOnTarget();
        }

        if (ascentEnded && !homingEnded && elapsed >= ascentDuration + homingDuration)
            homingEnded = true;

        if (ascentEnded && !homingEnded && lockedOn)
            UpdateHoming();

        ApplyVelocityAndRotation();
    }

    void LockOnTarget()
    {
        if (!IsTargetValid())
            return;

        lockedOn = true;
        lastTargetPos = GetTargetAimPoint();
    }

    void UpdateHoming()
    {
        if (IsTargetValid())
            lastTargetPos = GetTargetAimPoint();

        SteerTowards(DirectionTo(lastTargetPos));
    }

    void SteerTowards(Vector2 desired)
    {
        if (desired.sqrMagnitude < 0.0001f)
            return;

        float maxRadians = Mathf.Max(0f, maxTurnRate) * Mathf.Deg2Rad * Time.fixedDeltaTime;
        flyDirection = Vector3.RotateTowards(flyDirection, desired.normalized, maxRadians, 0f);
    }

    void ApplyVelocityAndRotation()
    {
        if (flyDirection.sqrMagnitude < 0.0001f)
            flyDirection = Vector2.up;

        rb.linearVelocity = flyDirection * speed;
        float angle = Mathf.Atan2(flyDirection.y, flyDirection.x) * Mathf.Rad2Deg;
        transform.rotation = Quaternion.Euler(0f, 0f, angle);
    }

    Vector2 DirectionTo(Vector2 point)
    {
        Vector2 delta = point - (Vector2)transform.position;
        if (delta.sqrMagnitude < 0.0001f)
            return flyDirection.sqrMagnitude > 0.0001f ? flyDirection : Vector2.up;

        return delta.normalized;
    }

    void SetTarget(Transform enemyTarget)
    {
        target = enemyTarget;
        targetBody = null;
        if (target == null)
            return;

        targetBody = target.GetComponent<Collider2D>()
            ?? target.GetComponentInChildren<Collider2D>();
    }

    Vector2 GetTargetAimPoint()
    {
        if (target == null)
            return (Vector2)transform.position + Vector2.up;

        if (targetBody != null)
            return targetBody.bounds.center;

        return target.position;
    }

    bool IsTargetValid()
    {
        if (target == null || !target.gameObject.activeInHierarchy)
            return false;

        var enemy = target.GetComponent<Enemy>();
        if (enemy == null)
            enemy = target.GetComponentInParent<Enemy>();

        return enemy == null || enemy.IsHittable;
    }

    void OnTriggerEnter2D(Collider2D other) => TryExplodeFromHit(other);

    void OnCollisionEnter2D(Collision2D collision)
    {
        if (collision != null)
            TryExplodeFromHit(collision.collider);
    }

    void TryExplodeFromHit(Collider2D other)
    {
        if (hasExploded || other == null)
            return;

        if (MeleeDetectZone.IsSensorCollider(other))
            return;

        if (IsRobotTopCollider(other) || IsFriendlyCollider(other))
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

        if (IsEnemyCollider(other))
        {
            if (!ascentEnded)
                return;
            Explode();
            return;
        }

        if (Attack.IsProjectileBlockingCollider(other))
            Explode();
    }

    static bool IsFriendlyCollider(Collider2D collider)
    {
        if (collider == null)
            return false;

        if (collider.CompareTag("Player") || collider.GetComponentInParent<AllyRobot>() != null)
            return true;

        var character = collider.GetComponentInParent<Character>();
        return character != null && character.CompareTag("Player");
    }

    static bool IsEnemyCollider(Collider2D collider)
    {
        if (collider.CompareTag(EnemyTag) || collider.CompareTag(AirEnemyTag))
            return true;

        var character = collider.GetComponentInParent<Character>();
        return character != null
            && (character.CompareTag(EnemyTag) || character.CompareTag(AirEnemyTag));
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
            Vector3 explodePos = missileCollider != null
                ? (Vector3)missileCollider.bounds.center
                : transform.position;
            Instantiate(explosionPrefab, explodePos, Quaternion.identity);
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
