using UnityEngine;

/// <summary>
/// 标记炸弹爆炸特效：播放 BombExplosion 动画，结束后自毁。
/// </summary>
[RequireComponent(typeof(CircleCollider2D))]
[RequireComponent(typeof(Attack))]
[RequireComponent(typeof(Animator))]
public class BombBlastExplosion : MonoBehaviour
{
    const string ExplosionStateName = "BombExplosion";

    [SerializeField] int damage = 40;

    Animator animator;
    bool isFinishing;

    void Awake()
    {
        animator = GetComponent<Animator>();

        var attack = GetComponent<Attack>();
        attack.damage = damage;
        attack.attackType = AttackType.Melee;
        attack.ignoreTag = "Player";
    }

    void Start()
    {
        if (animator != null)
            animator.Play(ExplosionStateName, 0, 0f);
    }

    void Update()
    {
        if (isFinishing || animator == null)
            return;

        var info = animator.GetCurrentAnimatorStateInfo(0);
        if (!info.IsName(ExplosionStateName))
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
}
