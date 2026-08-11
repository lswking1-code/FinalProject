using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 按命令播放摧毁动画并销毁自身（门、机关等，不走 Attack 计次）。
/// 可由 MultiCoreGate / UnityEvent / 其它脚本调用 BeginDestroy()。
/// </summary>
public class AnimatedDestroy : MonoBehaviour
{
    const string DefaultVisualName = "Visual";

    [Header("摧毁动画")]
    [Tooltip("视觉根节点（含 SpriteRenderer / Animator）。留空则查找子物体 Visual，或本物体")]
    [SerializeField] Transform visualRoot;
    [Tooltip("摧毁动画播放时，视觉在世界中目标等比缩放（各轴绝对值）。0 表示取当前世界缩放的最大轴")]
    [SerializeField] float destroyVisualWorldScale = 1f;
    [Tooltip("Animator 中摧毁状态名；为空则跳过动画直接销毁")]
    [SerializeField] string destroyStateName = "Destroy";
    [Tooltip("无 Animator 或状态名无效时的销毁延迟（秒）")]
    [SerializeField] float fallbackDestroyDelay = 0.5f;

    [Header("事件")]
    [SerializeField] UnityEvent OnDestroyStarted;

    Animator animator;
    Collider2D[] colliders;
    bool isDestroying;
    bool isFinishing;
    float fallbackTimer;

    void Awake()
    {
        ResolveVisualRoot();
        animator = visualRoot != null
            ? visualRoot.GetComponentInChildren<Animator>(true)
            : GetComponentInChildren<Animator>(true);
        colliders = GetComponentsInChildren<Collider2D>();
    }

    void ResolveVisualRoot()
    {
        if (visualRoot != null)
            return;

        Transform found = transform.Find(DefaultVisualName);
        if (found != null)
        {
            visualRoot = found;
            return;
        }

        visualRoot = transform;
    }

    /// <summary>UnityEvent / MultiCoreGate 调用入口：关碰撞 → 播动画 → 销毁。</summary>
    public void BeginDestroy()
    {
        if (isDestroying || isFinishing)
            return;

        isDestroying = true;
        OnDestroyStarted?.Invoke();
        PrepareVisualForDestroyAnimation();
        DisableColliders();

        if (animator != null && !string.IsNullOrEmpty(destroyStateName))
        {
            animator.Play(destroyStateName, 0, 0f);
            return;
        }

        fallbackTimer = fallbackDestroyDelay;
    }

    void Update()
    {
        if (isFinishing || !isDestroying)
            return;

        UpdateDestroying();
    }

    void DisableColliders()
    {
        if (colliders == null)
            return;

        for (int i = 0; i < colliders.Length; i++)
        {
            if (colliders[i] != null)
                colliders[i].enabled = false;
        }
    }

    void PrepareVisualForDestroyAnimation()
    {
        if (visualRoot == null || visualRoot == transform)
            return;

        Transform parent = visualRoot.parent;
        if (parent == null)
            return;

        Vector3 parentLossy = parent.lossyScale;
        float target = destroyVisualWorldScale;
        if (target <= 0.0001f)
        {
            float ax = Mathf.Abs(visualRoot.lossyScale.x);
            float ay = Mathf.Abs(visualRoot.lossyScale.y);
            target = Mathf.Max(ax, ay, 0.0001f);
        }

        visualRoot.localScale = new Vector3(
            target / Mathf.Max(0.0001f, Mathf.Abs(parentLossy.x)),
            target / Mathf.Max(0.0001f, Mathf.Abs(parentLossy.y)),
            target / Mathf.Max(0.0001f, Mathf.Abs(parentLossy.z))
        );
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
}
