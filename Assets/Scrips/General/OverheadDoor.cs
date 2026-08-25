using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 升降门：由 OverheadDoorCore 按伤害推进开度，停手后缓慢回关。
/// </summary>
public class OverheadDoor : MonoBehaviour
{
    [Header("位移")]
    [Tooltip("全开时相对关闭位置的世界坐标偏移。竖门向上开可填 (0, 10)")]
    [SerializeField] Vector2 openWorldOffset = new Vector2(0f, 10f);

    [Header("伤害推进")]
    [Tooltip("累计多少伤害开满（例如 100；单次 damage=20 则推进 20%）")]
    [SerializeField, Min(1)] int damageToFullyOpen = 100;

    [Header("回落")]
    [Tooltip("最后一次受击后多久开始回落（秒）")]
    [SerializeField, Min(0f)] float idleDelay = 1f;
    [Tooltip("回落时进度衰减速率（进度/秒，1 = 每秒完全关回）")]
    [SerializeField, Min(0.01f)] float returnSpeed = 0.35f;

    [Header("碰撞")]
    [Tooltip("进度达到该值时禁用门体碰撞，便于通行")]
    [SerializeField, Range(0f, 1f)] float passThroughProgress = 0.85f;
    [Tooltip("门体碰撞；勿包含 Core 碰撞，否则回关后无法再打 Core。留空则取本物体上的 Collider2D（不含子物体）")]
    [SerializeField] Collider2D[] doorColliders;

    [Header("事件")]
    [SerializeField] UnityEvent onFullyOpen;
    [SerializeField] UnityEvent onFullyClosed;

    float progress;
    float lastHitTime = float.NegativeInfinity;
    Vector3 closedWorldPos;
    Vector3 openWorldPos;
    bool collidersPassThrough;
    bool wasFullyOpen;
    bool wasFullyClosed = true;

    public float Progress => progress;
    public int DamageToFullyOpen => damageToFullyOpen;

    void Awake()
    {
        closedWorldPos = transform.position;
        openWorldPos = closedWorldPos + (Vector3)openWorldOffset;

        if (doorColliders == null || doorColliders.Length == 0)
            doorColliders = GetComponents<Collider2D>();

        ApplyPositionAndColliders();
    }

    void OnValidate()
    {
        damageToFullyOpen = Mathf.Max(1, damageToFullyOpen);
        returnSpeed = Mathf.Max(0.01f, returnSpeed);
    }

    void Update()
    {
        if (Time.time - lastHitTime > idleDelay && progress > 0f)
        {
            progress = Mathf.Max(0f, progress - returnSpeed * Time.deltaTime);
            ApplyPositionAndColliders();
            RaiseProgressEvents();
        }
    }

    /// <summary>由 OverheadDoorCore 调用：按伤害推进开度。</summary>
    public void ApplyDamage(int damage)
    {
        if (damage <= 0 || damageToFullyOpen <= 0)
            return;

        lastHitTime = Time.time;
        progress = Mathf.Clamp01(progress + damage / (float)damageToFullyOpen);
        ApplyPositionAndColliders();
        RaiseProgressEvents();
    }

    void ApplyPositionAndColliders()
    {
        transform.position = Vector3.Lerp(closedWorldPos, openWorldPos, progress);

        bool passThrough = progress >= passThroughProgress;
        if (passThrough == collidersPassThrough)
            return;

        collidersPassThrough = passThrough;
        SetDoorCollidersEnabled(!passThrough);
    }

    void SetDoorCollidersEnabled(bool enabled)
    {
        if (doorColliders == null)
            return;

        for (int i = 0; i < doorColliders.Length; i++)
        {
            if (doorColliders[i] != null)
                doorColliders[i].enabled = enabled;
        }
    }

    void RaiseProgressEvents()
    {
        bool fullyOpen = progress >= 1f - 0.0001f;
        bool fullyClosed = progress <= 0.0001f;

        if (fullyOpen && !wasFullyOpen)
            onFullyOpen?.Invoke();
        if (fullyClosed && !wasFullyClosed)
            onFullyClosed?.Invoke();

        wasFullyOpen = fullyOpen;
        wasFullyClosed = fullyClosed;
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        Vector3 closed = Application.isPlaying ? closedWorldPos : transform.position;
        Vector3 open = closed + (Vector3)openWorldOffset;
        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(closed, open);
        Gizmos.DrawWireCube(open, Vector3.one * 0.25f);
    }
#endif
}
