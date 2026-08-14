using UnityEngine;

/// <summary>
/// 枪手长按+上发射的追踪导弹：生成时索敌一次（只锁开火点上方的敌人），先追踪目标位置，再沿最后方向直线飞行。
/// 撞敌或 Ground 爆炸；超时直接销毁。
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(CircleCollider2D))]
public class PlayerHomingMissile : MonoBehaviour
{
    const string AirEnemyTag = "AirEnemy";
    const string EnemyTag = "Enemy";

    [SerializeField] float speed = 10f;
    [SerializeField] float homingDuration = 1.5f;
    [SerializeField] float lifetime = 3f;
    [SerializeField] float detectRange = 20f;
    [SerializeField] float yMismatchThreshold = 0.75f;
    [SerializeField] GrenadeExplosion explosionPrefab;

    Rigidbody2D rb;
    CircleCollider2D missileCollider;
    Transform target;
    Vector2 lastTargetPos;
    Vector2 flyDirection = Vector2.up;
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

    public void Init(Collider2D playerCollider)
    {
        if (playerCollider != null && missileCollider != null)
            Physics2D.IgnoreCollision(missileCollider, playerCollider);

        spawnTime = Time.time;
        AcquireTarget();

        if (target != null)
        {
            lastTargetPos = target.position;
            flyDirection = DirectionTo(lastTargetPos);
        }
        else
        {
            flyDirection = Vector2.up;
        }

        ApplyVelocity();
        Invoke(nameof(Despawn), lifetime);
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

        ApplyVelocity();
    }

    void UpdateHoming()
    {
        if (!IsTargetValid())
        {
            flyDirection = DirectionTo(lastTargetPos);
            return;
        }

        lastTargetPos = target.position;
        flyDirection = DirectionTo(lastTargetPos);
    }

    void ApplyVelocity()
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

        if (other.CompareTag("Player"))
            return;

        if (IsGround(other) || IsEnemyCollider(other))
            Explode();
    }

    static bool IsGround(Collider2D collider) =>
        LayerMask.LayerToName(collider.gameObject.layer) == "Ground";

    static bool IsEnemyCollider(Collider2D collider)
    {
        if (collider.CompareTag(EnemyTag) || collider.CompareTag(AirEnemyTag))
            return true;

        var character = collider.GetComponentInParent<Character>();
        return character != null
            && (character.CompareTag(EnemyTag) || character.CompareTag(AirEnemyTag));
    }

    void AcquireTarget()
    {
        Vector2 origin = transform.position;
        float rangeSq = detectRange * detectRange;
        float firePointY = origin.y;

        bool IsAboveFirePoint(Transform t) =>
            t.position.y - firePointY > yMismatchThreshold;

        target = FindClosest(AirEnemyTag, origin, rangeSq, IsAboveFirePoint);
        if (target != null)
        {
            LockOn(target);
            return;
        }

        target = FindClosest(EnemyTag, origin, rangeSq, IsAboveFirePoint);
        if (target != null)
            LockOn(target);
    }

    void LockOn(Transform acquired)
    {
        target = acquired;
        lastTargetPos = acquired.position;
        lockedOn = true;
    }

    static Transform FindClosest(
        string tag,
        Vector2 origin,
        float rangeSq,
        System.Predicate<Transform> extraFilter)
    {
        GameObject[] candidates = GameObject.FindGameObjectsWithTag(tag);
        Transform closest = null;
        float closestSq = rangeSq;

        for (int i = 0; i < candidates.Length; i++)
        {
            GameObject go = candidates[i];
            if (go == null || !go.activeInHierarchy)
                continue;

            if (!IsAliveEnemy(go))
                continue;

            Transform t = go.transform;
            if (extraFilter != null && !extraFilter(t))
                continue;

            float sq = ((Vector2)t.position - origin).sqrMagnitude;
            if (sq > closestSq)
                continue;

            closestSq = sq;
            closest = t;
        }

        return closest;
    }

    static bool IsAliveEnemy(GameObject go)
    {
        var enemy = go.GetComponent<Enemy>();
        if (enemy == null)
            enemy = go.GetComponentInParent<Enemy>();

        return enemy == null || !enemy.isDead;
    }

    bool IsTargetValid()
    {
        if (target == null)
            return false;

        if (!target.gameObject.activeInHierarchy)
            return false;

        return IsAliveEnemy(target.gameObject);
    }

    void Explode()
    {
        if (hasExploded)
            return;

        hasExploded = true;
        CancelInvoke(nameof(Despawn));

        if (explosionPrefab != null)
            Instantiate(explosionPrefab, transform.position, Quaternion.identity);

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
