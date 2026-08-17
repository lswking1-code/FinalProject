using UnityEngine;

/// <summary>
/// 敌人手雷爆炸特效：播放 GrenadeExplosion 动画，结束后自毁。
/// 伤害由同物体上的 Attack 负责，由动画手动开关。
/// </summary>
[RequireComponent(typeof(Animator))]
public class EnemyGrenadeExplosion : MonoBehaviour
{
    const string ExplosionStateName = "GrenadeExplosion";

    Animator animator;
    bool isFinishing;

    void Awake()
    {
        animator = GetComponent<Animator>();
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
