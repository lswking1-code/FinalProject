using UnityEngine;

/// <summary>
/// 附着在被 L 蓄力弹标记的敌人（或盾兵盾牌）上的炸弹。
/// 宿主受到 / 盾牌吸收 Tag 为 Blast 的 Attack 时引爆，播放爆炸动画并造成范围伤害。
/// </summary>
[RequireComponent(typeof(Attack))]
public class EnemyMarkBomb : MonoBehaviour
{
    const string BlastTag = "Blast";

    [SerializeField] float lifetime = 10f;
    [SerializeField] int explosionDamage = 40;
    [SerializeField] string explosionStateName = "BombExplosion";
    [SerializeField] float fallbackDestroyDelay = 0.5f;
    [Tooltip("若指定则在引爆点生成该特效（如 GrenadeExplosion）；否则使用本物体 Animator/Attack")]
    [SerializeField] GameObject explosionVfxPrefab;

    Enemy host;
    Character hostCharacter;
    Attack explosionAttack;
    Animator animator;
    Collider2D explosionCollider;
    SpriteRenderer spriteRenderer;
    bool ownsMark;
    bool hasDetonated;
    bool isFinishing;
    bool subscribed;
    bool usingExternalVfx;

    void Awake()
    {
        explosionAttack = GetComponent<Attack>();
        animator = GetComponent<Animator>();
        explosionCollider = GetComponent<Collider2D>();
        spriteRenderer = GetComponent<SpriteRenderer>();

        explosionAttack.damage = explosionDamage;
        explosionAttack.attackType = AttackType.Melee;
        explosionAttack.ignoreTag = "Player";

        // 待机时不造成伤害，引爆后再启用
        if (explosionCollider != null)
            explosionCollider.enabled = false;
        explosionAttack.enabled = false;
    }

    void Start()
    {
        if (lifetime > 0f)
            Invoke(nameof(Detonate), lifetime);
    }

    public void Init(Enemy enemy)
    {
        host = enemy;
        ownsMark = true;

        if (host == null)
            return;

        hostCharacter = host.GetComponent<Character>();
        if (hostCharacter != null)
        {
            hostCharacter.OnTakeDamage.AddListener(OnHostTakeDamage);
            hostCharacter.OnDie.AddListener(OnHostDie);
            subscribed = true;
        }
    }

    void OnHostTakeDamage(Transform attackTrans)
    {
        TryDetonateFromBlast(attackTrans);
    }

    /// <summary>
    /// 宿主受伤或盾牌吸收 Blast 时调用；确认是 Blast Attack 后引爆。
    /// </summary>
    public void TryDetonateFromBlast(Transform attackTrans)
    {
        if (hasDetonated || attackTrans == null)
            return;

        if (!HasBlastTag(attackTrans))
            return;

        if (attackTrans.GetComponent<Attack>() == null
            && attackTrans.GetComponentInParent<Attack>() == null)
            return;

        Detonate();
    }

    static bool HasBlastTag(Transform root)
    {
        for (Transform t = root; t != null; t = t.parent)
        {
            if (t.CompareTag(BlastTag))
                return true;
        }

        return false;
    }

    void OnHostDie()
    {
        Detonate();
    }

    void Detonate()
    {
        if (hasDetonated)
            return;

        hasDetonated = true;
        CancelInvoke(nameof(Detonate));
        Unsubscribe();
        ClearMark();

        // 脱离宿主，避免宿主死亡/移动打断爆炸表现
        transform.SetParent(null, true);

        if (explosionVfxPrefab != null)
        {
            usingExternalVfx = true;
            Instantiate(explosionVfxPrefab, transform.position, Quaternion.identity);
            if (spriteRenderer != null)
                spriteRenderer.enabled = false;
            Destroy(gameObject);
            return;
        }

        explosionAttack.damage = explosionDamage;
        explosionAttack.attackType = AttackType.Melee;
        explosionAttack.ignoreTag = "Player";
        explosionAttack.enabled = true;
        if (explosionCollider != null)
            explosionCollider.enabled = true;

        if (animator != null && !string.IsNullOrEmpty(explosionStateName))
            animator.Play(explosionStateName, 0, 0f);
        else
            Destroy(gameObject, fallbackDestroyDelay);
    }

    void Update()
    {
        if (!hasDetonated || isFinishing || usingExternalVfx || animator == null)
            return;

        var info = animator.GetCurrentAnimatorStateInfo(0);
        if (!info.IsName(explosionStateName))
            return;

        if (info.normalizedTime < 1f)
            return;

        Finish();
    }

    void Finish()
    {
        if (isFinishing)
            return;

        isFinishing = true;
        Destroy(gameObject);
    }

    void ClearMark()
    {
        if (!ownsMark || host == null)
            return;

        host.isMarked = false;
        ownsMark = false;
    }

    void Unsubscribe()
    {
        if (!subscribed || hostCharacter == null)
            return;

        hostCharacter.OnTakeDamage.RemoveListener(OnHostTakeDamage);
        hostCharacter.OnDie.RemoveListener(OnHostDie);
        subscribed = false;
    }

    void OnDestroy()
    {
        Unsubscribe();
        ClearMark();
    }
}
