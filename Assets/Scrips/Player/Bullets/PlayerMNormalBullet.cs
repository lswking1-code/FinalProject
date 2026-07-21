using UnityEngine;

/// <summary>
/// 机械师普通子弹：飞行与伤害逻辑同 PlayerProjectile，
/// 命中敌人时为发射者恢复 AbilityPower。
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(Attack))]
public class PlayerMNormalBullet : MonoBehaviour, IPlayerAmmo
{
    [SerializeField] float speed = 8f;
    [SerializeField] int damage = 10;
    [SerializeField] float lifetime = 5f;
    [SerializeField] float abilityPowerRestore = 5f;

    Rigidbody2D rb;
    Attack attack;
    Vector2 direction;
    Character owner;
    bool hasRestored;

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

    public void Init(FireDir dir, float faceY, Character owner)
    {
        this.owner = owner;
        transform.rotation = PlayerProjectile.GetRotation(dir, faceY);
        direction = transform.right;
        rb.linearVelocity = direction * speed;
    }

    void FixedUpdate()
    {
        rb.linearVelocity = direction * speed;
    }

    void OnTriggerEnter2D(Collider2D collision)
    {
        if (hasRestored || owner == null)
            return;
        if (collision.CompareTag("Player"))
            return;
        if (collision.GetComponentInParent<Enemy>() == null)
            return;

        hasRestored = true;
        owner.RestoreAbilityPower(abilityPowerRestore);
    }
}
