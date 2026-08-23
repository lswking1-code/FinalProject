using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 可开关门：SetOpen(true) 关碰撞并可选滑开；SetOpen(false) 回位并恢复碰撞。
/// 供压力板等布尔驱动机关使用（AnimatedDestroy 仅支持一次性开门）。
/// </summary>
public class ActuatedGate : MonoBehaviour
{
    [Header("位移开门")]
    [Tooltip("开启时相对关闭位置的世界坐标偏移。竖门向上开可填 (0, 10)")]
    [SerializeField] Vector2 openWorldOffset;
    [SerializeField, Min(0.01f)] float openMoveDuration = 0.5f;

    [Header("碰撞")]
    [Tooltip("开启时禁用这些碰撞体；留空则取本物体及子物体全部 Collider2D")]
    [SerializeField] Collider2D[] colliders;

    [Header("事件")]
    [SerializeField] UnityEvent onOpened;
    [SerializeField] UnityEvent onClosed;

    bool isOpen;
    bool sliding;
    float slideTimer;
    Vector3 closedWorldPos;
    Vector3 openWorldPos;
    Vector3 slideStart;
    Vector3 slideEnd;
    bool targetOpen;

    public bool IsOpen => isOpen;

    void Awake()
    {
        if (colliders == null || colliders.Length == 0)
            colliders = GetComponentsInChildren<Collider2D>(true);

        closedWorldPos = transform.position;
        openWorldPos = closedWorldPos + (Vector3)openWorldOffset;
        ApplyOpenState(isOpen, instant: true);
    }

    void Update()
    {
        if (!sliding)
            return;

        slideTimer += Time.deltaTime;
        float t = Mathf.Clamp01(slideTimer / Mathf.Max(0.01f, openMoveDuration));
        transform.position = Vector3.Lerp(slideStart, slideEnd, t);
        if (t < 1f)
            return;

        transform.position = slideEnd;
        sliding = false;
        FinishTransition(targetOpen);
    }

    /// <summary>UnityEvent / PressurePlate.onToggled 接线入口。</summary>
    public void SetOpen(bool open)
    {
        if (isOpen == open && !sliding)
            return;

        if (sliding && targetOpen == open)
            return;

        targetOpen = open;

        if (openWorldOffset.sqrMagnitude < 0.0001f || openMoveDuration <= 0f)
        {
            transform.position = open ? openWorldPos : closedWorldPos;
            sliding = false;
            FinishTransition(open);
            return;
        }

        // 开门时先关碰撞，关门到位后再开碰撞
        if (open)
            SetCollidersEnabled(false);

        sliding = true;
        slideTimer = 0f;
        slideStart = transform.position;
        slideEnd = open ? openWorldPos : closedWorldPos;
    }

    void FinishTransition(bool open)
    {
        bool wasOpen = isOpen;
        isOpen = open;
        transform.position = open ? openWorldPos : closedWorldPos;
        SetCollidersEnabled(!open);

        if (wasOpen == open)
            return;

        if (open)
            onOpened?.Invoke();
        else
            onClosed?.Invoke();
    }

    void ApplyOpenState(bool open, bool instant)
    {
        isOpen = open;
        targetOpen = open;
        sliding = false;
        transform.position = open ? openWorldPos : closedWorldPos;
        SetCollidersEnabled(!open);
    }

    void SetCollidersEnabled(bool enabled)
    {
        if (colliders == null)
            return;

        for (int i = 0; i < colliders.Length; i++)
        {
            if (colliders[i] != null)
                colliders[i].enabled = enabled;
        }
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        Vector3 closed = Application.isPlaying ? closedWorldPos : transform.position;
        Vector3 open = closed + (Vector3)openWorldOffset;
        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(closed, open);
        Gizmos.DrawWireCube(open, Vector3.one * 0.25f);
    }
#endif
}
