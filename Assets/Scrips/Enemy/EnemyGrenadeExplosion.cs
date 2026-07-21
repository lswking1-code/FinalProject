using UnityEngine;

[RequireComponent(typeof(CircleCollider2D))]
[RequireComponent(typeof(Attack))]
[RequireComponent(typeof(Animator))]
public class EnemyGrenadeExplosion : MonoBehaviour
{
    const string ExplosionStateName = "GrenadeExplosion";

    [SerializeField] int damage = 40;

    Animator animator;
    bool isFinishing;

    void Awake()
    {
        animator = GetComponent<Animator>();

        var attack = GetComponent<Attack>();
        attack.damage = damage;
        attack.attackType = AttackType.Melee;
        attack.requireTag = "Player";
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
