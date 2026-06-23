using UnityEngine;

/// <summary>
/// 敌人发射的子弹，沿水平方向飞行并对玩家造成伤害。
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

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        attack = GetComponent<Attack>();
        attack.damage = damage;
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
        rb.linearVelocity = direction * speed;
    }

    void OnTriggerEnter2D(Collider2D collision)
    {
        if (!collision.CompareTag("Player"))
            return;

        var character = collision.GetComponent<Character>();
        if (character != null)
            character.TakeDamage(attack);

        Destroy(gameObject);
    }
}
