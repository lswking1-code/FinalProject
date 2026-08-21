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

    [Header("到达效果")]
    [Tooltip("开启后，玩家首次到达时先回满生命，再执行存档。")]
    public bool restoreFullHealthOnSave;

    [Header("弹药保底（谜题入口）")]
    [Tooltip("开启后，存档前若指定弹药低于下限则补到下限（避免空弹存档卡谜题）。")]
    public bool ensureMinAmmoOnSave;
    [Tooltip("存档时 BulletM 至少保留的数量；ensureMinAmmoOnSave 开启时生效。")]
    public int minBulletM = 3;
    [Tooltip("存档时 BulletS 至少保留的数量；0 表示不保底。")]
    public int minBulletS;
    [Tooltip("存档时 BulletL 至少保留的数量；0 表示不保底。")]
    public int minBulletL;

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

        var character = other.GetComponent<Character>()
            ?? other.GetComponentInParent<Character>();
        TrySave(character);
    }

    void TrySave(Character character)
    {
        if (isDone)
            return;

        if (restoreFullHealthOnSave && character != null)
            character.RestoreFullHealth();

        if (ensureMinAmmoOnSave && character != null)
            EnsureMinimumAmmo(character);

        isDone = true;
        ApplyVisualState();
        saveDataEvent?.RaiseEvent();
    }

    void EnsureMinimumAmmo(Character character)
    {
        EnsureAmmoAtLeast(character, AmmoType.M, minBulletM);
        EnsureAmmoAtLeast(character, AmmoType.S, minBulletS);
        EnsureAmmoAtLeast(character, AmmoType.L, minBulletL);
    }

    static void EnsureAmmoAtLeast(Character character, AmmoType type, int minAmount)
    {
        if (minAmount <= 0)
            return;

        int current = character.GetAmmo(type);
        if (current >= minAmount)
            return;

        character.AddAmmo(type, minAmount - current);
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
