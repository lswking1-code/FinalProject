using UnityEngine;

/// <summary>
/// 生命点道具。拾取后写入存档数据，读档重载场景时不会重新出现。
/// </summary>
public class LifePack : MonoBehaviour, ISaveable
{
    const string ConsumedKeySuffix = "consumed";

    [SerializeField] int amount = 1;
    [Tooltip("稳定 ID，同一关内不要重复。留空则用 场景名+物体名。")]
    [SerializeField] string persistId;

    bool consumed;

    void OnEnable()
    {
        ((ISaveable)this).RegisterSaveData();
        DataManager.instance?.ApplyLoadedData(this);
    }

    void OnDisable() => ((ISaveable)this).UnregisterSaveData();

    void OnTriggerEnter2D(Collider2D other) => TryPickup(other);

    void OnTriggerStay2D(Collider2D other) => TryPickup(other);

    void TryPickup(Collider2D other)
    {
        if (consumed)
            return;

        if (!other.CompareTag("Player"))
            return;

        if (PlayerLifePoints.Instance == null)
            return;

        if (!PlayerLifePoints.Instance.TryAdd(amount))
            return;

        consumed = true;
        PersistConsumed();
        DisablePickup();
        Destroy(gameObject);
    }

    void PersistConsumed()
    {
        var data = DataManager.instance != null ? DataManager.instance.CurrentData : null;
        if (data == null)
            return;

        GetSaveData(data);
        DataManager.instance.PersistTransientProgress();
    }

    void DisablePickup()
    {
        consumed = true;
        var colliders = GetComponents<Collider2D>();
        for (int i = 0; i < colliders.Length; i++)
            colliders[i].enabled = false;

        var sr = GetComponent<SpriteRenderer>();
        if (sr != null)
            sr.enabled = false;

        var rb = GetComponent<Rigidbody2D>();
        if (rb != null)
            rb.simulated = false;
    }

    public DataDefination GetDataID() => GetComponent<DataDefination>();

    public void GetSaveData(Data data)
    {
        if (data?.boolSavedData == null)
            return;

        data.boolSavedData[ProgressKey()] = consumed;
    }

    public void LoadSaveData(Data data)
    {
        if (data?.boolSavedData == null)
            return;

        if (data.boolSavedData.TryGetValue(ProgressKey(), out bool done) && done)
        {
            DisablePickup();
            Destroy(gameObject);
        }
    }

    string ProgressKey()
    {
        string id = !string.IsNullOrEmpty(persistId)
            ? persistId
            : $"{gameObject.scene.name}:{name}";
        return $"LifePack:{id}:{ConsumedKeySuffix}";
    }
}
