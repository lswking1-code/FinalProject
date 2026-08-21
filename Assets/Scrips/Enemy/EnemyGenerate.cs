using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 单个生成实例的死亡掉落：弹药与血包可同时开启。
/// </summary>
[System.Serializable]
public class EnemyInstanceDropConfig
{
    [Tooltip("该实例死亡时是否掉落弹药包")]
    public bool dropAmmoOnDeath;
    [Tooltip("掉落弹药类型，对应 BulletBoxS / M / L")]
    public AmmoType ammoType = AmmoType.S;
    [Tooltip("该实例死亡时是否掉落血包")]
    public bool dropHealthOnDeath;
}

/// <summary>
/// 波内一种敌人：预制体 + 数量 + 可选专用刷怪点。
/// waitUntilBatchCleared：有限条目本批清光后再刷同波下一类（不循环）。
/// unlockEncounterOnCleared：有限条目本批清光后只解开遭遇锁区，不结束遭遇。
/// infiniteRefresh 已自带等本批清光，再勾选 waitUntilBatchCleared / unlockEncounterOnCleared 无效。
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
    [Tooltip("覆盖预制体上的专注模式。盾兵有盾原地举盾；枪兵/火箭兵不靠近玩家，MOVE 时原地停留。近战/飞行/装甲车无效")]
    public bool enableFocusMode;
    [Tooltip("开启后，生成后先走到目标点，再进入战斗/专注模式")]
    public bool enableTargetPoint;
    [Tooltip("目标点；为空则使用本条目该实例的刷怪位置（脚下）")]
    public Transform targetPoint;
    [Tooltip("勾选后：本批清光再刷下一批，直至遭遇结束/StopSpawning；不计入遭遇清敌结算与 maxTotalSpawns")]
    public bool infiniteRefresh;
    [Tooltip("有限条目：本批清光后再刷同波下一类，不循环。无限刷新条目本身就会等本批清光，此选项无效")]
    public bool waitUntilBatchCleared;
    [Tooltip("有限条目本批清光后只解开遭遇锁区（空气墙/镜头），不结束遭遇、不停刷。无限刷新条目无效")]
    public bool unlockEncounterOnCleared;
    [Tooltip("每个生成实例的弹药/血包掉落；长度随 count 自动对齐。Element 0 对应本条目第 1 个刷出的敌人")]
    public EnemyInstanceDropConfig[] drops;
}

/// <summary>
/// 单波刷怪配置：可包含多种敌人，共享本波刷怪点与节奏。
/// waitUntilCleared：本波有限敌人全灭后再进入波间等待/下一波。
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

    [Tooltip("本波结束后等待（秒）；<=0 时回退组件级 spawnInterval。无限刷新条目在两批之间也用此间隔")]
    public float delayAfterWave;

    [Tooltip("本波有限敌人全灭后，再执行 delayAfterWave / spawnInterval 并刷下一波。不含同波无限刷新敌人")]
    public bool waitUntilCleared;

    [Tooltip("生成后血量倍率（作用于 Character.maxHealth / currentHealth）")]
    public float hpScale = 1f;

    [Tooltip("生成后移速倍率（作用于 Enemy.normalSpeed / chaseSpeed / currentSpeed）")]
    public float speedScale = 1f;
}

/// <summary>
/// 按波次列表刷怪：每波可指定多种敌人、数量、刷怪点与节奏，支持总刷怪上限。
/// 遭遇战：spawnOnStart=false，由 EncounterZone.OnEncounterStarted 调用 StartSpawning；
/// OnEncounterEnded 调用 StopSpawning。条目勾选 infiniteRefresh 则循环刷且不登记遭遇结算。
/// 有限波可勾选 waitUntilCleared（整波清光再下一波）与条目 waitUntilBatchCleared（本批清光再刷同波下一类）。
/// 条目勾选 unlockEncounterOnCleared 则该批清光后只解锁锁区，不停刷、不结束遭遇。
/// </summary>
public class EnemyGenerate : MonoBehaviour
{
    [Header("波次列表")]
    [Tooltip("按顺序刷怪；每波可配置多种敌人。无有效条目的波会跳过。无限条目由独立协程处理")]
    [SerializeField] EnemyWaveConfig[] waves;

    [Header("数量与间隔")]
    [Tooltip("有限刷怪总上限；0 表示不额外限制（实际总数 = 各波有限敌人数量之和）。不含无限条目")]
    [SerializeField] int maxTotalSpawns;
    [Tooltip("相邻两波之间的默认等待时间（秒）；波的 delayAfterWave<=0 时使用此值")]
    [SerializeField] float spawnInterval = 3f;
    [Tooltip("开始刷怪前的首次延迟（秒）")]
    [SerializeField] float initialDelay;

    [Header("生成点")]
    [Tooltip("默认刷怪位置列表；波未配置专用点时使用。为空则在本物体位置生成。" +
             "遭遇战建议放在空气墙外侧/相机视野外：Enemy 可穿空气墙进入区内，玩家仍被挡住")]
    [SerializeField] Transform[] spawnPoints;

    [Header("出生错开")]
    [Tooltip("同一刷怪点多个实例的水平间距；0 表示不错开")]
    [SerializeField] float spawnSpreadSpacing = 0.4f;
    [Tooltip("飞行敌人额外的竖直错开")]
    [SerializeField] float spawnSpreadYForFlying = 0.25f;
    [Tooltip("错开后若与该层重叠则回退到原点；留空则使用 Ground")]
    [SerializeField] LayerMask spawnOverlapMask;

    [Header("掉落预制体")]
    [Tooltip("S 弹药包预制体（BulletBoxS）")]
    [SerializeField] GameObject ammoDropPrefabS;
    [Tooltip("M 弹药包预制体（BulletBoxM）")]
    [SerializeField] GameObject ammoDropPrefabM;
    [Tooltip("L 弹药包预制体（BulletBoxL）")]
    [SerializeField] GameObject ammoDropPrefabL;
    [Tooltip("血包预制体（HealthPack）")]
    [SerializeField] GameObject healthDropPrefab;

    [Header("编辑器显示")]
    [Tooltip("在 Scene 视图中始终绘制刷怪点")]
    [SerializeField] bool alwaysDrawSpawnPoints = true;

    [Header("遭遇战（可选）")]
    [Tooltip("若指定，有限生成的敌人会 RegisterEnemy 到该遭遇区。无限刷新敌人不登记")]
    [SerializeField] EncounterZone encounterZone;

    [Header("启动")]
    [Tooltip("勾选后在 Start 时自动开始刷怪；遭遇战场景可关掉，改由事件调用 StartSpawning")]
    [SerializeField] bool spawnOnStart = true;

    [Header("事件")]
    [Tooltip("每一波有限生成流程结束时触发（含本波勾选的清光等待；跳过的空波不触发）")]
    public UnityEvent OnWaveSpawned;
    [Tooltip("全部有限波次刷完（或达到总上限）时触发；无限循环不停该事件")]
    public UnityEvent OnSpawningCompleted;

    Coroutine spawnRoutine;
    readonly List<Coroutine> infiniteRoutines = new();
    readonly List<Coroutine> unlockLockRoutines = new();
    int totalSpawned;
    int finiteSpawned;
    bool reportedSpawningToZone;
    bool isSpawningActive;

    public int WaveCount => waves != null ? waves.Length : 0;
    public int TotalSpawned => totalSpawned;
    public int MaxTotalSpawns => GetEffectiveTotalLimit();
    public bool IsSpawning => isSpawningActive;

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

            if (wave.enemies == null)
                continue;

            for (int e = 0; e < wave.enemies.Length; e++)
                SyncEntryDrops(wave.enemies[e]);
        }
    }

    static void SyncEntryDrops(EnemyWaveEntry entry)
    {
        if (entry == null)
            return;

        int targetCount = Mathf.Max(0, entry.count);
        if (entry.drops == null)
        {
            entry.drops = new EnemyInstanceDropConfig[targetCount];
            for (int i = 0; i < targetCount; i++)
                entry.drops[i] = new EnemyInstanceDropConfig();
            return;
        }

        if (entry.drops.Length == targetCount)
        {
            for (int i = 0; i < entry.drops.Length; i++)
            {
                if (entry.drops[i] == null)
                    entry.drops[i] = new EnemyInstanceDropConfig();
            }
            return;
        }

        var resized = new EnemyInstanceDropConfig[targetCount];
        int copyCount = Mathf.Min(entry.drops.Length, targetCount);
        for (int i = 0; i < copyCount; i++)
            resized[i] = entry.drops[i] ?? new EnemyInstanceDropConfig();
        for (int i = copyCount; i < targetCount; i++)
            resized[i] = new EnemyInstanceDropConfig();
        entry.drops = resized;
    }

    public void StartSpawning()
    {
        // #region agent log
        AgentDebugLog.Write("A", "EnemyGenerate.cs:StartSpawning", "StartSpawning called",
            "{\"name\":\"" + name + "\",\"waveCount\":" + WaveCount + ",\"totalLimit\":" + GetEffectiveTotalLimit() + ",\"hasAnyValidWave\":" + (HasAnyValidWave() ? "true" : "false") + ",\"hasFinite\":" + (HasAnyFiniteValidWave() ? "true" : "false") + "}");
        // #endregion
        // 重启时不要先 NotifyCompleted，否则波间空窗会误触发遭遇区解锁
        StopSpawningInternal(releaseZone: false);
        totalSpawned = 0;
        finiteSpawned = 0;
        isSpawningActive = true;

        if (HasAnyFiniteValidWave())
            NotifyZoneSpawningStarted();

        StartInfiniteRoutines();

        if (HasAnyFiniteValidWave())
            spawnRoutine = StartCoroutine(SpawnRoutine());
        else if (!HasAnyInfiniteEntry())
        {
            Debug.LogWarning("EnemyGenerate: waves 未配置或全部无效。", this);
            FinishFiniteSpawning();
        }
    }

    public void StopSpawning() => StopSpawningInternal(releaseZone: true);

    void StopSpawningInternal(bool releaseZone)
    {
        isSpawningActive = false;

        if (spawnRoutine != null)
        {
            StopCoroutine(spawnRoutine);
            spawnRoutine = null;
        }

        StopInfiniteRoutines();
        StopUnlockLockRoutines();

        if (releaseZone)
            NotifyZoneSpawningCompleted();
    }

    void StartInfiniteRoutines()
    {
        if (waves == null)
            return;

        for (int w = 0; w < waves.Length; w++)
        {
            var wave = waves[w];
            if (wave == null)
                continue;

            var infiniteEntries = ResolveEntries(wave, infiniteOnly: true);
            for (int e = 0; e < infiniteEntries.Count; e++)
            {
                var entry = infiniteEntries[e];
                if (entry == null || entry.enemyPrefab == null || entry.count <= 0)
                    continue;
                infiniteRoutines.Add(StartCoroutine(InfiniteEntryRoutine(wave, entry)));
            }
        }
    }

    void StopInfiniteRoutines()
    {
        for (int i = 0; i < infiniteRoutines.Count; i++)
        {
            if (infiniteRoutines[i] != null)
                StopCoroutine(infiniteRoutines[i]);
        }

        infiniteRoutines.Clear();
    }

    void StopUnlockLockRoutines()
    {
        for (int i = 0; i < unlockLockRoutines.Count; i++)
        {
            if (unlockLockRoutines[i] != null)
                StopCoroutine(unlockLockRoutines[i]);
        }

        unlockLockRoutines.Clear();
    }

    void NotifyZoneSpawningStarted()
    {
        if (encounterZone == null || reportedSpawningToZone)
            return;

        reportedSpawningToZone = true;
        encounterZone.NotifySpawningStarted();
    }

    void NotifyZoneSpawningCompleted()
    {
        if (!reportedSpawningToZone || encounterZone == null)
            return;

        reportedSpawningToZone = false;
        encounterZone.NotifySpawningCompleted();
    }

    void FinishFiniteSpawning()
    {
        spawnRoutine = null;
        NotifyZoneSpawningCompleted();
        OnSpawningCompleted?.Invoke();

        if (!HasAnyInfiniteEntryRunning())
            isSpawningActive = false;
    }

    bool HasAnyInfiniteEntryRunning() => infiniteRoutines.Count > 0 && isSpawningActive;

    IEnumerator SpawnRoutine()
    {
        if (initialDelay > 0f)
            yield return new WaitForSeconds(initialDelay);

        if (!isSpawningActive)
            yield break;

        int waveLen = WaveCount;
        int totalLimit = GetEffectiveTotalLimit();

        if (waveLen <= 0 || totalLimit <= 0 || !HasAnyFiniteValidWave())
        {
            // #region agent log
            AgentDebugLog.Write("D", "EnemyGenerate.cs:SpawnRoutine", "early exit invalid finite waves",
                "{\"name\":\"" + name + "\",\"waveLen\":" + waveLen + ",\"totalLimit\":" + totalLimit + ",\"hasFinite\":" + (HasAnyFiniteValidWave() ? "true" : "false") + "}");
            // #endregion
            if (waveLen <= 0 || !HasAnyValidWave())
                Debug.LogWarning("EnemyGenerate: waves 未配置或全部无效。", this);

            FinishFiniteSpawning();
            yield break;
        }

        for (int waveIndex = 0; waveIndex < waveLen; waveIndex++)
        {
            if (!isSpawningActive)
                yield break;

            int remaining = totalLimit - finiteSpawned;
            if (remaining <= 0)
                break;

            var wave = waves[waveIndex];
            if (!IsValidFiniteWave(wave))
            {
                yield return WaitAfterWave(wave, waveIndex, waveLen);
                continue;
            }

            // #region agent log
            AgentDebugLog.Write("E", "EnemyGenerate.cs:SpawnRoutine", "spawning wave",
                "{\"name\":\"" + name + "\",\"waveIndex\":" + waveIndex + ",\"waveLen\":" + waveLen + ",\"remaining\":" + remaining + ",\"waveEnemyCount\":" + GetWaveFiniteCount(wave) + "}");
            // #endregion
            yield return SpawnWaveRoutine(wave, remaining);
            if (!isSpawningActive)
                yield break;

            OnWaveSpawned?.Invoke();

            if (finiteSpawned >= totalLimit)
                break;

            yield return WaitAfterWave(wave, waveIndex, waveLen);
        }

        // #region agent log
        AgentDebugLog.Write("E", "EnemyGenerate.cs:SpawnRoutine", "spawning completed",
            "{\"name\":\"" + name + "\",\"finiteSpawned\":" + finiteSpawned + ",\"totalSpawned\":" + totalSpawned + ",\"waveLen\":" + waveLen + "}");
        // #endregion
        FinishFiniteSpawning();
    }

    IEnumerator InfiniteEntryRoutine(EnemyWaveConfig wave, EnemyWaveEntry entry)
    {
        if (initialDelay > 0f)
            yield return new WaitForSeconds(initialDelay);

        while (isSpawningActive)
        {
            var aliveBatch = new HashSet<Character>();
            int spawnedThisBatch = 0;

            for (int i = 0; i < entry.count; i++)
            {
                if (!isSpawningActive)
                    yield break;

                var instance = SpawnEnemyAt(
                    entry.enemyPrefab,
                    wave,
                    GetSpawnPosition(wave, entry, i),
                    registerWithZone: false,
                    countTowardFiniteBudget: false,
                    entry,
                    i);

                if (instance != null)
                {
                    spawnedThisBatch++;
                    TrackBatchEnemy(instance, aliveBatch);
                }

                bool moreInBatch = i < entry.count - 1;
                if (wave.intraWaveInterval > 0f && moreInBatch && isSpawningActive)
                    yield return new WaitForSeconds(wave.intraWaveInterval);
            }

            if (spawnedThisBatch <= 0)
            {
                Debug.LogWarning("EnemyGenerate: 无限刷新条目无法生成敌人，已停止该循环。", this);
                yield break;
            }

            yield return WaitUntilBatchCleared(aliveBatch);
            if (!isSpawningActive)
                yield break;

            float delay = GetDelayAfterWave(wave);
            if (delay > 0f)
                yield return new WaitForSeconds(delay);
        }
    }

    void TrackBatchEnemy(GameObject instance, HashSet<Character> aliveBatch)
    {
        if (instance == null || aliveBatch == null)
            return;

        var character = instance.GetComponent<Character>();
        if (character == null)
            character = instance.GetComponentInChildren<Character>();
        if (character == null)
            character = instance.GetComponentInParent<Character>();
        if (character == null || character.IsDead)
            return;

        var tracker = instance.GetComponent<BatchAliveTracker>();
        if (tracker == null)
            tracker = instance.AddComponent<BatchAliveTracker>();
        tracker.AddToBatch(aliveBatch, character);
    }

    IEnumerator WaitUntilBatchCleared(HashSet<Character> aliveBatch)
    {
        if (aliveBatch == null)
            yield break;

        while (isSpawningActive && aliveBatch.Count > 0)
        {
            aliveBatch.RemoveWhere(c => c == null || c.IsDead);
            if (aliveBatch.Count == 0)
                break;
            yield return null;
        }
    }

    IEnumerator SpawnWaveRoutine(EnemyWaveConfig wave, int remainingBudget)
    {
        List<EnemyWaveEntry> entries = ResolveEntries(wave, infiniteOnly: false);
        var waveAlive = new HashSet<Character>();
        int spawnedInWave = 0;
        int totalToSpawn = 0;
        for (int i = 0; i < entries.Count; i++)
            totalToSpawn += entries[i].count;
        totalToSpawn = Mathf.Min(totalToSpawn, remainingBudget);

        for (int e = 0; e < entries.Count; e++)
        {
            if (!isSpawningActive)
                yield break;

            var entry = entries[e];
            int countThisEntry = Mathf.Min(entry.count, remainingBudget - spawnedInWave);
            if (countThisEntry <= 0)
                break;

            var entryAlive = new HashSet<Character>();
            int spawnedThisEntry = 0;

            for (int i = 0; i < countThisEntry; i++)
            {
                if (!isSpawningActive)
                    yield break;

                var instance = SpawnEnemyAt(
                    entry.enemyPrefab,
                    wave,
                    GetSpawnPosition(wave, entry, i),
                    registerWithZone: true,
                    countTowardFiniteBudget: true,
                    entry,
                    i);
                spawnedInWave++;

                if (instance != null)
                {
                    spawnedThisEntry++;
                    TrackBatchEnemy(instance, entryAlive);
                    TrackBatchEnemy(instance, waveAlive);
                }

                bool moreInWave = spawnedInWave < totalToSpawn;
                if (wave.intraWaveInterval > 0f && moreInWave)
                    yield return new WaitForSeconds(wave.intraWaveInterval);
            }

            if (entry.unlockEncounterOnCleared && spawnedThisEntry > 0 && encounterZone != null)
                unlockLockRoutines.Add(StartCoroutine(UnlockLockWhenBatchCleared(entryAlive)));

            if (entry.waitUntilBatchCleared && spawnedThisEntry > 0)
            {
                yield return WaitUntilBatchCleared(entryAlive);
                if (!isSpawningActive)
                    yield break;
            }
        }

        if (wave != null && wave.waitUntilCleared && waveAlive.Count > 0)
            yield return WaitUntilBatchCleared(waveAlive);
    }

    IEnumerator UnlockLockWhenBatchCleared(HashSet<Character> aliveBatch)
    {
        if (aliveBatch == null)
            yield break;

        // 不依赖 isSpawningActive：有限波刷完后仍要等本批死完才能开墙。
        // StopSpawning 会停掉本协程，避免遭遇结束后再 Unlock。
        while (aliveBatch.Count > 0)
        {
            if (encounterZone == null || !encounterZone.IsActive)
                yield break;

            aliveBatch.RemoveWhere(c => c == null || c.IsDead);
            if (aliveBatch.Count == 0)
                break;
            yield return null;
        }

        if (encounterZone == null || !encounterZone.IsActive)
            yield break;

        encounterZone.UnlockLock();
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

    GameObject SpawnEnemyAt(
        GameObject prefab,
        EnemyWaveConfig wave,
        Vector3 position,
        bool registerWithZone,
        bool countTowardFiniteBudget,
        EnemyWaveEntry entry,
        int indexInEntry)
    {
        if (prefab == null)
        {
            // #region agent log
            AgentDebugLog.Write("D", "EnemyGenerate.cs:SpawnEnemyAt", "prefab null skip",
                "{\"name\":\"" + name + "\"}");
            // #endregion
            return null;
        }

        position = ApplySpawnSpread(position, prefab, wave, entry, indexInEntry);
        var instance = Instantiate(prefab, position, Quaternion.identity);
        EnemySceneCleanup.PlaceInSourceScene(instance, this);
        var spawnedEnemy = instance.GetComponent<Enemy>() ?? instance.GetComponentInChildren<Enemy>();
        spawnedEnemy?.MarkAsRuntimeSpawned();
        totalSpawned++;
        if (countTowardFiniteBudget)
            finiteSpawned++;

        ApplyScales(instance, wave);
        ApplyDrops(instance, entry, indexInEntry);
        ApplyEncounterBehavior(instance, entry, position);

        if (encounterZone != null)
        {
            if (registerWithZone)
                encounterZone.RegisterEnemy(instance);
            encounterZone.PrepareSpawnedEnemy(instance);
        }

        OneWayAirWallVolume.PrepareSpawnedEnemyAll(instance);

        // #region agent log
        AgentDebugLog.Write("E", "EnemyGenerate.cs:SpawnEnemyAt", "enemy spawned",
            "{\"name\":\"" + name + "\",\"prefab\":\"" + prefab.name + "\",\"totalSpawned\":" + totalSpawned + ",\"finiteSpawned\":" + finiteSpawned + ",\"register\":" + (registerWithZone ? "true" : "false") + ",\"pos\":\"" + position.x.ToString("F1") + "," + position.y.ToString("F1") + "\"}");
        // #endregion

        return instance;
    }

    void ApplyDrops(GameObject instance, EnemyWaveEntry entry, int indexInEntry)
    {
        if (instance == null)
            return;

        var enemy = instance.GetComponent<Enemy>() ?? instance.GetComponentInChildren<Enemy>();
        if (enemy == null)
            return;

        bool dropAmmo = false;
        bool dropHealth = false;
        AmmoType ammoType = AmmoType.S;

        if (entry != null && entry.drops != null
            && indexInEntry >= 0 && indexInEntry < entry.drops.Length
            && entry.drops[indexInEntry] != null)
        {
            var cfg = entry.drops[indexInEntry];
            dropAmmo = cfg.dropAmmoOnDeath;
            dropHealth = cfg.dropHealthOnDeath;
            ammoType = cfg.ammoType;
        }

        GameObject ammoPrefab = null;
        if (dropAmmo)
        {
            ammoPrefab = ResolveAmmoDropPrefab(ammoType);
            if (ammoPrefab == null)
            {
                Debug.LogWarning(
                    $"EnemyGenerate: 已勾选弹药掉落但未配置 {ammoType} 弹药包预制体。", this);
                dropAmmo = false;
            }
        }

        GameObject healthPrefab = null;
        if (dropHealth)
        {
            healthPrefab = healthDropPrefab;
            if (healthPrefab == null)
            {
                Debug.LogWarning("EnemyGenerate: 已勾选血包掉落但未配置 HealthPack 预制体。", this);
                dropHealth = false;
            }
        }

        enemy.ApplyDropOverride(dropAmmo, ammoPrefab, dropHealth, healthPrefab);
    }

    void ApplyEncounterBehavior(GameObject instance, EnemyWaveEntry entry, Vector3 spawnPosition)
    {
        if (instance == null || entry == null)
            return;

        var enemy = instance.GetComponent<Enemy>() ?? instance.GetComponentInChildren<Enemy>();
        if (enemy == null)
            return;

        enemy.ApplyEncounterFocusMode(entry.enableFocusMode);

        if (entry.enableTargetPoint)
        {
            Vector3 target = entry.targetPoint != null ? entry.targetPoint.position : spawnPosition;
            enemy.BeginSpawnApproach(target);
            return;
        }

        enemy.EnterPostSpawnBehavior();
    }

    GameObject ResolveAmmoDropPrefab(AmmoType type) => type switch
    {
        AmmoType.S => ammoDropPrefabS,
        AmmoType.M => ammoDropPrefabM,
        AmmoType.L => ammoDropPrefabL,
        _ => null
    };

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
    /// infiniteOnly=true 时仅无限条目；false 时仅有限条目。
    /// </summary>
    static List<EnemyWaveEntry> ResolveEntries(EnemyWaveConfig wave, bool infiniteOnly)
    {
        var result = new List<EnemyWaveEntry>();
        if (wave == null)
            return result;

        if (wave.enemies != null)
        {
            for (int i = 0; i < wave.enemies.Length; i++)
            {
                var entry = wave.enemies[i];
                if (entry == null || entry.enemyPrefab == null || entry.count <= 0)
                    continue;
                if (entry.infiniteRefresh != infiniteOnly)
                    continue;
                result.Add(entry);
            }
        }

        if (result.Count == 0 && !infiniteOnly && wave.enemyPrefab != null && wave.count > 0)
        {
            result.Add(new EnemyWaveEntry
            {
                enemyPrefab = wave.enemyPrefab,
                count = wave.count,
                infiniteRefresh = false
            });
        }

        return result;
    }

    static int GetWaveFiniteCount(EnemyWaveConfig wave)
    {
        var entries = ResolveEntries(wave, infiniteOnly: false);
        int total = 0;
        for (int i = 0; i < entries.Count; i++)
            total += entries[i].count;
        return total;
    }

    static int GetWaveTotalCount(EnemyWaveConfig wave)
    {
        if (wave == null)
            return 0;

        int total = 0;
        if (wave.enemies != null)
        {
            for (int i = 0; i < wave.enemies.Length; i++)
            {
                var entry = wave.enemies[i];
                if (entry != null && entry.enemyPrefab != null && entry.count > 0)
                    total += entry.count;
            }
        }

        if (total == 0 && wave.enemyPrefab != null && wave.count > 0)
            total = wave.count;

        return total;
    }

    static bool IsValidFiniteWave(EnemyWaveConfig wave) => GetWaveFiniteCount(wave) > 0;

    static bool IsValidWave(EnemyWaveConfig wave) => GetWaveTotalCount(wave) > 0;

    bool HasAnyFiniteValidWave()
    {
        if (waves == null)
            return false;

        for (int i = 0; i < waves.Length; i++)
        {
            if (IsValidFiniteWave(waves[i]))
                return true;
        }

        return false;
    }

    bool HasAnyInfiniteEntry()
    {
        if (waves == null)
            return false;

        for (int w = 0; w < waves.Length; w++)
        {
            if (ResolveEntries(waves[w], infiniteOnly: true).Count > 0)
                return true;
        }

        return false;
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

    int GetConfiguredFiniteTotalFromWaves()
    {
        if (waves == null)
            return 0;

        int total = 0;
        for (int i = 0; i < waves.Length; i++)
            total += GetWaveFiniteCount(waves[i]);

        return total;
    }

    int GetEffectiveTotalLimit()
    {
        int fromWaves = GetConfiguredFiniteTotalFromWaves();
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

    Vector3 ApplySpawnSpread(
        Vector3 pos,
        GameObject prefab,
        EnemyWaveConfig wave,
        EnemyWaveEntry entry,
        int indexInEntry)
    {
        if (spawnSpreadSpacing <= 0f)
            return pos;

        int count = entry != null ? Mathf.Max(1, entry.count) : 1;
        Transform[] points = ResolveSpawnPoints(wave, entry);
        int pointCount = points != null && points.Length > 0 ? points.Length : 1;
        int pointIndex = indexInEntry % pointCount;
        int localIndex = indexInEntry / pointCount;
        int sharing = 0;
        for (int i = 0; i < count; i++)
        {
            if (i % pointCount == pointIndex)
                sharing++;
        }

        if (sharing <= 1)
            return pos;

        float centered = localIndex - (sharing - 1) * 0.5f;
        Vector3 candidate = pos;
        candidate.x += centered * spawnSpreadSpacing;

        bool flying = prefab != null && prefab.GetComponentInChildren<FlyingEnemy>(true) != null;
        if (flying && spawnSpreadYForFlying != 0f)
            candidate.y += centered * spawnSpreadYForFlying;

        if (IsSpawnBlocked(candidate))
            return pos;

        return candidate;
    }

    bool IsSpawnBlocked(Vector3 pos)
    {
        LayerMask mask = spawnOverlapMask;
        if (mask.value == 0)
            mask = LayerMask.GetMask("Ground");
        return Physics2D.OverlapCircle(pos, 0.35f, mask) != null;
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

    void OnDrawGizmos()
    {
        if (!alwaysDrawSpawnPoints)
            return;

        // 组件级默认点（青绿）
        if (spawnPoints == null || spawnPoints.Length == 0)
        {
            DrawSpawnMarker(transform.position, new Color(0.2f, 0.95f, 0.85f, 1f), 0.28f, name);
        }
        else
        {
            for (int i = 0; i < spawnPoints.Length; i++)
            {
                if (spawnPoints[i] == null)
                    continue;
                DrawSpawnMarker(
                    spawnPoints[i].position,
                    new Color(0.2f, 0.95f, 0.85f, 1f),
                    0.28f,
                    spawnPoints[i].name);
            }
        }

        if (waves != null)
        {
            for (int w = 0; w < waves.Length; w++)
            {
                var wave = waves[w];
                if (wave == null)
                    continue;

                Color waveColor = Color.HSVToRGB((w * 0.17f) % 1f, 0.85f, 1f);
                waveColor.a = 1f;

                if (wave.spawnPoints != null)
                {
                    for (int i = 0; i < wave.spawnPoints.Length; i++)
                    {
                        if (wave.spawnPoints[i] == null)
                            continue;
                        DrawSpawnMarker(wave.spawnPoints[i].position, waveColor, 0.24f, wave.spawnPoints[i].name);
                    }
                }

                if (wave.enemies == null)
                    continue;

                for (int e = 0; e < wave.enemies.Length; e++)
                {
                    var entry = wave.enemies[e];
                    if (entry == null || entry.spawnPoints == null)
                        continue;

                    Color entryColor = entry.infiniteRefresh
                        ? new Color(1f, 0.55f, 0.15f, 1f)
                        : Color.Lerp(waveColor, Color.white, 0.35f + (e * 0.1f) % 0.4f);
                    for (int i = 0; i < entry.spawnPoints.Length; i++)
                    {
                        if (entry.spawnPoints[i] == null)
                            continue;
                        DrawSpawnMarker(entry.spawnPoints[i].position, entryColor, 0.2f, entry.spawnPoints[i].name);
                    }
                }
            }
        }

        // 子物体刷怪点：即使还没挂到 spawnPoints（如 Stage2 的 Point3）也画出位置
        DrawUnassignedChildSpawnPoints();
        DrawTargetPoints();
    }

    void DrawTargetPoints()
    {
        if (waves == null)
            return;

        Color targetColor = new Color(0.95f, 0.25f, 0.75f, 1f);

        for (int w = 0; w < waves.Length; w++)
        {
            var wave = waves[w];
            if (wave?.enemies == null)
                continue;

            for (int e = 0; e < wave.enemies.Length; e++)
            {
                var entry = wave.enemies[e];
                if (entry == null || !entry.enableTargetPoint)
                    continue;

                Transform[] points = ResolveSpawnPoints(wave, entry);
                Vector3 fallback = transform.position;
                if (points != null)
                {
                    for (int i = 0; i < points.Length; i++)
                    {
                        if (points[i] != null)
                        {
                            fallback = points[i].position;
                            break;
                        }
                    }
                }

                if (entry.targetPoint != null)
                {
                    Vector3 targetPos = entry.targetPoint.position;
                    DrawSpawnMarker(targetPos, targetColor, 0.22f, entry.targetPoint.name);

                    Gizmos.color = targetColor;
                    if (points == null || points.Length == 0)
                    {
                        Gizmos.DrawLine(fallback, targetPos);
                    }
                    else
                    {
                        for (int i = 0; i < points.Length; i++)
                        {
                            if (points[i] == null)
                                continue;
                            Gizmos.DrawLine(points[i].position, targetPos);
                        }
                    }
                }
                else if (points == null || points.Length == 0)
                {
                    DrawSpawnMarker(fallback, targetColor, 0.18f, "脚下");
                }
                else
                {
                    for (int i = 0; i < points.Length; i++)
                    {
                        if (points[i] == null)
                            continue;
                        DrawSpawnMarker(points[i].position, targetColor, 0.18f, "脚下");
                    }
                }
            }
        }
    }

    void DrawUnassignedChildSpawnPoints()
    {
        for (int i = 0; i < transform.childCount; i++)
        {
            Transform child = transform.GetChild(i);
            if (child == null || IsAssignedSpawnPoint(child))
                continue;
            // Slot 点由 EncounterZone 的编辑器绘制负责
            if (child.name == "Slots")
                continue;

            DrawSpawnMarker(child.position, new Color(1f, 0.85f, 0.2f, 1f), 0.28f, child.name);
        }
    }

    bool IsAssignedSpawnPoint(Transform point)
    {
        if (ContainsSpawnPoint(spawnPoints, point))
            return true;

        if (waves == null)
            return false;

        for (int w = 0; w < waves.Length; w++)
        {
            var wave = waves[w];
            if (wave == null)
                continue;
            if (ContainsSpawnPoint(wave.spawnPoints, point))
                return true;
            if (wave.enemies == null)
                continue;
            for (int e = 0; e < wave.enemies.Length; e++)
            {
                var entry = wave.enemies[e];
                if (entry == null)
                    continue;
                if (ContainsSpawnPoint(entry.spawnPoints, point) || entry.targetPoint == point)
                    return true;
            }
        }

        return false;
    }

    static bool ContainsSpawnPoint(Transform[] points, Transform point)
    {
        if (points == null || point == null)
            return false;
        for (int i = 0; i < points.Length; i++)
        {
            if (points[i] == point)
                return true;
        }
        return false;
    }

    static void DrawSpawnMarker(Vector3 pos, Color color, float radius, string label = null)
    {
        // 半透明实心 + 线框，再加十字，未选中时也容易辨认
        Color fill = color;
        fill.a = 0.35f;
        Gizmos.color = fill;
        Gizmos.DrawSphere(pos, radius * 0.65f);

        Gizmos.color = color;
        Gizmos.DrawWireSphere(pos, radius);

        float arm = radius * 1.35f;
        Gizmos.DrawLine(pos + Vector3.left * arm, pos + Vector3.right * arm);
        Gizmos.DrawLine(pos + Vector3.up * arm, pos + Vector3.down * arm);

        Gizmos.DrawIcon(pos, "sv_icon_dot3_pix16_gizmo", true);

        if (!string.IsNullOrEmpty(label))
            DrawSpawnLabel(pos, label, color);
    }

    static void DrawSpawnLabel(Vector3 pos, string text, Color color)
    {
#if UNITY_EDITOR
        var style = new GUIStyle(UnityEditor.EditorStyles.boldLabel)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 11
        };
        style.normal.textColor = color;
        UnityEditor.Handles.Label(pos + Vector3.up * 0.45f, text, style);
#endif
    }

    /// <summary>
    /// 批次死亡/销毁时从存活集合移除；同一敌人可同时属于多个批次。
    /// </summary>
    class BatchAliveTracker : MonoBehaviour
    {
        readonly List<HashSet<Character>> batches = new();
        Character character;
        UnityAction dieHandler;

        public void AddToBatch(HashSet<Character> aliveBatch, Character target)
        {
            if (aliveBatch == null || target == null)
                return;

            if (character == null)
            {
                character = target;
                dieHandler = OnDied;
                character.OnDie.AddListener(dieHandler);
            }

            if (!batches.Contains(aliveBatch))
                batches.Add(aliveBatch);

            aliveBatch.Add(character);
        }

        void RemoveFromAll()
        {
            if (character != null)
            {
                for (int i = 0; i < batches.Count; i++)
                    batches[i]?.Remove(character);
            }

            batches.Clear();
        }

        void Unbind()
        {
            if (character != null && dieHandler != null)
                character.OnDie.RemoveListener(dieHandler);
            dieHandler = null;
            character = null;
            batches.Clear();
        }

        void OnDied() => RemoveFromAll();

        void OnDestroy()
        {
            RemoveFromAll();
            Unbind();
        }
    }
}
