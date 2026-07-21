using System.Collections;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 单波刷怪配置：指定本波敌人种类与数量。
/// </summary>
[System.Serializable]
public class EnemyWaveConfig
{
    [Tooltip("本波刷出的敌人预制体")]
    public GameObject enemyPrefab;
    [Tooltip("本波刷出数量")]
    [Min(0)] public int count = 2;
}

/// <summary>
/// 按波次列表刷怪：每波可指定敌人种类与数量，支持总刷怪上限。
/// </summary>
public class EnemyGenerate : MonoBehaviour
{
    [Header("波次列表")]
    [Tooltip("按顺序刷怪；每波指定一种敌人与数量。prefab 为空或 count≤0 的波会跳过")]
    [SerializeField] EnemyWaveConfig[] waves;

    [Header("数量与间隔")]
    [Tooltip("总刷怪上限；0 表示不额外限制（实际总数 = 各波 count 之和）")]
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
    [Tooltip("每一波敌人生成完毕时触发（跳过的空波不触发）")]
    public UnityEvent OnWaveSpawned;
    [Tooltip("全部波次刷完（或达到总上限）时触发")]
    public UnityEvent OnSpawningCompleted;

    Coroutine spawnRoutine;
    int totalSpawned;

    public int WaveCount => waves != null ? waves.Length : 0;
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

        int waveLen = WaveCount;
        int totalLimit = GetEffectiveTotalLimit();

        if (waveLen <= 0 || totalLimit <= 0 || !HasAnyValidWave())
        {
            if (waveLen <= 0 || !HasAnyValidWave())
                Debug.LogWarning("EnemyGenerate: waves 未配置或全部无效。", this);

            spawnRoutine = null;
            OnSpawningCompleted?.Invoke();
            yield break;
        }

        for (int waveIndex = 0; waveIndex < waveLen; waveIndex++)
        {
            int remaining = totalLimit - totalSpawned;
            if (remaining <= 0)
                break;

            var wave = waves[waveIndex];
            if (!IsValidWave(wave))
            {
                if (waveIndex < waveLen - 1 && spawnInterval > 0f)
                    yield return new WaitForSeconds(spawnInterval);
                continue;
            }

            int countThisWave = Mathf.Min(wave.count, remaining);
            if (countThisWave <= 0)
                break;

            SpawnWave(wave, countThisWave);
            OnWaveSpawned?.Invoke();

            if (totalSpawned >= totalLimit)
                break;

            if (waveIndex < waveLen - 1 && spawnInterval > 0f)
                yield return new WaitForSeconds(spawnInterval);
        }

        spawnRoutine = null;
        OnSpawningCompleted?.Invoke();
    }

    void SpawnWave(EnemyWaveConfig wave, int count)
    {
        for (int i = 0; i < count; i++)
            SpawnEnemyAt(wave.enemyPrefab, GetSpawnPosition(i));
    }

    void SpawnEnemyAt(GameObject prefab, Vector3 position)
    {
        if (prefab == null)
            return;

        var instance = Instantiate(prefab, position, Quaternion.identity);
        totalSpawned++;

        if (encounterZone != null)
            encounterZone.RegisterEnemy(instance);
    }

    static bool IsValidWave(EnemyWaveConfig wave)
    {
        return wave != null && wave.enemyPrefab != null && wave.count > 0;
    }

    bool HasAnyValidWave()
    {
        if (waves == null)
            return false;

        for (int i = 0; i < waves.Length; i++)
        {
            if (IsValidWave(waves[i]))
                return true;
        }

        return false;
    }

    int GetConfiguredTotalFromWaves()
    {
        if (waves == null)
            return 0;

        int total = 0;
        for (int i = 0; i < waves.Length; i++)
        {
            if (IsValidWave(waves[i]))
                total += waves[i].count;
        }

        return total;
    }

    int GetEffectiveTotalLimit()
    {
        int fromWaves = GetConfiguredTotalFromWaves();
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
