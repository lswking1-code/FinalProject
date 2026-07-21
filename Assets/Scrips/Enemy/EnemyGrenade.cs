using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(CircleCollider2D))]
[RequireComponent(typeof(Animator))]
public class EnemyGrenade : MonoBehaviour
{
    const string RollingStateName = "GrenadeRolling";

    [Tooltip("未由投掷者覆盖时的默认抛射角（度，相对水平向上）")]
    [SerializeField] float defaultThrowAngle = 35.5f;
    [Tooltip("未由投掷者覆盖时的默认抛射速度")]
    [SerializeField] float defaultThrowSpeed = 8.6f;
    [SerializeField] EnemyGrenadeExplosion explosionPrefab;
    [SerializeField, Range(0f, 1f)] float throwerHorizontalInherit = 0.5f;
    [SerializeField, Range(0f, 1f)] float throwerVerticalInherit = 0f;
    [SerializeField] float rollSpeedReference = 12f;
    [SerializeField] float minRollAnimSpeed = 0.6f;
    [SerializeField] float maxRollAnimSpeed = 1.8f;
    [Tooltip("命中该层时引爆（地面）")]
    [SerializeField] LayerMask groundLayer;
    [SerializeField] float groundSnapRayDistance = 1.5f;

    Rigidbody2D rb;
    CircleCollider2D grenadeCollider;
    Animator animator;
    bool hasExploded;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        grenadeCollider = GetComponent<CircleCollider2D>();
        animator = GetComponent<Animator>();
        rb.constraints = RigidbodyConstraints2D.FreezeRotation;
    }

    public void Init(float faceDir, Vector2 throwerVelocity, Collider2D throwerCollider)
    {
        Init(faceDir, throwerVelocity, throwerCollider, defaultThrowAngle, defaultThrowSpeed);
    }

    public void Init(
        float faceDir,
        Vector2 throwerVelocity,
        Collider2D throwerCollider,
        float throwAngleDegrees,
        float throwSpeed)
    {
        float dir = Mathf.Sign(faceDir);
        if (dir == 0f)
            dir = 1f;

        float rad = throwAngleDegrees * Mathf.Deg2Rad;
        float speed = Mathf.Max(0f, throwSpeed);
        var throwVelocity = new Vector2(dir * speed * Mathf.Cos(rad), speed * Mathf.Sin(rad));

        rb.gravityScale = 1f;
        var inherited = new Vector2(
            throwerVelocity.x * throwerHorizontalInherit,
            throwerVelocity.y * throwerVerticalInherit);
        rb.linearVelocity = inherited + throwVelocity;

        if (throwerCollider != null && grenadeCollider != null)
            Physics2D.IgnoreCollision(grenadeCollider, throwerCollider);

        if (animator != null)
            animator.Play(RollingStateName, 0, 0f);

        SyncRollAnimSpeed();
    }

    void FixedUpdate()
    {
        if (!hasExploded)
            SyncRollAnimSpeed();
    }

    void SyncRollAnimSpeed()
    {
        if (animator == null)
            return;

        float t = rollSpeedReference > 0f
            ? Mathf.Clamp01(Mathf.Abs(rb.linearVelocity.x) / rollSpeedReference)
            : 1f;
        animator.speed = Mathf.Lerp(minRollAnimSpeed, maxRollAnimSpeed, t);
    }

    void OnCollisionEnter2D(Collision2D collision)
    {
        if (hasExploded)
            return;

        if (IsPlayerCollider(collision.collider))
        {
            Explode();
            return;
        }

        if (IsGroundCollider(collision.collider))
            Despawn();
    }

    static bool IsPlayerCollider(Collider2D collider)
    {
        if (collider.CompareTag("Player"))
            return true;

        var character = collider.GetComponentInParent<Character>();
        return character != null && character.CompareTag("Player");
    }

    bool IsGroundCollider(Collider2D collider)
    {
        if (groundLayer.value == 0 || collider == null)
            return false;

        return (groundLayer.value & (1 << collider.gameObject.layer)) != 0;
    }

    void Despawn()
    {
        if (hasExploded)
            return;

        hasExploded = true;
        Destroy(gameObject);
    }

    void Explode()
    {
        if (hasExploded)
            return;

        hasExploded = true;

        if (explosionPrefab != null)
            Instantiate(explosionPrefab, GetExplosionPosition(), Quaternion.identity);

        Destroy(gameObject);
    }

    Vector3 GetExplosionPosition()
    {
        float x = transform.position.x;
        float z = transform.position.z;
        float probeY = grenadeCollider != null
            ? grenadeCollider.bounds.max.y
            : transform.position.y + 0.1f;

        if (groundLayer.value != 0)
        {
            var hit = Physics2D.Raycast(new Vector2(x, probeY), Vector2.down, groundSnapRayDistance, groundLayer);
            if (hit.collider != null)
                return new Vector3(x, hit.point.y, z);
        }

        if (grenadeCollider != null)
            return new Vector3(x, grenadeCollider.bounds.min.y, z);

        return transform.position;
    }
}
