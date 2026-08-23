using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 敌人死亡门控：指定场景敌人全部死亡后触发事件（向上开门等）。
/// 读档时已击杀敌人会被直接移除且不走 OnDie，因此会扫描缺失 / 已死 / 存档击杀。
/// </summary>
[RequireComponent(typeof(DataDefination))]
public class EnemyDeathGate : MonoBehaviour, ISaveable
{
    const string CompletedKeySuffix = "completed";

    [Header("敌人")]
    [Tooltip("需要击杀的敌人数量。若 requiredEnemies 列表非空，运行时用列表长度覆盖")]
    [SerializeField, Min(1)] int requiredCount = 1;
    [Tooltip("拖入本关必须击杀的场景敌人。非空时 requiredCount = 列表长度，并自动订阅 OnDie。不要留空槽，不要拖 Clone")]
    [SerializeField] Character[] requiredEnemies;

    [Header("完成")]
    [SerializeField] UnityEvent OnAllEnemiesDead;
    [Tooltip("可选：完成时调用本物体或引用上的 AnimatedDestroy.BeginOpen")]
    [SerializeField] AnimatedDestroy destroyOnComplete;

    readonly Dictionary<Character, UnityAction> dieHandlers = new();
    readonly HashSet<int> countedSlots = new();
    string[] cachedProgressKeys;
    int deadCount;
    bool completed;

    public int DeadCount => deadCount;
    public int RequiredCount => requiredCount;
    public bool IsCompleted => completed;

    void Awake()
    {
        if (requiredEnemies != null && requiredEnemies.Length > 0)
            requiredCount = Mathf.Max(1, requiredEnemies.Length);

        if (destroyOnComplete == null)
            destroyOnComplete = GetComponent<AnimatedDestroy>();

        CacheProgressKeys();
    }

    void OnEnable()
    {
        SubscribeLivingEnemies();
        ((ISaveable)this).RegisterSaveData();
        DataManager.instance?.ApplyLoadedData(this);
        if (!completed)
            EvaluateAlreadySatisfied();
    }

    void Start()
    {
        // Additive 加载时 OnEnable 里 scene.name 可能仍为空，补一次。
        RefreshCachedKeysIfNeeded();
        DataManager.instance?.ApplyLoadedData(this);
        if (!completed)
            EvaluateAlreadySatisfied();
    }

    void OnDisable()
    {
        UnsubscribeAll();
        ((ISaveable)this).UnregisterSaveData();
    }

    void CacheProgressKeys()
    {
        if (requiredEnemies == null)
        {
            cachedProgressKeys = System.Array.Empty<string>();
            return;
        }

        cachedProgressKeys = new string[requiredEnemies.Length];
        for (int i = 0; i < requiredEnemies.Length; i++)
        {
            var character = requiredEnemies[i];
            if (character == null)
                continue;

            cachedProgressKeys[i] = EnemyDeathPersist.BuildProgressKey(character.transform);
        }
    }

    void RefreshCachedKeysIfNeeded()
    {
        if (requiredEnemies == null)
            return;

        if (cachedProgressKeys == null || cachedProgressKeys.Length != requiredEnemies.Length)
        {
            CacheProgressKeys();
            return;
        }

        for (int i = 0; i < requiredEnemies.Length; i++)
        {
            if (!string.IsNullOrEmpty(cachedProgressKeys[i]))
                continue;

            var character = requiredEnemies[i];
            if (character == null)
                continue;

            cachedProgressKeys[i] = EnemyDeathPersist.BuildProgressKey(character.transform);
        }
    }

    void SubscribeLivingEnemies()
    {
        if (requiredEnemies == null)
            return;

        for (int i = 0; i < requiredEnemies.Length; i++)
        {
            var character = requiredEnemies[i];
            if (character == null || character.IsDead || dieHandlers.ContainsKey(character))
                continue;

            int slot = i;
            UnityAction handler = () => OnRequiredEnemyDied(slot);
            dieHandlers[character] = handler;
            character.OnDie.AddListener(handler);
        }
    }

    void UnsubscribeAll()
    {
        foreach (var pair in dieHandlers)
        {
            if (pair.Key != null)
                pair.Key.OnDie.RemoveListener(pair.Value);
        }

        dieHandlers.Clear();
    }

    void OnRequiredEnemyDied(int slot) => MarkSlotSatisfied(slot);

    void EvaluateAlreadySatisfied()
    {
        if (completed || requiredEnemies == null)
            return;

        for (int i = 0; i < requiredEnemies.Length; i++)
        {
            if (IsSlotSatisfied(i))
                MarkSlotSatisfied(i);
        }
    }

    bool IsSlotSatisfied(int slot)
    {
        if (requiredEnemies == null || slot < 0 || slot >= requiredEnemies.Length)
            return false;

        var character = requiredEnemies[slot];
        if (character == null || character.IsDead)
            return true;

        string key = SlotProgressKey(slot, character);
        return EnemyDeathProgress.IsSavedDead(DataManager.instance?.CurrentData, key);
    }

    string SlotProgressKey(int slot, Character character)
    {
        if (cachedProgressKeys != null && slot >= 0 && slot < cachedProgressKeys.Length
            && !string.IsNullOrEmpty(cachedProgressKeys[slot]))
            return cachedProgressKeys[slot];

        return character != null
            ? EnemyDeathPersist.BuildProgressKey(character.transform)
            : string.Empty;
    }

    void MarkSlotSatisfied(int slot)
    {
        if (completed)
            return;
        if (!countedSlots.Add(slot))
            return;

        NotifyEnemyDied();
    }

    /// <summary>
    /// 供 Inspector 手动接线：敌人 Character.OnDie → 本方法。
    /// 也可仅拖 requiredEnemies 列表由脚本自动订阅。
    /// </summary>
    public void NotifyEnemyDied()
    {
        if (completed)
            return;

        deadCount++;
        if (deadCount < requiredCount)
            return;

        Complete();
    }

    void Complete()
    {
        if (completed)
            return;

        completed = true;
        UnsubscribeAll();
        OnAllEnemiesDead?.Invoke();
        if (destroyOnComplete != null)
            destroyOnComplete.BeginOpen();
    }

    void ApplyCompletedState()
    {
        if (completed)
            return;

        completed = true;
        deadCount = requiredCount;
        UnsubscribeAll();
        if (destroyOnComplete != null)
            destroyOnComplete.BeginOpen();
    }

    public DataDefination GetDataID() => GetComponent<DataDefination>();

    string ProgressKey(string suffix)
    {
        var dataId = GetDataID();
        string id = dataId != null && !string.IsNullOrEmpty(dataId.ID) ? dataId.ID : name;
        string sceneName = gameObject.scene.IsValid() && !string.IsNullOrEmpty(gameObject.scene.name)
            ? gameObject.scene.name
            : name;
        return $"{sceneName}:{id}:{name}:{suffix}";
    }

    public void GetSaveData(Data data)
    {
        if (data?.boolSavedData == null)
            return;

        var dataId = GetDataID();
        if (dataId == null || string.IsNullOrEmpty(dataId.ID))
            return;

        data.boolSavedData[ProgressKey(CompletedKeySuffix)] = completed;
    }

    public void LoadSaveData(Data data)
    {
        if (data?.boolSavedData == null)
            return;

        var dataId = GetDataID();
        if (dataId == null || string.IsNullOrEmpty(dataId.ID))
            return;

        bool wasCompleted = data.boolSavedData.TryGetValue(ProgressKey(CompletedKeySuffix), out bool saved)
            && saved;

        if (wasCompleted)
            ApplyCompletedState();
    }
}
