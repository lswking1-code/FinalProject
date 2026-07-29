using System;
using System.Collections.Generic;
using UnityEngine;

public enum SpecialAmmoType
{
    S,
    M,
    L,
}

/// <summary>
/// 机械师特殊弹 FIFO 弹夹；与 Character 普通弹药库存独立。
/// </summary>
public class SpecialMagazine : MonoBehaviour
{
    [SerializeField] int capacity = 7;

    [Header("调试（Play 模式）")]
    [Tooltip("FIFO 顺序：Index 0 为下一发将消耗的特殊弹。仅用于 Inspector 查看，勿手动改。")]
    [SerializeField] List<SpecialAmmoType> debugRounds = new List<SpecialAmmoType>();

    readonly Queue<SpecialAmmoType> rounds = new Queue<SpecialAmmoType>();

    public event Action<SpecialAmmoType> RoundLoaded;
    public event Action<SpecialAmmoType> RoundConsumed;

    public int Capacity => capacity;
    public int Count => rounds.Count;
    public int RemainingCapacity => Mathf.Max(0, capacity - rounds.Count);

    /// <summary>
    /// 尝试装入 loadCount 发同种特殊弹。会超容时整次失败，不入队。
    /// </summary>
    public bool TryLoad(SpecialAmmoType type, int loadCount)
    {
        if (loadCount <= 0)
            return false;

        if (rounds.Count + loadCount > capacity)
            return false;

        for (int i = 0; i < loadCount; i++)
        {
            rounds.Enqueue(type);
            RoundLoaded?.Invoke(type);
        }

        SyncDebugView();
        return true;
    }

    /// <summary>
    /// 按 FIFO 消耗 1 发。弹夹为空时返回 false。
    /// </summary>
    public bool TryConsume(out SpecialAmmoType type)
    {
        if (rounds.Count == 0)
        {
            type = default;
            return false;
        }

        type = rounds.Dequeue();
        SyncDebugView();
        RoundConsumed?.Invoke(type);
        return true;
    }

    /// <summary>
    /// 只读窥视队首，不出队。弹夹为空时返回 false。
    /// </summary>
    public bool TryPeek(out SpecialAmmoType type)
    {
        if (rounds.Count == 0)
        {
            type = default;
            return false;
        }

        type = rounds.Peek();
        return true;
    }

    /// <summary>
    /// 只读枚举当前队列（从前到后，即先消耗的在前）。
    /// </summary>
    public IEnumerable<SpecialAmmoType> EnumerateRounds() => rounds;

    void SyncDebugView()
    {
        debugRounds.Clear();
        debugRounds.AddRange(rounds);
    }
}
