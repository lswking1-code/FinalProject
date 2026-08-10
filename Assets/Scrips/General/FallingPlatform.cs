using UnityEngine;

/// <summary>
/// 掉落平台：玩家碰撞一次后开始倒计时，时间到后播放摧毁动画并禁用碰撞，动画结束后销毁自身。
/// 倒计时期间连续闪红，提示已被踩中。
/// 视觉与 Animator 放在子物体上：待机继承根缩放；销毁前对视觉做逆缩放补偿，避免摧毁动画被压扁。
/// </summary>
public class FallingPlatform : MonoBehaviour
{
    const string DefaultVisualName = "Visual";

    [Header("掉落")]
    [Tooltip("玩家碰撞后到开始摧毁动画的等待秒数")]
    [SerializeField] float fallDelay = 1f;
    [Tooltip("判定「从上踩上」的法线阈值。仅 contact.normal.y 小于 -该值时触发（平台侧：法线从玩家指向本平台）")]
    [SerializeField, Range(0.1f, 1f)] float minTopNormal = 0.5f;

    [Header("踩中警示")]
    [Tooltip("被踩后销毁前的闪红颜色")]
    [SerializeField] Color warnFlashColor = new Color(1f, 0.25f, 0.25f, 1f);
    [Tooltip("闪红频率（次/秒）：红与原色互相切换的次数。越小越慢，越大越快")]
    [SerializeField, Min(0.1f)] float warnFlashFrequency = 2f;

    [Header("摧毁动画")]
    [Tooltip("视觉根节点（含 SpriteRenderer / Animator）。留空则查找子物体 Visual，或本物体")]
    [SerializeField] Transform visualRoot;
    [Tooltip("摧毁动画播放时，视觉在世界中目标等比缩放（各轴绝对值）。0 表示取当前世界缩放的最大轴")]
    [SerializeField] float destroyVisualWorldScale = 1f;
    [Tooltip("Animator 中摧毁状态名；为空则跳过动画直接销毁")]
    [SerializeField] string destroyStateName = "Destroy";
    [Tooltip("无 Animator 或状态名无效时的销毁延迟（秒）")]
    [SerializeField] float fallbackDestroyDelay = 0.5f;

    /// <summary>编辑器 Bake 等外部写入配置；不改变运行时流程。</summary>
    public void ApplyEditorSettings(float fallDelay, string destroyStateName, float fallbackDestroyDelay)
    {
        this.fallDelay = fallDelay;
        this.destroyStateName = destroyStateName;
        this.fallbackDestroyDelay = fallbackDestroyDelay;
    }

    Animator animator;
    Collider2D[] colliders;
    SpriteRenderer[] spriteRenderers;
    Color[] originalColors;

    bool hasTriggered;
    bool isDestroying;
    bool isFinishing;
    bool flashOn;
    float armedTimer;
    float fallbackTimer;
    float flashTimer;

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

        // 无 Visual 子物体时，Animator/Sprite 仍可在自身上
        visualRoot = transform;
    }

    void OnCollisionEnter2D(Collision2D collision)
    {
        TryArmFromPlayerContact(collision);
    }

    void OnCollisionStay2D(Collision2D collision)
    {
        // 落地首帧法线偶发不准时，Stay 可补触发「踩上」
        TryArmFromPlayerContact(collision);
    }

    void TryArmFromPlayerContact(Collision2D collision)
    {
        if (hasTriggered || isDestroying)
            return;

        if (!IsPlayer(collision.collider))
            return;

        if (!IsContactFromAbove(collision))
            return;

        hasTriggered = true;
        armedTimer = fallDelay;
        flashTimer = 0f;
        flashOn = true;
        ApplyFlashColors(true);
    }

    /// <summary>
    /// 平台侧 ContactPoint2D.normal 从对方指向本碰撞体。
    /// 玩家站在上方时法线约向下，故 normal.y 为负。
    /// </summary>
    bool IsContactFromAbove(Collision2D collision)
    {
        float threshold = -Mathf.Clamp(minTopNormal, 0.1f, 1f);
        int count = collision.contactCount;
        for (int i = 0; i < count; i++)
        {
            if (collision.GetContact(i).normal.y <= threshold)
                return true;
        }

        return false;
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

        UpdateWarnFlash();

        armedTimer -= Time.deltaTime;
        if (armedTimer > 0f)
            return;

        BeginDestroy();
    }

    void UpdateWarnFlash()
    {
        float interval = 1f / Mathf.Max(0.1f, warnFlashFrequency);
        flashTimer += Time.deltaTime;
        if (flashTimer < interval)
            return;

        flashTimer = 0f;
        flashOn = !flashOn;
        ApplyFlashColors(flashOn);
    }

    void ApplyFlashColors(bool warn)
    {
        for (int i = 0; i < spriteRenderers.Length; i++)
        {
            if (spriteRenderers[i] == null)
                continue;
            spriteRenderers[i].color = warn ? warnFlashColor : originalColors[i];
        }
    }

    void RestoreOriginalColors()
    {
        for (int i = 0; i < spriteRenderers.Length; i++)
        {
            if (spriteRenderers[i] == null)
                continue;
            spriteRenderers[i].color = originalColors[i];
        }
    }

    void BeginDestroy()
    {
        if (isDestroying)
            return;

        isDestroying = true;
        RestoreOriginalColors();
        PrepareVisualForDestroyAnimation();

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
