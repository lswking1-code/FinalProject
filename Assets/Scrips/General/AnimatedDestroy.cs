using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 按命令播放打开/摧毁动画，结束后销毁或隐藏（门、机关等，不走 Attack 计次）。
/// 可由 BoundDevice / MultiCoreGate / UnityEvent 调用 BeginDestroy()。
/// </summary>
public class AnimatedDestroy : MonoBehaviour
{
    const string DefaultVisualName = "Visual";
    const float AnimatorEnterGrace = 0.25f;
    const float AnimatorSafetyTimeout = 8f;

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

    [Header("破坏特效")]
    [Tooltip("关闭后本扇门不播爆炸，仍保留特效资源引用")]
    [SerializeField] bool playDestructionEffect = true;
    [SerializeField] DoorDestructionEffect destructionEffect;
    [Tooltip("仅指定门体，不包含远处的控制节点；留空使用当前物体")]
    [SerializeField] Transform destructionEffectRoot;

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
    bool waitingForAnimator;
    bool sawDestroyState;
    float fallbackTimer;
    float animatorTimer;
    bool sliding;
    float slideTimer;
    Vector3 slideStart;
    Vector3 slideEnd;

    void Awake()
    {
        ResolveVisualRoot();
        // Animator 可能在根节点，而 visualRoot 只是名为 Visual 的空子物体。
        animator = GetComponentInChildren<Animator>(true);
        colliders = GetComponentsInChildren<Collider2D>(true);
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
        if (playDestructionEffect && destructionEffect != null)
            destructionEffect.Play(destructionEffectRoot != null ? destructionEffectRoot : transform);
        OnDestroyStarted?.Invoke();
        PrepareVisualForDestroyAnimation();
        DisableColliders();
        BeginSlideIfNeeded();

        if (TryPlayDestroyAnimation())
        {
            waitingForAnimator = true;
            animatorTimer = 0f;
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

    bool TryPlayDestroyAnimation()
    {
        if (animator == null || string.IsNullOrEmpty(destroyStateName))
            return false;

        if (!AnimatorHasState(animator, destroyStateName))
            return false;

        animator.Play(destroyStateName, 0, 0f);
        return true;
    }

    static bool AnimatorHasState(Animator target, string stateName)
    {
        if (target == null || string.IsNullOrEmpty(stateName))
            return false;

        int shortHash = Animator.StringToHash(stateName);
        int layeredHash = Animator.StringToHash("Base Layer." + stateName);
        return target.HasState(0, shortHash) || target.HasState(0, layeredHash);
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

        if (waitingForAnimator)
        {
            animatorTimer += Time.deltaTime;
            if (animator != null)
            {
                var info = animator.GetCurrentAnimatorStateInfo(0);
                if (info.IsName(destroyStateName))
                {
                    sawDestroyState = true;
                    if (info.normalizedTime < 1f && animatorTimer < AnimatorSafetyTimeout)
                        return;
                }
                else if (!sawDestroyState && animatorTimer < AnimatorEnterGrace)
                {
                    return;
                }
            }

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
        var animators = GetComponentsInChildren<Animator>(true);
        for (int i = 0; i < animators.Length; i++)
        {
            if (animators[i] != null)
                animators[i].enabled = false;
        }

        var renderers = GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] != null)
                renderers[i].enabled = false;
        }

        if (visualRoot != null && visualRoot != transform)
            visualRoot.gameObject.SetActive(false);
    }
}
