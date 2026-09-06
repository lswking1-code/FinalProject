using UnityEngine;

/// <summary>
/// 机械师持续伤害子弹：飞行逻辑同普通弹，命中敌人不销毁并按间隔持续伤害；
/// 击中 AllyRobot 时触发机器人贯穿激光并销毁自身。
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(Attack))]
public class PlayerMSustainBullet : MonoBehaviour, IPlayerAmmo
{
    [SerializeField] float speed = 8f;
    [SerializeField] int damage = 10;
    [SerializeField] float lifetime = 5f;
    [SerializeField] float damageInterval = 0.5f;
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
        // Melee：命中不自毁；靠 attackRate 在 Stay 中按间隔重复伤害
        attack.attackType = AttackType.Melee;
        attack.attackRate = damageInterval > 0f ? 1f / damageInterval : 0f;
        attack.ignoreTag = "Player";
        attack.chargesEnergyNode = true;
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
        attack.NotifySpawnInitialized();
    }

    void FixedUpdate()
    {
        rb.linearVelocity = direction * speed;
    }

    void OnTriggerEnter2D(Collider2D collision)
    {
        if (Attack.IsProjectileBlockingCollider(collision))
        {
            attack.ReportImpact(collision, MachinistImpactKind.Surface);
            Destroy(gameObject);
            return;
        }

        var robot = collision.GetComponentInParent<AllyRobot>();
        if (robot != null)
        {
            attack.ReportImpact(collision, MachinistImpactKind.Electric);
            robot.TryFirePierceLaser();
            Destroy(gameObject);
            return;
        }

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
