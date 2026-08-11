using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 按命中次数破坏的场景物：忽略伤害数值，仅计数。
/// 核心（isCore）击破后触发 OnBroken；普通物播破坏动画后销毁。
/// </summary>
public class BreakableProp : MonoBehaviour, IHitCountable
{
    const string DefaultVisualName = "Visual";

    [Header("击破")]
    [Tooltip("达到该命中次数后破坏（忽略武器伤害）")]
    [SerializeField, Min(1)] int hitsToBreak = 3;
    [Tooltip("核心：击破后触发 OnBroken，默认不销毁（除非勾选 destroyOnBreak）；普通物：播破坏动画后销毁")]
    [SerializeField] bool isCore;
    [Tooltip("仅对核心有效：击破后是否销毁自身。普通物始终销毁")]
    [SerializeField] bool destroyOnBreak;
    [SerializeField] UnityEvent OnBroken;

    public bool IsBroken => isBroken;
    public int CurrentHits => currentHits;
    public int HitsToBreak => hitsToBreak;

    /// <summary>代码侧订阅击破（MultiCoreGate 等）；与 Inspector OnBroken 一并触发。</summary>
    public void AddBrokenListener(UnityAction listener)
    {
        if (listener == null)
            return;
        OnBroken.AddListener(listener);
    }

    public void RemoveBrokenListener(UnityAction listener)
    {
        if (listener == null)
            return;
        OnBroken.RemoveListener(listener);
    }

    [Header("受击反馈")]
    [Tooltip("受击闪烁颜色；a=0 则关闭")]
    [SerializeField] Color hitFlashColor = new Color(1f, 0.35f, 0.35f, 1f);
    [SerializeField, Min(0f)] float hitFlashDuration = 0.08f;

    [Header("摧毁动画")]
    [Tooltip("视觉根节点（含 SpriteRenderer / Animator）。留空则查找子物体 Visual，或本物体")]
    [SerializeField] Transform visualRoot;
    [Tooltip("摧毁动画播放时，视觉在世界中目标等比缩放（各轴绝对值）。0 表示取当前世界缩放的最大轴")]
    [SerializeField] float destroyVisualWorldScale = 1f;
    [Tooltip("Animator 中摧毁状态名；为空则跳过动画直接销毁")]
    [SerializeField] string destroyStateName = "Destroy";
    [Tooltip("无 Animator 或状态名无效时的销毁延迟（秒）")]
    [SerializeField] float fallbackDestroyDelay = 0.5f;

    int currentHits;
    bool isBroken;
    bool isDestroying;
    bool isFinishing;
    Attack lastHitAttacker;
    int lastHitFrame = -1;

    Animator animator;
    Collider2D[] colliders;
    SpriteRenderer[] spriteRenderers;
    Color[] originalColors;

    float flashTimer;
    float fallbackTimer;
    bool flashing;

    void Awake()
    {
        ResolveVisualRoot();
        animator = visualRoot != null
            ? visualRoot.GetComponentInChildren<Animator>(true)
            : GetComponentInChildren<Animator>(true);

        colliders = GetComponentsInChildren<Collider2D>();
        spriteRenderers = visualRoot != null
            ? visualRoot.GetComponentsInChildren<SpriteRenderer>(true)
            : GetComponentsInChildren<SpriteRenderer>(true);

        originalColors = new Color[spriteRenderers.Length];
        for (int i = 0; i < spriteRenderers.Length; i++)
        {
            if (spriteRenderers[i] != null)
                originalColors[i] = spriteRenderers[i].color;
        }
    }

    void OnValidate()
    {
        hitsToBreak = Mathf.Max(1, hitsToBreak);
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

    public bool RegisterHit(Attack attacker)
    {
        if (isBroken || isDestroying)
            return false;

        // 同一 Attack 实例同一帧内去重（Bob 的 Trigger + Overlap 双路径）
        if (attacker != null && attacker == lastHitAttacker && lastHitFrame == Time.frameCount)
            return true;

        lastHitAttacker = attacker;
        lastHitFrame = Time.frameCount;

        currentHits++;
        BeginHitFlash();

        if (currentHits < hitsToBreak)
            return true;

        Break();
        return true;
    }

    void Break()
    {
        if (isBroken)
            return;

        isBroken = true;
        RestoreOriginalColors();
        flashing = false;

        OnBroken?.Invoke();

        // 普通物始终销毁；核心仅在 destroyOnBreak 为 true 时销毁
        if (!isCore || destroyOnBreak)
            BeginDestroy();
        else
            DisableColliders();
    }

    void BeginDestroy()
    {
        if (isDestroying)
            return;

        isDestroying = true;
        PrepareVisualForDestroyAnimation();
        DisableColliders();

        if (animator != null && !string.IsNullOrEmpty(destroyStateName))
        {
            animator.Play(destroyStateName, 0, 0f);
            return;
        }

        fallbackTimer = fallbackDestroyDelay;
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

    void Update()
    {
        if (isFinishing)
            return;

        if (flashing)
            UpdateHitFlash();

        if (isDestroying)
            UpdateDestroying();
    }

    void BeginHitFlash()
    {
        if (hitFlashDuration <= 0f || hitFlashColor.a <= 0f || spriteRenderers == null)
            return;

        flashing = true;
        flashTimer = hitFlashDuration;
        ApplyFlashColors(true);
    }

    void UpdateHitFlash()
    {
        flashTimer -= Time.deltaTime;
        if (flashTimer > 0f)
            return;

        flashing = false;
        RestoreOriginalColors();
    }

    void ApplyFlashColors(bool flash)
    {
        for (int i = 0; i < spriteRenderers.Length; i++)
        {
            if (spriteRenderers[i] == null)
                continue;
            spriteRenderers[i].color = flash ? hitFlashColor : originalColors[i];
        }
    }

    void RestoreOriginalColors()
    {
        if (spriteRenderers == null)
            return;

        for (int i = 0; i < spriteRenderers.Length; i++)
        {
            if (spriteRenderers[i] == null)
                continue;
            spriteRenderers[i].color = originalColors[i];
        }
    }

    /// <summary>
    /// 销毁前抵消根节点非等比缩放，使摧毁动画以等比世界尺寸播放。
    /// </summary>
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
