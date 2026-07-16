using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 按波次刷怪：可配置敌人种类、波数、每波数量与总刷怪上限。
/// </summary>
public class EnemyGenerate : MonoBehaviour
{
    /// <summary>从敌人种类列表中选取预制体的方式。</summary>
    public enum EnemyPickMode
    {
        /// <summary>每次从列表中随机选一种。</summary>
        Random,
        /// <summary>按列表顺序轮流选取。</summary>
        Sequential
    }

    [Header("敌人种类")]
    [Tooltip("可刷出的敌人预制体列表，可配置多种")]
    [SerializeField] GameObject[] enemyPrefabs;
    [Tooltip("Random：每次随机一种；Sequential：按列表顺序轮流刷")]
    [SerializeField] EnemyPickMode pickMode = EnemyPickMode.Random;

    [Header("波次与数量")]
    [Tooltip("总共刷几波；刷完后自动停止")]
    [SerializeField] int waveCount = 3;
    [Tooltip("每一波刷几个敌人")]
    [SerializeField] int spawnCountPerWave = 2;
    [Tooltip("总刷怪上限；0 表示不额外限制（实际总数 = 波数 × 每波数量）")]
    [SerializeField] int maxTotalSpawns;
    [Tooltip("相邻两波之间的等待时间（秒）")]
    [SerializeField] float spawnInterval = 3f;
    [Tooltip("开始刷怪前的首次延迟（秒）")]
    [SerializeField] float initialDelay;

    [Header("生成点")]
    [Tooltip("刷怪位置列表；为空则在本物体位置生成。每波内按索引循环使用")]
    [SerializeField] Transform[] spawnPoints;

    [Header("遭遇战（可选）")]
    [Tooltip("若指定，生成的敌人会自动 RegisterEnemy 到该遭遇区，用于清敌结束判定")]
    [SerializeField] EncounterZone encounterZone;

    [Header("启动")]
    [Tooltip("勾选后在 Start 时自动开始刷怪；遭遇战场景可关掉，改由事件调用 StartSpawning")]
    [SerializeField] bool spawnOnStart = true;

    [Header("事件")]
    [Tooltip("每一波敌人生成完毕时触发")]
    public UnityEvent OnWaveSpawned;
    [Tooltip("全部波次刷完（或达到总上限）时触发")]
    public UnityEvent OnSpawningCompleted;

    Coroutine spawnRoutine;
    int totalSpawned;
    int sequentialIndex;

    public int WaveCount => Mathf.Max(0, waveCount);
    public int SpawnCountPerWave => Mathf.Max(0, spawnCountPerWave);
    public int TotalSpawned => totalSpawned;
    public int MaxTotalSpawns => GetEffectiveTotalLimit();
    public bool IsSpawning => spawnRoutine != null;

    void Start()
    {
        if (spawnOnStart)
            StartSpawning();
    }

    void OnDisable() => StopSpawning();

    public void StartSpawning()
    {
        StopSpawning();
        totalSpawned = 0;
        sequentialIndex = 0;
        spawnRoutine = StartCoroutine(SpawnRoutine());
    }

    public void StopSpawning()
    {
        if (spawnRoutine == null)
            return;

        StopCoroutine(spawnRoutine);
        spawnRoutine = null;
    }

    IEnumerator SpawnRoutine()
    {
        if (initialDelay > 0f)
            yield return new WaitForSeconds(initialDelay);

        int waves = WaveCount;
        int perWave = SpawnCountPerWave;
        int totalLimit = GetEffectiveTotalLimit();

        if (waves <= 0 || perWave <= 0 || totalLimit <= 0)
        {
            spawnRoutine = null;
            OnSpawningCompleted?.Invoke();
            yield break;
        }

        for (int wave = 0; wave < waves; wave++)
        {
            int remaining = totalLimit - totalSpawned;
            if (remaining <= 0)
                break;

            int countThisWave = Mathf.Min(perWave, remaining);
            SpawnWave(countThisWave);
            OnWaveSpawned?.Invoke();

            if (totalSpawned >= totalLimit)
                break;

            if (wave < waves - 1 && spawnInterval > 0f)
                yield return new WaitForSeconds(spawnInterval);
        }

        spawnRoutine = null;
        OnSpawningCompleted?.Invoke();
    }

    void SpawnWave(int count)
    {
        if (!HasValidPrefabs())
        {
            Debug.LogWarning("EnemyGenerate: enemyPrefabs 未配置或全为空。", this);
            return;
        }

        for (int i = 0; i < count; i++)
            SpawnEnemyAt(GetSpawnPosition(i));
    }

    void SpawnEnemyAt(Vector3 position)
    {
        var prefab = PickPrefab();
        if (prefab == null)
            return;

        var instance = Instantiate(prefab, position, Quaternion.identity);
        totalSpawned++;

        if (encounterZone != null)
            encounterZone.RegisterEnemy(instance);
    }

    GameObject PickPrefab()
    {
        if (enemyPrefabs == null || enemyPrefabs.Length == 0)
            return null;

        var valid = ListValidPrefabs();
        if (valid.Count == 0)
            return null;

        if (pickMode == EnemyPickMode.Sequential)
        {
            var prefab = valid[sequentialIndex % valid.Count];
            sequentialIndex++;
            return prefab;
        }

        return valid[Random.Range(0, valid.Count)];
    }

    List<GameObject> ListValidPrefabs()
    {
        var list = new List<GameObject>();
        if (enemyPrefabs == null)
            return list;

        for (int i = 0; i < enemyPrefabs.Length; i++)
        {
            if (enemyPrefabs[i] != null)
                list.Add(enemyPrefabs[i]);
        }

        return list;
    }

    bool HasValidPrefabs()
    {
        if (enemyPrefabs == null)
            return false;

        for (int i = 0; i < enemyPrefabs.Length; i++)
        {
            if (enemyPrefabs[i] != null)
                return true;
        }

        return false;
    }

    int GetEffectiveTotalLimit()
    {
        int fromWaves = WaveCount * SpawnCountPerWave;
        if (maxTotalSpawns <= 0)
            return fromWaves;

        return Mathf.Min(fromWaves, maxTotalSpawns);
    }

    Vector3 GetSpawnPosition(int index)
    {
        if (spawnPoints == null || spawnPoints.Length == 0)
            return transform.position;

        var point = spawnPoints[index % spawnPoints.Length];
        return point != null ? point.position : transform.position;
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        if (spawnPoints == null || spawnPoints.Length == 0)
        {
            Gizmos.DrawWireSphere(transform.position, 0.3f);
            return;
        }

        for (int i = 0; i < spawnPoints.Length; i++)
        {
            if (spawnPoints[i] == null)
                continue;
            Gizmos.DrawWireSphere(spawnPoints[i].position, 0.3f);
        }
    }
}
