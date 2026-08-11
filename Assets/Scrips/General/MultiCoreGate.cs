using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 多核心门控：监听多个核心击破后触发事件（开门 / 摧毁门等）。
/// 将每个核心的 BreakableProp.OnBroken 绑到 NotifyCoreBroken。
/// </summary>
public class MultiCoreGate : MonoBehaviour
{
    [Header("核心")]
    [Tooltip("需要击破的核心数量。若 cores 列表非空，运行时用列表长度覆盖")]
    [SerializeField, Min(1)] int requiredCores = 1;
    [Tooltip("可选：拖入本关相关核心。非空时 requiredCores = 列表长度，并在 Awake 自动订阅 OnBroken")]
    [SerializeField] BreakableProp[] cores;

    [Header("完成")]
    [SerializeField] UnityEvent OnAllCoresBroken;
    [Tooltip("可选：完成时直接调用本物体或引用上的 AnimatedDestroy.BeginDestroy")]
    [SerializeField] AnimatedDestroy destroyOnComplete;

    int brokenCount;
    bool completed;

    public int BrokenCount => brokenCount;
    public int RequiredCores => requiredCores;
    public bool IsCompleted => completed;

    void Awake()
    {
        if (cores != null && cores.Length > 0)
            requiredCores = Mathf.Max(1, cores.Length);

        if (destroyOnComplete == null)
            destroyOnComplete = GetComponent<AnimatedDestroy>();
    }

    void OnEnable()
    {
        if (cores == null)
            return;

        for (int i = 0; i < cores.Length; i++)
        {
            if (cores[i] != null)
                cores[i].AddBrokenListener(NotifyCoreBroken);
        }
    }

    void OnDisable()
    {
        if (cores == null)
            return;

        for (int i = 0; i < cores.Length; i++)
        {
            if (cores[i] != null)
                cores[i].RemoveBrokenListener(NotifyCoreBroken);
        }
    }

    /// <summary>
    /// 供 Inspector 手动接线：核心 BreakableProp.OnBroken → 本方法。
    /// 也可仅拖 cores 列表由脚本自动订阅。
    /// </summary>
    public void NotifyCoreBroken()
    {
        if (completed)
            return;

        brokenCount++;
        if (brokenCount < requiredCores)
            return;

        completed = true;
        OnAllCoresBroken?.Invoke();
        if (destroyOnComplete != null)
            destroyOnComplete.BeginDestroy();
    }
}
