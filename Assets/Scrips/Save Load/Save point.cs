using UnityEngine;

/// <summary>
/// 存档点：玩家进入 Collider2D（Trigger）时触发一次全局存档。
/// </summary>
[RequireComponent(typeof(DataDefination))]
[RequireComponent(typeof(Collider2D))]
public class Savepoint : MonoBehaviour, ISaveable
{
    const string IsDoneKeySuffix = "isDone";

    [Header("广播")]
    public VoidEventSO saveDataEvent;

    [Header("存档点显示")]
    public SpriteRenderer spriteRenderer;
    public GameObject Light2D;
    public Sprite darkSprite;
    public Sprite lightSprite;
    public bool isDone;

    void Awake()
    {
        var col = GetComponent<Collider2D>();
        if (col != null && !col.isTrigger)
            Debug.LogWarning("Savepoint: Collider2D 建议勾选 Is Trigger，以便玩家进入时触发存档。", this);
    }

    void OnEnable()
    {
        ApplyVisualState();
        ((ISaveable)this).RegisterSaveData();
        DataManager.instance?.ApplyLoadedData(this);
    }

    void OnDisable() => ((ISaveable)this).UnregisterSaveData();

    void OnTriggerEnter2D(Collider2D other)
    {
        if (!other.CompareTag("Player"))
            return;

        TrySave();
    }

    void TrySave()
    {
        if (isDone)
            return;

        isDone = true;
        ApplyVisualState();
        saveDataEvent?.RaiseEvent();
    }

    void ApplyVisualState()
    {
        if (spriteRenderer != null)
            spriteRenderer.sprite = isDone ? lightSprite : darkSprite;
        if (Light2D != null)
            Light2D.SetActive(isDone);
    }

    public DataDefination GetDataID() => GetComponent<DataDefination>();

    public void GetSaveData(Data data)
    {
        var dataId = GetDataID();
        if (dataId == null || string.IsNullOrEmpty(dataId.ID))
            return;

        data.boolSavedData[ProgressKey(IsDoneKeySuffix)] = isDone;
    }

    public void LoadSaveData(Data data)
    {
        var dataId = GetDataID();
        if (dataId == null || string.IsNullOrEmpty(dataId.ID))
            return;

        if (data.boolSavedData.TryGetValue(ProgressKey(IsDoneKeySuffix), out bool done))
            isDone = done;

        ApplyVisualState();
    }

    string ProgressKey(string suffix)
    {
        var dataId = GetDataID();
        string id = dataId != null && !string.IsNullOrEmpty(dataId.ID) ? dataId.ID : name;
        return $"{gameObject.scene.name}:{id}:{name}:{suffix}";
    }
}
