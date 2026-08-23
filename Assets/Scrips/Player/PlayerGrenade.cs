using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(CircleCollider2D))]
[RequireComponent(typeof(Animator))]
public class PlayerGrenade : MonoBehaviour
{
    const string RollingStateName = "GrenadeRolling";

    [SerializeField] float horizontalSpeed = 6.5f;
    [SerializeField] float verticalSpeed = 6.5f;
    [Tooltip("投掷后的重力倍率，越大下落越干脆")]
    [SerializeField] float gravityScale = 1.6f;
    [Tooltip("生成时沿面向施加的冲量；0 表示不加力")]
    [SerializeField] float forwardImpulse = 0f;
    [Tooltip(">=0 时覆盖碰撞体摩擦；旋转锁定时摩擦过大会几乎不滚，宜偏低")]
    [SerializeField] float rollFriction = 0.04f;
    [Tooltip("落地材质弹力，略大于 0 更像弹跳滚动")]
    [SerializeField, Range(0f, 1f)] float rollBounciness = 0.18f;
    [Tooltip("首次落地时保留的水平速度比例")]
    [SerializeField, Range(0f, 1f)] float landHorizontalRetain = 0.92f;
    [Tooltip("首次落地时保留的向上反弹比例（抑制轻飘回弹）")]
    [SerializeField, Range(0f, 1f)] float landBounceRetain = 0.25f;
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
    bool hasLanded;

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
            friction = Mathf.Max(0f, rollFriction),
            bounciness = Mathf.Clamp01(rollBounciness),
            // 取较小摩擦，避免地面材质把滚动直接刹死
            frictionCombine = PhysicsMaterialCombine2D.Minimum,
            bounceCombine = PhysicsMaterialCombine2D.Maximum
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

        rb.gravityScale = Mathf.Max(0.01f, gravityScale);
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

        if (IsEnemyCollider(collision.collider))
        {
            Explode();
            return;
        }

        TryApplyLandingFeel(collision);
    }

    void TryApplyLandingFeel(Collision2D collision)
    {
        if (hasLanded || groundLayer.value == 0)
            return;

        int layerBit = 1 << collision.collider.gameObject.layer;
        if ((groundLayer.value & layerBit) == 0)
            return;

        bool landedOnTop = false;
        for (int i = 0; i < collision.contactCount; i++)
        {
            if (collision.GetContact(i).normal.y > 0.5f)
            {
                landedOnTop = true;
                break;
            }
        }

        if (!landedOnTop)
            return;

        hasLanded = true;
        var velocity = rb.linearVelocity;
        velocity.x *= landHorizontalRetain;
        if (velocity.y > 0f)
            velocity.y *= landBounceRetain;
        rb.linearVelocity = velocity;
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
