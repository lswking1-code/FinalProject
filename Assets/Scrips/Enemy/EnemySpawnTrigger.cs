using UnityEngine;

/// <summary>
/// 玩家进入触发区域后生成敌人，默认只生成一次。
/// </summary>
[RequireComponent(typeof(DataDefination))]
public class EnemySpawnTrigger : MonoBehaviour, ISaveable
{
    const string HasSpawnedKeySuffix = "hasSpawned";

    [SerializeField] GameObject enemyPrefab;
    [SerializeField] Transform spawnPoint;
    [SerializeField] bool spawnOnce = true;

    [Header("编辑器显示")]
    [Tooltip("在 Scene 视图中始终绘制刷怪点")]
    [SerializeField] bool alwaysDrawSpawnPoint = true;

    bool hasSpawned;

    void OnEnable()
    {
        ((ISaveable)this).RegisterSaveData();
        DataManager.instance?.ApplyLoadedData(this);
    }

    void OnDisable() => ((ISaveable)this).UnregisterSaveData();

    void OnTriggerEnter2D(Collider2D other)
    {
        if (!other.CompareTag("Player"))
            return;
        if (spawnOnce && hasSpawned)
            return;

        hasSpawned = true;
        SpawnEnemy();
    }

    void SpawnEnemy()
    {
        if (enemyPrefab == null)
        {
            Debug.LogWarning("EnemySpawnTrigger: enemyPrefab 未配置。", this);
            return;
        }

        Vector3 pos = spawnPoint != null ? spawnPoint.position : transform.position;
        var instance = Instantiate(enemyPrefab, pos, Quaternion.identity);
        EnemySceneCleanup.PlaceInSourceScene(instance, this);
        var spawnedEnemy = instance.GetComponent<Enemy>() ?? instance.GetComponentInChildren<Enemy>();
        spawnedEnemy?.MarkAsRuntimeSpawned();
    }

    public DataDefination GetDataID() => GetComponent<DataDefination>();

    string ProgressKey(string suffix)
    {
        var dataId = GetDataID();
        string id = dataId != null && !string.IsNullOrEmpty(dataId.ID) ? dataId.ID : name;
        return $"{gameObject.scene.name}:{id}:{name}:{suffix}";
    }

    public void GetSaveData(Data data)
    {
        if (!spawnOnce)
            return;

        var dataId = GetDataID();
        if (dataId == null || string.IsNullOrEmpty(dataId.ID))
            return;

        data.boolSavedData[ProgressKey(HasSpawnedKeySuffix)] = hasSpawned;
    }

    public void LoadSaveData(Data data)
    {
        if (!spawnOnce)
            return;

        var dataId = GetDataID();
        if (dataId == null || string.IsNullOrEmpty(dataId.ID))
            return;

        if (data.boolSavedData.TryGetValue(ProgressKey(HasSpawnedKeySuffix), out bool spawned))
            hasSpawned = spawned;
    }

    void OnDrawGizmos()
    {
        if (!alwaysDrawSpawnPoint)
            return;

        Vector3 pos = spawnPoint != null ? spawnPoint.position : transform.position;
        Color color = new Color(1f, 0.25f, 0.2f, 1f);

        Color fill = color;
        fill.a = 0.35f;
        Gizmos.color = fill;
        Gizmos.DrawSphere(pos, 0.18f);

        Gizmos.color = color;
        Gizmos.DrawWireSphere(pos, 0.28f);
        Gizmos.DrawLine(pos + Vector3.left * 0.35f, pos + Vector3.right * 0.35f);
        Gizmos.DrawLine(pos + Vector3.up * 0.35f, pos + Vector3.down * 0.35f);
    }
}
