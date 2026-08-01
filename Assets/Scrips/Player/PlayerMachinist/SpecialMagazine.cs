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
[RequireComponent(typeof(DataDefination))]
public class SpecialMagazine : MonoBehaviour, ISaveable
{
    const string SpecialMagKeySuffix = "specialMag";

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

    void OnEnable()
    {
        ((ISaveable)this).RegisterSaveData();
        DataManager.instance?.ApplyLoadedData(this);
    }

    void OnDisable() => ((ISaveable)this).UnregisterSaveData();

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

    public void Clear()
    {
        while (rounds.Count > 0)
        {
            var type = rounds.Dequeue();
            RoundConsumed?.Invoke(type);
        }

        SyncDebugView();
    }

    /// <summary>
    /// 用给定顺序重建弹夹（先消耗的在前）。会触发 RoundLoaded 以便 UI 同步。
    /// </summary>
    public void Restore(IEnumerable<SpecialAmmoType> types)
    {
        Clear();
        if (types == null)
            return;

        foreach (var type in types)
        {
            if (rounds.Count >= capacity)
                break;
            rounds.Enqueue(type);
            RoundLoaded?.Invoke(type);
        }

        SyncDebugView();
    }

    void SyncDebugView()
    {
        debugRounds.Clear();
        debugRounds.AddRange(rounds);
    }

    public DataDefination GetDataID() => GetComponent<DataDefination>();

    public void GetSaveData(Data data)
    {
        var dataId = GetDataID();
        if (dataId == null || string.IsNullOrEmpty(dataId.ID))
            return;

        var list = new List<int>(rounds.Count);
        foreach (var round in rounds)
            list.Add((int)round);

        data.intListSavedData[dataId.ID + SpecialMagKeySuffix] = list;
    }

    public void LoadSaveData(Data data)
    {
        var dataId = GetDataID();
        if (dataId == null || string.IsNullOrEmpty(dataId.ID))
            return;

        string key = dataId.ID + SpecialMagKeySuffix;
        if (!data.intListSavedData.TryGetValue(key, out var list) || list == null)
        {
            Clear();
            return;
        }

        var restored = new List<SpecialAmmoType>(list.Count);
        for (int i = 0; i < list.Count; i++)
        {
            int value = list[i];
            if (value < 0 || value > (int)SpecialAmmoType.L)
                continue;
            restored.Add((SpecialAmmoType)value);
        }

        Restore(restored);
    }
}
