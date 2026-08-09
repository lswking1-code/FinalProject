using UnityEngine;

/// <summary>
/// 掉落平台：玩家碰撞一次后开始倒计时，时间到后播放摧毁动画并禁用碰撞，动画结束后销毁自身。
/// </summary>
public class FallingPlatform : MonoBehaviour
{
    [Header("掉落")]
    [Tooltip("玩家碰撞后到开始摧毁动画的等待秒数")]
    [SerializeField] float fallDelay = 1f;

    [Header("摧毁动画")]
    [Tooltip("Animator 中摧毁状态名；为空则跳过动画直接销毁")]
    [SerializeField] string destroyStateName = "Destroy";
    [Tooltip("无 Animator 或状态名无效时的销毁延迟（秒）")]
    [SerializeField] float fallbackDestroyDelay = 0.5f;

    Animator animator;
    Collider2D[] colliders;
    bool hasTriggered;
    bool isDestroying;
    bool isFinishing;
    float armedTimer;
    float fallbackTimer;

    void Awake()
    {
        animator = GetComponent<Animator>();
        colliders = GetComponentsInChildren<Collider2D>();
    }

    void OnCollisionEnter2D(Collision2D collision)
    {
        if (hasTriggered || isDestroying)
            return;

        if (!IsPlayer(collision.collider))
            return;

        hasTriggered = true;
        armedTimer = fallDelay;
    }

    void Update()
    {
        if (isFinishing)
            return;

        if (isDestroying)
        {
            UpdateDestroying();
            return;
        }

        if (!hasTriggered)
            return;

        armedTimer -= Time.deltaTime;
        if (armedTimer > 0f)
            return;

        BeginDestroy();
    }

    void BeginDestroy()
    {
        if (isDestroying)
            return;

        isDestroying = true;

        for (int i = 0; i < colliders.Length; i++)
        {
            if (colliders[i] != null)
                colliders[i].enabled = false;
        }

        if (animator != null && !string.IsNullOrEmpty(destroyStateName))
        {
            animator.Play(destroyStateName, 0, 0f);
            return;
        }

        fallbackTimer = fallbackDestroyDelay;
    }

    void UpdateDestroying()
    {
        if (animator != null && !string.IsNullOrEmpty(destroyStateName))
        {
            var info = animator.GetCurrentAnimatorStateInfo(0);
            if (!info.IsName(destroyStateName))
                return;

            if (info.normalizedTime < 1f)
                return;

            Finish();
            return;
        }

        fallbackTimer -= Time.deltaTime;
        if (fallbackTimer <= 0f)
            Finish();
    }

    void Finish()
    {
        if (isFinishing)
            return;

        isFinishing = true;
        Destroy(gameObject);
    }

    static bool IsPlayer(Collider2D other)
    {
        if (other == null)
            return false;

        if (other.CompareTag("Player"))
            return true;

        Transform root = other.transform.root;
        return root != null && root.CompareTag("Player");
    }
}
