using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 霰弹蓄力龙息：停在枪口、扇形判定区；Melee + attackRate 对同目标多段伤害。
/// 碰撞体由 Animation Event 开启，动画结束销毁。
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(Attack))]
[RequireComponent(typeof(Collider2D))]
public class PlayerShotgunDragonBlast : MonoBehaviour, IPlayerAmmo
{
    [SerializeField] int damage = 30;
    [Tooltip("每秒伤害次数；写入 Attack.attackRate")]
    [SerializeField] float hitsPerSecond = 10f;
    [SerializeField] float lifetime = 0.55f;
    [SerializeField] Color flameTint = new Color(1f, 0.55f, 0.15f, 1f);
    [SerializeField] float abilityPowerRestore = 5f;

    Rigidbody2D rb;
    Attack attack;
    Collider2D hitCollider;
    Animator animator;
    SpriteRenderer spriteRenderer;
    Character owner;
    bool finished;
    readonly Dictionary<Enemy, float> nextRestoreTime = new();

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        attack = GetComponent<Attack>();
        hitCollider = GetComponent<Collider2D>();
        animator = GetComponent<Animator>();
        spriteRenderer = GetComponent<SpriteRenderer>();

        rb.bodyType = RigidbodyType2D.Kinematic;
        rb.linearVelocity = Vector2.zero;
        rb.gravityScale = 0f;

        attack.damage = damage;
        attack.attackType = AttackType.Melee;
        attack.attackRate = hitsPerSecond > 0f ? hitsPerSecond : 0f;
        attack.ignoreTag = "Player";
        // L 蓄力龙息：不带击退
        attack.enableKnockback = false;
        attack.knockbackForce = 0f;

        if (hitCollider != null)
            hitCollider.enabled = false;

        if (spriteRenderer != null)
            spriteRenderer.color = flameTint;
    }

    void Start()
    {
        float fallbackLifetime = lifetime > 0f ? lifetime : 0.55f;
        if (animator != null)
        {
            var info = animator.GetCurrentAnimatorStateInfo(0);
            if (info.length > 0f)
                fallbackLifetime = Mathf.Max(fallbackLifetime, info.length);
        }

        Destroy(gameObject, fallbackLifetime + 0.05f);
    }

    public void Init(FireDir dir, float faceY, Character owner = null)
    {
        this.owner = owner;
        transform.rotation = PlayerProjectile.GetRotation(dir, faceY);
        if (rb != null)
            rb.linearVelocity = Vector2.zero;
    }

    void OnTriggerEnter2D(Collider2D collision) => TryRestore(collision);

    void OnTriggerStay2D(Collider2D collision) => TryRestore(collision);

    void TryRestore(Collider2D collision)
    {
        if (owner == null || abilityPowerRestore <= 0f)
            return;
        if (collision.CompareTag("Player"))
            return;

        var enemy = collision.GetComponentInParent<Enemy>();
        if (enemy == null || enemy.isDead)
            return;

        float rate = attack != null ? attack.attackRate : 0f;
        if (rate > 0f)
        {
            if (nextRestoreTime.TryGetValue(enemy, out float next) && Time.time < next)
                return;
            nextRestoreTime[enemy] = Time.time + 1f / rate;
        }

        owner.RestoreAbilityPower(abilityPowerRestore);
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
