using System.IO;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 升降门：由 OverheadDoorCore 按伤害推进开度，停手后缓慢回关。
/// </summary>
public class OverheadDoor : MonoBehaviour
{
    // #region agent log
    const string DebugLogPath = "D:/Github/FinalProject/debug-a85fa1.log";
    void AgentLog(string hypothesisId, string location, string message, string dataJson)
    {
        try
        {
            long ts = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            File.AppendAllText(DebugLogPath,
                "{\"sessionId\":\"a85fa1\",\"hypothesisId\":\"" + hypothesisId +
                "\",\"location\":\"" + location + "\",\"message\":\"" + message +
                "\",\"data\":" + dataJson + ",\"timestamp\":" + ts + "}\n");
        }
        catch { }
    }
    // #endregion

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

        int assignedLen = doorColliders == null ? -1 : doorColliders.Length;
        int assignedNulls = 0;
        int assignedValid = 0;
        if (doorColliders != null)
        {
            for (int i = 0; i < doorColliders.Length; i++)
            {
                if (doorColliders[i] == null)
                    assignedNulls++;
                else
                    assignedValid++;
            }
        }

        bool usedFallback = false;
        if (doorColliders == null || doorColliders.Length == 0 || assignedValid == 0)
        {
            doorColliders = GetComponentsInChildren<Collider2D>(true);
            // 排除 Core 上的碰撞，避免通行时关掉受击区
            var filtered = new System.Collections.Generic.List<Collider2D>(doorColliders.Length);
            for (int i = 0; i < doorColliders.Length; i++)
            {
                if (doorColliders[i] == null)
                    continue;
                if (doorColliders[i].GetComponentInParent<OverheadDoorCore>() != null)
                    continue;
                filtered.Add(doorColliders[i]);
            }
            doorColliders = filtered.ToArray();
            usedFallback = true;
        }

        var col2dRoot = GetComponents<Collider2D>();
        var col2dAll = GetComponentsInChildren<Collider2D>(true);
        var col3dAll = GetComponentsInChildren<Collider>(true);
        int enabled2d = 0;
        for (int i = 0; i < col2dAll.Length; i++)
        {
            if (col2dAll[i] != null && col2dAll[i].enabled)
                enabled2d++;
        }
        int enabled3d = 0;
        for (int i = 0; i < col3dAll.Length; i++)
        {
            // Collider is 3D; exclude Collider2D which also inherits? No - Collider2D does NOT inherit Collider.
            if (col3dAll[i] != null && col3dAll[i].enabled)
                enabled3d++;
        }

        // #region agent log
        AgentLog("A,B,C", "OverheadDoor.Awake", "collider_snapshot",
            "{\"name\":\"" + name +
            "\",\"runId\":\"post-fix\"" +
            ",\"assignedLen\":" + assignedLen +
            ",\"assignedNulls\":" + assignedNulls +
            ",\"usedFallback\":" + (usedFallback ? "true" : "false") +
            ",\"doorCollidersLen\":" + (doorColliders == null ? -1 : doorColliders.Length) +
            ",\"rootCol2d\":" + col2dRoot.Length +
            ",\"allCol2d\":" + col2dAll.Length +
            ",\"enabledCol2d\":" + enabled2d +
            ",\"allCol3d\":" + col3dAll.Length +
            ",\"enabledCol3d\":" + enabled3d +
            ",\"progress\":" + progress.ToString("F3") +
            ",\"passThroughProgress\":" + passThroughProgress.ToString("F3") + "}");
        // #endregion

        ApplyPositionAndColliders();

        // #region agent log
        int afterEnabled = 0;
        if (doorColliders != null)
        {
            for (int i = 0; i < doorColliders.Length; i++)
            {
                if (doorColliders[i] != null && doorColliders[i].enabled)
                    afterEnabled++;
            }
        }
        AgentLog("D,E", "OverheadDoor.Awake", "after_apply",
            "{\"collidersPassThrough\":" + (collidersPassThrough ? "true" : "false") +
            ",\"doorCollidersEnabled\":" + afterEnabled +
            ",\"pos\":\"" + transform.position.x.ToString("F2") + "," + transform.position.y.ToString("F2") + "\"}");
        // #endregion
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
