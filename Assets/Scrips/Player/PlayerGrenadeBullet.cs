using UnityEngine;

/// <summary>
/// 向前飞行的手雷弹：命中敌人、墙壁（Ground）或引信到期后生成 GrenadeExplosion。
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(CircleCollider2D))]
public class PlayerGrenadeBullet : MonoBehaviour
{
    [SerializeField] float speed = 10f;
    [SerializeField] float fuseTime = 1.5f;
    [SerializeField] GrenadeExplosion explosionPrefab;

    Rigidbody2D rb;
    CircleCollider2D bulletCollider;
    Vector2 direction = Vector2.right;
    bool hasExploded;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        bulletCollider = GetComponent<CircleCollider2D>();
        rb.gravityScale = 0f;
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        rb.constraints = RigidbodyConstraints2D.FreezeRotation;
    }

    public void Init(float faceDir, Collider2D playerCollider)
    {
        float dir = Mathf.Sign(faceDir);
        if (dir == 0f)
            dir = 1f;

        transform.rotation = Quaternion.Euler(0f, dir < 0f ? 180f : 0f, 0f);
        direction = new Vector2(dir, 0f);
        rb.linearVelocity = direction * speed;

        if (playerCollider != null && bulletCollider != null)
            Physics2D.IgnoreCollision(bulletCollider, playerCollider);

        Invoke(nameof(Explode), fuseTime);
    }

    void FixedUpdate()
    {
        if (!hasExploded)
            rb.linearVelocity = direction * speed;
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        TryExplodeFromHit(other);
    }

    void OnCollisionEnter2D(Collision2D collision)
    {
        if (collision != null)
            TryExplodeFromHit(collision.collider);
    }

    void TryExplodeFromHit(Collider2D other)
    {
        if (hasExploded || other == null)
            return;

        if (other.CompareTag("Player"))
            return;

        if (IsGround(other) || IsEnemyCollider(other))
            Explode();
    }

    static bool IsGround(Collider2D collider) =>
        LayerMask.LayerToName(collider.gameObject.layer) == "Ground";

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
            Instantiate(explosionPrefab, transform.position, Quaternion.identity);

        Destroy(gameObject);
    }
}
