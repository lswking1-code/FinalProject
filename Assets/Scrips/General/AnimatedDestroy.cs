using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 按命令播放打开/摧毁动画，结束后销毁或隐藏（门、机关等，不走 Attack 计次）。
/// 可由 BoundDevice / MultiCoreGate / UnityEvent 调用 BeginDestroy()。
/// </summary>
public class AnimatedDestroy : MonoBehaviour
{
    const string DefaultVisualName = "Visual";

    [Header("打开 / 摧毁动画")]
    [Tooltip("视觉根节点（含 SpriteRenderer / Animator）。留空则查找子物体 Visual，或本物体")]
    [SerializeField] Transform visualRoot;
    [Tooltip("动画播放时，视觉在世界中目标等比缩放（各轴绝对值）。0 表示取当前世界缩放的最大轴")]
    [SerializeField] float destroyVisualWorldScale = 1f;
    [Tooltip("Animator 状态名（开门用 Open，摧毁用 Destroy）。为空则跳过动画，按延迟结束")]
    [SerializeField] string destroyStateName = "Destroy";
    [Tooltip("无 Animator 或状态名无效时的结束延迟（秒）")]
    [SerializeField] float fallbackDestroyDelay = 0.5f;
    [Tooltip("动画完全结束后隐藏视觉（不销毁物体），避免最后一帧遮挡。门请勾选此项")]
    [SerializeField] bool hideWhenFinished = true;

    [Header("位移开门")]
    [Tooltip("世界坐标位移。Stage2 竖门高度约 10，向上开填 (0, 10)。有 Animator 位移动画时请留 (0,0)")]
    [SerializeField] Vector2 openWorldOffset;
    [SerializeField, Min(0.01f)] float openMoveDuration = 0.5f;

    [Header("事件")]
    [SerializeField] UnityEvent OnDestroyStarted;

    Animator animator;
    Collider2D[] colliders;
    bool isDestroying;
    bool isFinishing;
    float fallbackTimer;
    bool sliding;
    float slideTimer;
    Vector3 slideStart;
    Vector3 slideEnd;

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

    /// <summary>UnityEvent / BoundDevice 调用入口：关碰撞 → 播动画 → 隐藏或销毁。</summary>
    public void BeginDestroy()
    {
        if (isDestroying || isFinishing)
            return;

        isDestroying = true;
        OnDestroyStarted?.Invoke();
        PrepareVisualForDestroyAnimation();
        DisableColliders();
        BeginSlideIfNeeded();

        if (animator != null && !string.IsNullOrEmpty(destroyStateName))
        {
            animator.Play(destroyStateName, 0, 0f);
            return;
        }

        if (sliding)
            return;

        fallbackTimer = fallbackDestroyDelay;
    }

    /// <summary>开门入口，与 BeginDestroy 相同，方便 Inspector 接线。</summary>
    public void BeginOpen() => BeginDestroy();

    void Update()
    {
        if (isFinishing || !isDestroying)
            return;

        TickSlide();
        UpdateDestroying();
    }

    void BeginSlideIfNeeded()
    {
        if (openWorldOffset.sqrMagnitude < 0.0001f || openMoveDuration <= 0f)
            return;

        sliding = true;
        slideTimer = 0f;
        slideStart = transform.position;
        slideEnd = slideStart + (Vector3)openWorldOffset;
    }

    void TickSlide()
    {
        if (!sliding)
            return;

        slideTimer += Time.deltaTime;
        float t = Mathf.Clamp01(slideTimer / openMoveDuration);
        transform.position = Vector3.Lerp(slideStart, slideEnd, t);
        if (t < 1f)
            return;

        transform.position = slideEnd;
        sliding = false;
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
        if (sliding)
            return;

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

        if (hideWhenFinished)
        {
            HideVisual();
            return;
        }

        Destroy(gameObject);
    }

    void HideVisual()
    {
        if (animator != null)
            animator.enabled = false;

        if (visualRoot != null && visualRoot != transform)
        {
            visualRoot.gameObject.SetActive(false);
            return;
        }

        var renderers = GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] != null)
                renderers[i].enabled = false;
        }
    }
}
