using UnityEngine;

/// <summary>
/// 机械师 L 蓄力弹：飞行与伤害逻辑同 PlayerMNormalBullet，
/// 命中敌人时回 AbilityPower、标记该敌人，并在敌人身上附着标记炸弹。
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(Attack))]
public class PlayerMLChargeBullet : MonoBehaviour, IPlayerAmmo
{
    [SerializeField] float speed = 8f;
    [SerializeField] int damage = 10;
    [SerializeField] float lifetime = 5f;
    [SerializeField] float abilityPowerRestore = 5f;
    [SerializeField] GameObject markBombPrefab;
    [SerializeField] Vector3 bombLocalOffset = Vector3.zero;

    Rigidbody2D rb;
    Attack attack;
    Vector2 direction;
    Character owner;
    bool hasAppliedHitEffects;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        attack = GetComponent<Attack>();
        attack.damage = damage;
        attack.attackType = AttackType.Projectile;
        attack.ignoreTag = "Player";
        // L 蓄力弹：不带击退
        attack.enableKnockback = false;
        attack.knockbackForce = 0f;
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
        if (hasAppliedHitEffects)
            return;
        if (collision.CompareTag("Player"))
            return;

        Enemy enemy = collision.GetComponentInParent<Enemy>();
        if (enemy == null || enemy.isDead)
            return;

        hasAppliedHitEffects = true;

        if (owner != null)
            owner.RestoreAbilityPower(abilityPowerRestore);

        ApplyMarkAndBomb(enemy);
    }

    void ApplyMarkAndBomb(Enemy enemy)
    {
        enemy.isMarked = true;

        EnemyMarkBomb existing = enemy.GetComponentInChildren<EnemyMarkBomb>();
        if (existing != null)
            Destroy(existing.gameObject);

        if (markBombPrefab != null)
        {
            GameObject bombGo = Instantiate(markBombPrefab, enemy.transform);
            bombGo.transform.localPosition = bombLocalOffset;
            bombGo.transform.localRotation = Quaternion.identity;

            EnemyMarkBomb bomb = bombGo.GetComponent<EnemyMarkBomb>();
            if (bomb != null)
                bomb.Init(enemy);
        }

        AllyRobot[] robots = FindObjectsByType<AllyRobot>(FindObjectsSortMode.None);
        foreach (var robot in robots)
        {
            if (robot != null)
                robot.RequestRetarget();
        }
    }
}
