using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 遭遇结束门控：指定 EncounterZone 结束后开门（AnimatedDestroy / ActuatedGate）。
/// 读档后若遭遇已完成，会立刻开门。UnlockLock 只解空气墙，不会开门。
/// </summary>
public class EncounterClearGate : MonoBehaviour
{
    [Header("遭遇")]
    [Tooltip("拖入对应遭遇区。结束后自动开门。不要留空")]
    [SerializeField] EncounterZone encounterZone;

    [Header("完成")]
    [SerializeField] UnityEvent OnEncounterCleared;
    [Tooltip("可选：完成时调用本物体或引用上的 AnimatedDestroy.BeginOpen")]
    [SerializeField] AnimatedDestroy destroyOnComplete;
    [Tooltip("可选：完成时调用本物体或引用上的 ActuatedGate.SetOpen(true)")]
    [SerializeField] ActuatedGate gateOnComplete;

    bool completed;

    public bool IsCompleted => completed;
    public EncounterZone EncounterZone => encounterZone;

    void Awake()
    {
        if (destroyOnComplete == null)
            destroyOnComplete = GetComponent<AnimatedDestroy>();
        if (gateOnComplete == null)
            gateOnComplete = GetComponent<ActuatedGate>();
    }

    void OnEnable()
    {
        Subscribe();
        TryCompleteIfAlreadyCleared();
    }

    void Start()
    {
        // Additive 加载时遭遇区可能比本脚本更晚套用存档，补一次。
        TryCompleteIfAlreadyCleared();
    }

    void OnDisable() => Unsubscribe();

    void Subscribe()
    {
        if (encounterZone != null)
            encounterZone.OnEncounterEnded.AddListener(NotifyEncounterEnded);
    }

    void Unsubscribe()
    {
        if (encounterZone != null)
            encounterZone.OnEncounterEnded.RemoveListener(NotifyEncounterEnded);
    }

    void TryCompleteIfAlreadyCleared()
    {
        if (encounterZone != null && encounterZone.HasCompleted)
            Complete();
    }

    /// <summary>
    /// 供 Inspector 手动接线：EncounterZone.OnEncounterEnded → 本方法。
    /// 也可仅拖 encounterZone 由脚本自动订阅。
    /// </summary>
    public void NotifyEncounterEnded()
    {
        if (encounterZone != null && !encounterZone.HasCompleted)
            return;

        Complete();
    }

    void Complete()
    {
        if (completed)
            return;

        completed = true;
        OnEncounterCleared?.Invoke();
        if (destroyOnComplete != null)
            destroyOnComplete.BeginOpen();
        if (gateOnComplete != null)
            gateOnComplete.SetOpen(true);
    }
}
