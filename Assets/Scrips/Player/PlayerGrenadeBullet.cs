using FMODUnity;
using UnityEngine;

/// <summary>
/// 向前飞行的手雷弹：命中敌人、核心、墙壁或引信到期后生成 GrenadeExplosion。
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(CircleCollider2D))]
public class PlayerGrenadeBullet : MonoBehaviour
{
    const string AirEnemyTag = "AirEnemy";
    const string EnemyTag = "Enemy";

    [SerializeField] float speed = 10f;
    [SerializeField] float fuseTime = 1.5f;
    [SerializeField] GrenadeExplosion explosionPrefab;
    [Header("音效")]
    [SerializeField] EventReference explodeEvent;

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
            TryExplodeFromHit(collision.collider,
                collision.contactCount > 0 ? collision.GetContact(0).point : (Vector2?)null);
    }

    void TryExplodeFromHit(Collider2D other, Vector2? contactPoint = null)
    {
        if (hasExploded || other == null)
            return;

        if (other.CompareTag("Player"))
            return;

        if (GrenadeExplosion.IsCoreCollider(other))
        {
            Vector2 contact = contactPoint ?? other.ClosestPoint(transform.position);
            ExplodeAt(new Vector3(contact.x, contact.y, transform.position.z));
            return;
        }

        if (Attack.IsProjectileBlockingCollider(other) || IsEnemyCollider(other))
            Explode();
    }

    static bool IsEnemyCollider(Collider2D collider)
    {
        if (collider.CompareTag(EnemyTag) || collider.CompareTag(AirEnemyTag))
            return true;

        var character = collider.GetComponentInParent<Character>();
        return character != null
            && (character.CompareTag(EnemyTag) || character.CompareTag(AirEnemyTag));
    }

    void Explode()
    {
        ExplodeAt(transform.position);
    }

    void ExplodeAt(Vector3 explodePos)
    {
        if (hasExploded)
            return;

        hasExploded = true;
        CancelInvoke(nameof(Explode));

        if (!explodeEvent.IsNull)
            FmodAudio.Play(explodeEvent, explodePos);

        if (explosionPrefab != null)
            Instantiate(explosionPrefab, explodePos, Quaternion.identity);

        Destroy(gameObject);
    }
}
