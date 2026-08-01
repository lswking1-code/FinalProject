using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 波内一种敌人：预制体 + 数量 + 可选专用刷怪点。
/// </summary>
[System.Serializable]
public class EnemyWaveEntry
{
    [Tooltip("敌人预制体")]
    public GameObject enemyPrefab;
    [Tooltip("本条目刷出数量")]
    [Min(0)] public int count = 1;
    [Tooltip("本种敌人专用刷怪点；为空则回退到本波 spawnPoints，再空则用组件级点")]
    public Transform[] spawnPoints;
}

/// <summary>
/// 单波刷怪配置：可包含多种敌人，共享本波刷怪点与节奏。
/// </summary>
[System.Serializable]
public class EnemyWaveConfig
{
    [Tooltip("本波敌人列表（可多种）；按列表顺序依次刷出")]
    public EnemyWaveEntry[] enemies;

    [Tooltip("【兼容旧配置】单敌人预制体；enemies 为空时使用")]
    [HideInInspector] public GameObject enemyPrefab;
    [Tooltip("【兼容旧配置】单敌人数量；enemies 为空时使用")]
    [HideInInspector] public int count = 2;

    [Tooltip("本波默认刷怪点；条目未配专用点时使用，再空则用组件级 spawnPoints")]
    public Transform[] spawnPoints;

    [Tooltip("波内相邻两个敌人的间隔（秒）；0 表示本波瞬间刷完")]
    [Min(0f)] public float intraWaveInterval;

    [Tooltip("本波结束后等待（秒）；<=0 时回退组件级 spawnInterval")]
    public float delayAfterWave;

    [Tooltip("生成后血量倍率（作用于 Character.maxHealth / currentHealth）")]
    public float hpScale = 1f;

    [Tooltip("生成后移速倍率（作用于 Enemy.normalSpeed / chaseSpeed / currentSpeed）")]
    public float speedScale = 1f;
}

/// <summary>
/// 按波次列表刷怪：每波可指定多种敌人、数量、刷怪点与节奏，支持总刷怪上限。
/// 遭遇战：spawnOnStart=false，由 EncounterZone.OnEncounterStarted 调用 StartSpawning。
/// </summary>
public class EnemyGenerate : MonoBehaviour
{
    [Header("波次列表")]
    [Tooltip("按顺序刷怪；每波可配置多种敌人。无有效条目的波会跳过")]
    [SerializeField] EnemyWaveConfig[] waves;

    [Header("数量与间隔")]
    [Tooltip("总刷怪上限；0 表示不额外限制（实际总数 = 各波敌人数量之和）")]
    [SerializeField] int maxTotalSpawns;
    [Tooltip("相邻两波之间的默认等待时间（秒）；波的 delayAfterWave<=0 时使用此值")]
    [SerializeField] float spawnInterval = 3f;
    [Tooltip("开始刷怪前的首次延迟（秒）")]
    [SerializeField] float initialDelay;

    [Header("生成点")]
    [Tooltip("默认刷怪位置列表；波未配置专用点时使用。为空则在本物体位置生成")]
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
        // #region agent log
        AgentDebugLog.Write("A", "EnemyGenerate.cs:Start", "EnemyGenerate Start",
            "{\"name\":\"" + name + "\",\"spawnOnStart\":" + (spawnOnStart ? "true" : "false") + ",\"waveCount\":" + WaveCount + ",\"hasEncounterZone\":" + (encounterZone != null ? "true" : "false") + ",\"encounterZoneName\":\"" + (encounterZone != null ? encounterZone.name : "null") + "\"}");
        // #endregion
        if (spawnOnStart)
            StartSpawning();
    }

    void OnDisable() => StopSpawning();

    void OnValidate()
    {
        if (waves == null)
            return;

        for (int i = 0; i < waves.Length; i++)
        {
            var wave = waves[i];
            if (wave == null)
                continue;

            bool hasEnemies = false;
            if (wave.enemies != null)
            {
                for (int e = 0; e < wave.enemies.Length; e++)
                {
                    if (wave.enemies[e] != null && wave.enemies[e].enemyPrefab != null)
                    {
                        hasEnemies = true;
                        break;
                    }
                }
            }

            // 把旧版单 prefab/count 迁到 enemies，便于在 Inspector 中继续编辑
            if (!hasEnemies && wave.enemyPrefab != null && wave.count > 0)
            {
                wave.enemies = new[]
                {
                    new EnemyWaveEntry
                    {
                        enemyPrefab = wave.enemyPrefab,
                        count = wave.count
                    }
                };
            }
        }
    }

    public void StartSpawning()
    {
        // #region agent log
        AgentDebugLog.Write("A", "EnemyGenerate.cs:StartSpawning", "StartSpawning called",
            "{\"name\":\"" + name + "\",\"waveCount\":" + WaveCount + ",\"totalLimit\":" + GetEffectiveTotalLimit() + ",\"hasAnyValidWave\":" + (HasAnyValidWave() ? "true" : "false") + "}");
        // #endregion
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
            // #region agent log
            AgentDebugLog.Write("D", "EnemyGenerate.cs:SpawnRoutine", "early exit invalid waves",
                "{\"name\":\"" + name + "\",\"waveLen\":" + waveLen + ",\"totalLimit\":" + totalLimit + ",\"hasAnyValidWave\":" + (HasAnyValidWave() ? "true" : "false") + "}");
            // #endregion
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
                yield return WaitAfterWave(wave, waveIndex, waveLen);
                continue;
            }

            // #region agent log
            AgentDebugLog.Write("E", "EnemyGenerate.cs:SpawnRoutine", "spawning wave",
                "{\"name\":\"" + name + "\",\"waveIndex\":" + waveIndex + ",\"waveLen\":" + waveLen + ",\"remaining\":" + remaining + ",\"waveEnemyCount\":" + GetWaveTotalCount(wave) + "}");
            // #endregion
            yield return SpawnWaveRoutine(wave, remaining);
            OnWaveSpawned?.Invoke();

            if (totalSpawned >= totalLimit)
                break;

            yield return WaitAfterWave(wave, waveIndex, waveLen);
        }

        // #region agent log
        AgentDebugLog.Write("E", "EnemyGenerate.cs:SpawnRoutine", "spawning completed",
            "{\"name\":\"" + name + "\",\"totalSpawned\":" + totalSpawned + ",\"waveLen\":" + waveLen + "}");
        // #endregion
        spawnRoutine = null;
        OnSpawningCompleted?.Invoke();
    }

    IEnumerator SpawnWaveRoutine(EnemyWaveConfig wave, int remainingBudget)
    {
        List<EnemyWaveEntry> entries = ResolveEntries(wave);
        int spawnedInWave = 0;
        int totalToSpawn = 0;
        for (int i = 0; i < entries.Count; i++)
            totalToSpawn += entries[i].count;
        totalToSpawn = Mathf.Min(totalToSpawn, remainingBudget);

        for (int e = 0; e < entries.Count; e++)
        {
            var entry = entries[e];
            int countThisEntry = Mathf.Min(entry.count, remainingBudget - spawnedInWave);
            if (countThisEntry <= 0)
                break;

            for (int i = 0; i < countThisEntry; i++)
            {
                SpawnEnemyAt(entry.enemyPrefab, wave, GetSpawnPosition(wave, entry, i));
                spawnedInWave++;

                bool moreInWave = spawnedInWave < totalToSpawn;
                if (wave.intraWaveInterval > 0f && moreInWave)
                    yield return new WaitForSeconds(wave.intraWaveInterval);
            }
        }
    }

    IEnumerator WaitAfterWave(EnemyWaveConfig wave, int waveIndex, int waveLen)
    {
        if (waveIndex >= waveLen - 1)
            yield break;

        float delay = GetDelayAfterWave(wave);
        if (delay > 0f)
            yield return new WaitForSeconds(delay);
    }

    float GetDelayAfterWave(EnemyWaveConfig wave)
    {
        if (wave != null && wave.delayAfterWave > 0f)
            return wave.delayAfterWave;
        return spawnInterval;
    }

    void SpawnEnemyAt(GameObject prefab, EnemyWaveConfig wave, Vector3 position)
    {
        if (prefab == null)
        {
            // #region agent log
            AgentDebugLog.Write("D", "EnemyGenerate.cs:SpawnEnemyAt", "prefab null skip",
                "{\"name\":\"" + name + "\"}");
            // #endregion
            return;
        }

        var instance = Instantiate(prefab, position, Quaternion.identity);
        totalSpawned++;

        ApplyScales(instance, wave);

        if (encounterZone != null)
            encounterZone.RegisterEnemy(instance);

        // #region agent log
        AgentDebugLog.Write("E", "EnemyGenerate.cs:SpawnEnemyAt", "enemy spawned",
            "{\"name\":\"" + name + "\",\"prefab\":\"" + prefab.name + "\",\"totalSpawned\":" + totalSpawned + ",\"pos\":\"" + position.x.ToString("F1") + "," + position.y.ToString("F1") + "\"}");
        // #endregion
    }

    static void ApplyScales(GameObject instance, EnemyWaveConfig wave)
    {
        if (instance == null || wave == null)
            return;

        float hpScale = wave.hpScale;
        if (!Mathf.Approximately(hpScale, 1f) && hpScale > 0f)
        {
            var character = instance.GetComponent<Character>();
            if (character != null)
            {
                character.maxHealth *= hpScale;
                character.currentHealth = character.maxHealth;
            }
        }

        float speedScale = wave.speedScale;
        if (!Mathf.Approximately(speedScale, 1f) && speedScale > 0f)
        {
            var enemy = instance.GetComponent<Enemy>();
            if (enemy != null)
            {
                enemy.normalSpeed *= speedScale;
                enemy.chaseSpeed *= speedScale;
                enemy.currentSpeed *= speedScale;
            }
        }
    }

    /// <summary>
    /// 解析本波敌人条目；优先用 enemies，为空则回退旧版单 prefab/count。
    /// </summary>
    static List<EnemyWaveEntry> ResolveEntries(EnemyWaveConfig wave)
    {
        var result = new List<EnemyWaveEntry>();
        if (wave == null)
            return result;

        if (wave.enemies != null)
        {
            for (int i = 0; i < wave.enemies.Length; i++)
            {
                var entry = wave.enemies[i];
                if (entry != null && entry.enemyPrefab != null && entry.count > 0)
                    result.Add(entry);
            }
        }

        if (result.Count == 0 && wave.enemyPrefab != null && wave.count > 0)
        {
            result.Add(new EnemyWaveEntry
            {
                enemyPrefab = wave.enemyPrefab,
                count = wave.count
            });
        }

        return result;
    }

    static int GetWaveTotalCount(EnemyWaveConfig wave)
    {
        var entries = ResolveEntries(wave);
        int total = 0;
        for (int i = 0; i < entries.Count; i++)
            total += entries[i].count;
        return total;
    }

    static bool IsValidWave(EnemyWaveConfig wave)
    {
        return GetWaveTotalCount(wave) > 0;
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
            total += GetWaveTotalCount(waves[i]);

        return total;
    }

    int GetEffectiveTotalLimit()
    {
        int fromWaves = GetConfiguredTotalFromWaves();
        if (maxTotalSpawns <= 0)
            return fromWaves;

        return Mathf.Min(fromWaves, maxTotalSpawns);
    }

    Vector3 GetSpawnPosition(EnemyWaveConfig wave, EnemyWaveEntry entry, int indexInEntry)
    {
        Transform[] points = ResolveSpawnPoints(wave, entry);
        if (points == null || points.Length == 0)
            return transform.position;

        var point = points[indexInEntry % points.Length];
        return point != null ? point.position : transform.position;
    }

    /// <summary>
    /// 优先级：条目专用点 → 本波点 → 组件级点。
    /// </summary>
    Transform[] ResolveSpawnPoints(EnemyWaveConfig wave, EnemyWaveEntry entry)
    {
        if (entry != null && entry.spawnPoints != null && entry.spawnPoints.Length > 0)
            return entry.spawnPoints;

        if (wave != null && wave.spawnPoints != null && wave.spawnPoints.Length > 0)
            return wave.spawnPoints;

        if (spawnPoints != null && spawnPoints.Length > 0)
            return spawnPoints;

        return null;
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        if (spawnPoints == null || spawnPoints.Length == 0)
        {
            Gizmos.DrawWireSphere(transform.position, 0.3f);
        }
        else
        {
            for (int i = 0; i < spawnPoints.Length; i++)
            {
                if (spawnPoints[i] == null)
                    continue;
                Gizmos.DrawWireSphere(spawnPoints[i].position, 0.3f);
            }
        }

        if (waves == null)
            return;

        for (int w = 0; w < waves.Length; w++)
        {
            var wave = waves[w];
            if (wave == null)
                continue;

            Color waveColor = Color.HSVToRGB((w * 0.17f) % 1f, 0.75f, 1f);

            if (wave.spawnPoints != null)
            {
                Gizmos.color = waveColor;
                for (int i = 0; i < wave.spawnPoints.Length; i++)
                {
                    if (wave.spawnPoints[i] == null)
                        continue;
                    Gizmos.DrawWireSphere(wave.spawnPoints[i].position, 0.25f);
                }
            }

            if (wave.enemies == null)
                continue;

            for (int e = 0; e < wave.enemies.Length; e++)
            {
                var entry = wave.enemies[e];
                if (entry == null || entry.spawnPoints == null)
                    continue;

                Gizmos.color = Color.Lerp(waveColor, Color.white, 0.35f + (e * 0.1f) % 0.4f);
                for (int i = 0; i < entry.spawnPoints.Length; i++)
                {
                    if (entry.spawnPoints[i] == null)
                        continue;
                    Gizmos.DrawWireSphere(entry.spawnPoints[i].position, 0.2f);
                }
            }
        }
    }
}
