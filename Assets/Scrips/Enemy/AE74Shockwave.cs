using UnityEngine;

/// <summary>
/// AE-74 践踏冲击波：沿水平短距低高度移动，可被跳过。
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(Attack))]
public class AE74Shockwave : MonoBehaviour, IEnemyProjectileCancelable
{
    [SerializeField] float speed = 6f;
    [SerializeField] float lifetime = 0.45f;
    [SerializeField] int damage = 12;

    [Header("落地冲击波美术")]
    [SerializeField] SpriteRenderer artwork;
    [SerializeField] Sprite[] animationFrames;
    float visualElapsed;
    bool initialized;

    Rigidbody2D rb;
    Attack attack;
    Collider2D body;
    Vector2 direction = Vector2.right;
    bool destroyed;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        attack = GetComponent<Attack>();
        body = GetComponent<Collider2D>();

        rb.gravityScale = 0f;
        rb.constraints = RigidbodyConstraints2D.FreezeRotation;
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

        attack.damage = damage;
        attack.attackType = AttackType.Melee;
        attack.requireTag = "Player";
    }

    public void Init(Vector2 flyDirection, float moveSpeed, float life, Collider2D thrower)
    {
        visualElapsed = 0f;
        initialized = true;
        ApplyAnimationFrame(0);
        direction = flyDirection.sqrMagnitude > 0.0001f ? flyDirection.normalized : Vector2.right;
        if (moveSpeed > 0f)
            speed = moveSpeed;
        if (life > 0f)
            lifetime = life;

        IgnoreThrower(thrower);
        rb.linearVelocity = direction * speed;
        float sign = Mathf.Sign(direction.x);
        if (!Mathf.Approximately(sign, 0f))
        {
            Vector3 scale = transform.localScale;
            scale.x = Mathf.Abs(scale.x) * sign;
            transform.localScale = scale;
        }

        Destroy(gameObject, lifetime);
    }

    void Update()
    {
        if (!initialized || destroyed || animationFrames == null || animationFrames.Length == 0)
            return;

        visualElapsed += Time.deltaTime;
        int frame = Mathf.Min(animationFrames.Length - 1,
            Mathf.FloorToInt(visualElapsed / Mathf.Max(0.01f, lifetime) * animationFrames.Length));
        ApplyAnimationFrame(frame);
    }

    void ApplyAnimationFrame(int frame)
    {
        if (artwork != null && animationFrames != null && frame < animationFrames.Length)
            artwork.sprite = animationFrames[frame];
    }

    void FixedUpdate()
    {
        if (destroyed || rb == null)
            return;

        rb.linearVelocity = direction * speed;
    }

    public bool TryCancelByMelee(Attack attacker)
    {
        if (destroyed || attacker == null)
            return false;

        Despawn();
        return true;
    }

    void IgnoreThrower(Collider2D thrower)
    {
        if (body == null || thrower == null)
            return;

        Physics2D.IgnoreCollision(body, thrower);
        var enemy = thrower.GetComponentInParent<Enemy>();
        if (enemy == null)
            return;

        var cols = enemy.GetComponentsInChildren<Collider2D>(true);
        for (int i = 0; i < cols.Length; i++)
        {
            if (cols[i] != null && cols[i] != thrower)
                Physics2D.IgnoreCollision(body, cols[i], true);
        }
    }

    void Despawn()
    {
        if (destroyed)
            return;

        destroyed = true;
        Destroy(gameObject);
    }
}
