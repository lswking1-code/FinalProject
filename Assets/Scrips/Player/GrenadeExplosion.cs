using UnityEngine;

[RequireComponent(typeof(CircleCollider2D))]
[RequireComponent(typeof(Attack))]
[RequireComponent(typeof(Animator))]
[DefaultExecutionOrder(-100)] // 先于 Attack.OnEnable，避免用 damage=0 结算并把目标记入 hitTargets
public class GrenadeExplosion : MonoBehaviour
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
