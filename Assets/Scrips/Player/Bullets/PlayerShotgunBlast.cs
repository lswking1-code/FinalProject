using UnityEngine;

/// <summary>
/// 霰弹枪枪焰：停在枪口、不飞行；碰撞体由 Animation Event 开启，动画结束销毁。
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(Attack))]
[RequireComponent(typeof(Collider2D))]
public class PlayerShotgunBlast : MonoBehaviour, IPlayerAmmo
{
    [SerializeField] int damage = 70;

    Rigidbody2D rb;
    Attack attack;
    Collider2D hitCollider;
    Animator animator;
    bool finished;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        attack = GetComponent<Attack>();
        hitCollider = GetComponent<Collider2D>();
        animator = GetComponent<Animator>();

        rb.bodyType = RigidbodyType2D.Kinematic;
        rb.linearVelocity = Vector2.zero;
        rb.gravityScale = 0f;

        attack.damage = damage;
        attack.attackType = AttackType.Melee;
        attack.ignoreTag = "Player";

        if (hitCollider != null)
            hitCollider.enabled = false;
    }

    void Start()
    {
        float fallbackLifetime = 0.75f;
        if (animator != null)
        {
            var info = animator.GetCurrentAnimatorStateInfo(0);
            if (info.length > 0f)
                fallbackLifetime = info.length;
        }

        Destroy(gameObject, fallbackLifetime + 0.05f);
    }

    public void Init(FireDir dir, float faceY, Character owner = null)
    {
        transform.rotation = PlayerProjectile.GetRotation(dir, faceY);
        if (rb != null)
            rb.linearVelocity = Vector2.zero;
    }

    /// <summary>Animation Event：关键帧开启判定。</summary>
    public void EnableHitbox()
    {
        if (hitCollider != null)
            hitCollider.enabled = true;
    }

    /// <summary>Animation Event：可选关闭判定。</summary>
    public void DisableHitbox()
    {
        if (hitCollider != null)
            hitCollider.enabled = false;
    }

    /// <summary>Animation Event：动画结束销毁。</summary>
    public void OnBlastFinished()
    {
        if (finished)
            return;

        finished = true;
        Destroy(gameObject);
    }
}
