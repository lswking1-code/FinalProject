using System;
using UnityEngine;

/// <summary>
/// 玩家生命点：独立于角色存档快照。读档不覆盖当前命数。
/// </summary>
[DefaultExecutionOrder(-50)]
public class PlayerLifePoints : MonoBehaviour
{
    public static PlayerLifePoints Instance { get; private set; }

    public const int DefaultCount = 5;
    public const int MaxCount = 9;

    int current = DefaultCount;

    public int Current => current;
    public int Max => MaxCount;

    public event Action<int> Changed;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }

        Instance = this;
        ApplyFromSaveData(persist: false);
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    /// <summary>有命时扣 1 点。成功返回 true。</summary>
    public bool TryConsume()
    {
        if (current <= 0)
            return false;

        SetCurrent(current - 1, persist: true);
        return true;
    }

    /// <summary>未满上限时加命。满了返回 false（道具不销毁）。</summary>
    public bool TryAdd(int amount = 1)
    {
        if (amount <= 0 || current >= MaxCount)
            return false;

        SetCurrent(Mathf.Min(MaxCount, current + amount), persist: true);
        return true;
    }

    /// <summary>新游戏 / 本关 Restart：回到默认 5 点，不写盘。</summary>
    public void ResetToDefault()
    {
        SetCurrent(DefaultCount, persist: false);
    }

    /// <summary>进程启动时从存档文件初始化，游戏中 Load() 不要调用。</summary>
    public void ApplyFromSaveData(bool persist)
    {
        int value = DefaultCount;
        if (DataManager.instance != null && DataManager.instance.CurrentData != null)
            value = DataManager.instance.CurrentData.lifePoints;

        SetCurrent(value, persist);
    }

    void SetCurrent(int value, bool persist)
    {
        current = Mathf.Clamp(value, 0, MaxCount);
        Changed?.Invoke(current);

        if (persist)
            DataManager.instance?.PersistLifePoints();
    }
}
