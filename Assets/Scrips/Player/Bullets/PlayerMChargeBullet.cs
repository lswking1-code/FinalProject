using UnityEngine;

/// <summary>
/// 机械师蓄力子弹：飞行与伤害逻辑同普通子弹，
/// 命中敌人或达到最大飞行距离后在自身位置生成 TaggetArea。
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(Attack))]
public class PlayerMChargeBullet : MonoBehaviour, IPlayerAmmo
{
    [SerializeField] float speed = 8f;
    [SerializeField] int damage = 10;
    [SerializeField] float maxDistance = 10f;
    [SerializeField] GameObject taggetAreaPrefab;

    Rigidbody2D rb;
    Attack attack;
    Vector2 direction;
    Vector2 startPosition;
    bool hasDetonated;

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
        startPosition = transform.position;
    }

    public void Init(FireDir dir, float faceY, Character owner = null)
    {
        transform.rotation = PlayerProjectile.GetRotation(dir, faceY);
        direction = transform.right;
        startPosition = transform.position;
        rb.linearVelocity = direction * speed;
    }

    void FixedUpdate()
    {
        if (hasDetonated)
            return;

        rb.linearVelocity = direction * speed;

        if (Vector2.Distance(startPosition, transform.position) >= maxDistance)
            Detonate();
    }

    void OnDestroy()
    {
        // Attack 命中敌人后会 Destroy 本物体，在此生成区域
        if (!hasDetonated && Application.isPlaying)
            SpawnTaggetArea();
    }

    void Detonate()
    {
        if (hasDetonated)
            return;

        SpawnTaggetArea();
        Destroy(gameObject);
    }

    void SpawnTaggetArea()
    {
        hasDetonated = true;
        if (taggetAreaPrefab != null)
            Instantiate(taggetAreaPrefab, transform.position, Quaternion.identity);
    }
}
