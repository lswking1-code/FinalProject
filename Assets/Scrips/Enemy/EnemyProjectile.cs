using UnityEngine;

/// <summary>
/// 敌人发射的子弹，沿水平方向飞行并对玩家造成伤害。
/// 命中遭遇战空气墙时销毁（区外无法穿墙伤人）。
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(Attack))]
public class EnemyProjectile : MonoBehaviour
{
    [SerializeField] float speed = 8f;
    [SerializeField] int damage = 10;
    [SerializeField] float lifetime = 5f;

    Rigidbody2D rb;
    Attack attack;
    Vector2 direction;
    bool destroyed;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        attack = GetComponent<Attack>();
        attack.damage = damage;
        attack.attackType = AttackType.Projectile;
        attack.requireTag = "Player";

        // 薄空气墙用连续检测，减少高速子弹漏检
        if (rb != null)
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
    }

    void Start()
    {
        Destroy(gameObject, lifetime);
    }

    public void Init(Vector2 flyDirection)
    {
        direction = flyDirection.normalized;
        if (direction == Vector2.zero)
            direction = Vector2.right;

        rb.linearVelocity = direction * speed;
    }

    void FixedUpdate()
    {
        if (destroyed || rb == null)
            return;
        rb.linearVelocity = direction * speed;
    }

    void OnTriggerEnter2D(Collider2D other) => TryDestroyOnAirWall(other);

    void OnCollisionEnter2D(Collision2D collision)
    {
        if (collision != null)
            TryDestroyOnAirWall(collision.collider);
    }

    void TryDestroyOnAirWall(Collider2D other)
    {
        if (destroyed || other == null)
            return;
        if (!EncounterZone.IsAirWallCollider(other))
            return;

        destroyed = true;
        Destroy(gameObject);
    }
}
