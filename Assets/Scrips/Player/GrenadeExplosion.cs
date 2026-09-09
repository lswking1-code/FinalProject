using System.Collections;
using UnityEngine;

[RequireComponent(typeof(CircleCollider2D))]
[RequireComponent(typeof(Attack))]
[RequireComponent(typeof(Animator))]
[DefaultExecutionOrder(-100)] // 先于 Attack.OnEnable，避免用 damage=0 结算并把目标记入 hitTargets
public class GrenadeExplosion : MonoBehaviour
{
    const string DefaultExplosionStateName = "GrenadeExplosion";

    [SerializeField] int damage = 40;
    [Tooltip("要播放的 Animator 状态名；状态不存在时使用控制器默认状态，播完仍会自毁")]
    [SerializeField] string explosionStateName = DefaultExplosionStateName;

    Animator animator;
    Attack attack;
    bool isFinishing;

    void Awake()
    {
        animator = GetComponent<Animator>();
        attack = GetComponent<Attack>();
        attack.damage = damage;
        attack.attackType = AttackType.Melee;
        attack.ignoreTag = "Player";
    }

    void Start()
    {
        if (animator != null && !string.IsNullOrEmpty(explosionStateName)
            && animator.HasState(0, Animator.StringToHash(explosionStateName)))
            animator.Play(explosionStateName, 0, 0f);

        // 生成当帧静态 Trigger 往往扫不到已重叠目标，下一拍物理再补扫一次。
        StartCoroutine(ApplySpawnHits());
    }

    IEnumerator ApplySpawnHits()
    {
        attack?.ProcessOverlapHits();
        yield return new WaitForFixedUpdate();
        if (!isFinishing)
            attack?.ProcessOverlapHits();
    }

    void Update()
    {
        if (isFinishing || animator == null)
            return;

        var info = animator.GetCurrentAnimatorStateInfo(0);
        if (info.length < 0.01f)
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
