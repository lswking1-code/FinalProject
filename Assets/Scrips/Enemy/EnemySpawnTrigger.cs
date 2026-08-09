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

    void OnDrawGizmosSelected()
    {
        if (spawnPoint != null)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(spawnPoint.position, 0.3f);
        }
    }
}
