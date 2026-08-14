using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Player 霰弹普通枪焰：停在枪口、不飞行；碰撞体由 Animation Event 开启，动画结束销毁。
/// 仅此路径带击退（绑定子弹 Attack，命中敌人/箱子时触发）；蓄力龙息与机械师 L 不加击退。
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(Attack))]
[RequireComponent(typeof(Collider2D))]
public class PlayerShotgunBlast : MonoBehaviour, IPlayerAmmo
{
    [SerializeField] int damage = 70;
    [SerializeField] bool enableKnockback = true;
    [SerializeField] float knockbackForce = 10f;
    [SerializeField] float knockbackDuration = 0.15f;
    [SerializeField] float abilityPowerRestore = 5f;

    Rigidbody2D rb;
    Attack attack;
    Collider2D hitCollider;
    Animator animator;
    Character owner;
    bool finished;
    readonly HashSet<Enemy> restoredTargets = new();
    readonly Dictionary<Enemy, float> nextRestoreTime = new();

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
        attack.enableKnockback = enableKnockback;
        attack.knockbackForce = knockbackForce;
        attack.knockbackDuration = knockbackDuration;

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
        else if (!restoredTargets.Add(enemy))
        {
            return;
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
