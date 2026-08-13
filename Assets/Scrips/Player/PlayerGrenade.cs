using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(CircleCollider2D))]
[RequireComponent(typeof(Animator))]
public class PlayerGrenade : MonoBehaviour
{
    const string RollingStateName = "GrenadeRolling";

    [SerializeField] float horizontalSpeed = 7f;
    [SerializeField] float verticalSpeed = 5f;
    [Tooltip("生成时沿面向施加的冲量；0 表示不加力")]
    [SerializeField] float forwardImpulse = 0f;
    [Tooltip(">=0 时覆盖碰撞体摩擦，便于贴地滚动；-1 表示不改")]
    [SerializeField] float rollFriction = -1f;
    [SerializeField] float fuseTime = 2.5f;
    [SerializeField] GrenadeExplosion explosionPrefab;
    [SerializeField, Range(0f, 1f)] float playerHorizontalInherit = 0.5f;
    [SerializeField, Range(0f, 1f)] float playerVerticalInherit = 0f;
    [SerializeField] float rollSpeedReference = 12f;
    [SerializeField] float minRollAnimSpeed = 0.6f;
    [SerializeField] float maxRollAnimSpeed = 1.8f;
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
        ApplyRollFriction();
    }

    void ApplyRollFriction()
    {
        if (rollFriction < 0f)
            return;

        var mat = new PhysicsMaterial2D("GrenadeRollFriction")
        {
            friction = rollFriction,
            bounciness = 0f
        };
        rb.sharedMaterial = mat;
        if (grenadeCollider != null)
            grenadeCollider.sharedMaterial = mat;
    }

    public void Init(float faceDir, Vector2 playerVelocity, Collider2D playerCollider)
    {
        float dir = Mathf.Sign(faceDir);
        if (dir == 0f)
            dir = 1f;

        rb.gravityScale = 1f;
        var inherited = new Vector2(
            playerVelocity.x * playerHorizontalInherit,
            playerVelocity.y * playerVerticalInherit);
        rb.linearVelocity = inherited + new Vector2(dir * horizontalSpeed, verticalSpeed);
        if (forwardImpulse > 0f)
            rb.AddForce(new Vector2(dir * forwardImpulse, 0f), ForceMode2D.Impulse);

        if (playerCollider != null && grenadeCollider != null)
            Physics2D.IgnoreCollision(grenadeCollider, playerCollider);

        if (animator != null)
            animator.Play(RollingStateName, 0, 0f);

        SyncRollAnimSpeed();
        Invoke(nameof(Explode), fuseTime);
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

        if (!IsEnemyCollider(collision.collider))
            return;

        Explode();
    }

    static bool IsEnemyCollider(Collider2D collider)
    {
        if (collider.CompareTag("Enemy"))
            return true;

        var character = collider.GetComponentInParent<Character>();
        return character != null && character.CompareTag("Enemy");
    }

    void Explode()
    {
        if (hasExploded)
            return;

        hasExploded = true;
        CancelInvoke(nameof(Explode));

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
