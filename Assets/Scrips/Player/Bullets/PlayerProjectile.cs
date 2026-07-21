using UnityEngine;

public enum FireDir
{
    Forward,
    Crouch,
    Up,
    Down,
}

/// <summary>
/// 玩家发射的子弹，沿指定方向飞行并对敌人造成伤害。
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(Attack))]
public class PlayerProjectile : MonoBehaviour
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
        attack.attackType = AttackType.Projectile;
        attack.ignoreTag = "Player";
    }

    void Start()
    {
        Destroy(gameObject, lifetime);
    }

    public static Quaternion GetRotation(FireDir dir, float faceY) => dir switch
    {
        FireDir.Up => Quaternion.Euler(0, faceY, 90),
        FireDir.Down => Quaternion.Euler(0, faceY, -90),
        _ => Quaternion.Euler(0, faceY, 0),
    };

    public void Init(FireDir dir, float faceY)
    {
        transform.rotation = GetRotation(dir, faceY);
        direction = transform.right;
        rb.linearVelocity = direction * speed;
    }

    void FixedUpdate()
    {
        rb.linearVelocity = direction * speed;
    }
}
