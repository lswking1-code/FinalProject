using UnityEngine;

/// <summary>
/// 直升机单次召唤的一种小兵。
/// </summary>
[System.Serializable]
public class HelicopterSummonMinion
{
    [Tooltip("要生成的敌人预制体；不要填 Helicopter 自身")]
    public GameObject enemyPrefab;
    [Tooltip("本种敌人数量")]
    [Min(0)] public int count = 1;
    [Tooltip("每个生成实例的弹药/血包掉落；长度随 count 自动对齐")]
    public EnemyInstanceDropConfig[] drops;
}

/// <summary>
/// 直升机召唤编制。预制体挂默认档，遭遇波次条目可另拖一份覆盖。
/// 数量/间隔/无限刷会覆盖直升机 EnemyGenerate 上的对应设置。
/// </summary>
[CreateAssetMenu(
    fileName = "HelicopterSummonProfile",
    menuName = "Lost Division/Helicopter Summon Profile")]
public class HelicopterSummonProfile : ScriptableObject
{
    [Tooltip("按列表顺序依次召唤")]
    public HelicopterSummonMinion[] minions;

    [Tooltip("相邻两个敌人的间隔（秒）；0 表示本批瞬间刷完")]
    [Min(0f)] public float intraWaveInterval = 0.4f;

    [Header("覆盖 EnemyGenerate")]
    [Tooltip("有限刷怪总上限；0 表示不额外限制（实际总数 = 各小兵数量之和）。不含无限刷新。覆盖直升机 maxTotalSpawns")]
    [Min(0)] public int maxTotalSpawns;

    [Tooltip("相邻两波之间的默认等待时间（秒）；无限刷新两批之间也用此间隔。覆盖直升机 spawnInterval")]
    [Min(0f)] public float spawnInterval = 3f;

    [Tooltip("开始召唤前的首次延迟（秒）。覆盖直升机 initialDelay")]
    [Min(0f)] public float initialDelay;

    [Tooltip("勾选后循环刷新本编制：本批刷完直升机可继续走位，场上这批清光后再按 Spawn Interval 补刷。覆盖条目 infiniteRefresh")]
    public bool infiniteRefresh;

    [Header("刷完离场")]
    [Tooltip("有限召唤全部刷完后垂直向上飞离并销毁。Infinite Refresh 开启时无效")]
    public bool leaveAfterSpawn;

    [Tooltip("离场后多少秒销毁自身")]
    [Min(0.1f)] public float leaveDestroyDelay = 3f;

    void OnValidate()
    {
        if (minions == null)
            return;

        for (int i = 0; i < minions.Length; i++)
            SyncMinionDrops(minions[i]);
    }

    static void SyncMinionDrops(HelicopterSummonMinion minion)
    {
        if (minion == null)
            return;

        int targetCount = Mathf.Max(0, minion.count);
        if (minion.drops == null)
        {
            minion.drops = new EnemyInstanceDropConfig[targetCount];
            for (int i = 0; i < targetCount; i++)
                minion.drops[i] = new EnemyInstanceDropConfig();
            return;
        }

        if (minion.drops.Length == targetCount)
        {
            for (int i = 0; i < minion.drops.Length; i++)
            {
                if (minion.drops[i] == null)
                    minion.drops[i] = new EnemyInstanceDropConfig();
            }
            return;
        }

        var resized = new EnemyInstanceDropConfig[targetCount];
        int copyCount = Mathf.Min(minion.drops.Length, targetCount);
        for (int i = 0; i < copyCount; i++)
            resized[i] = minion.drops[i] ?? new EnemyInstanceDropConfig();
        for (int i = copyCount; i < targetCount; i++)
            resized[i] = new EnemyInstanceDropConfig();
        minion.drops = resized;
    }

    public bool HasAnyMinion()
    {
        if (minions == null)
            return false;

        for (int i = 0; i < minions.Length; i++)
        {
            var minion = minions[i];
            if (minion != null && minion.enemyPrefab != null && minion.count > 0)
                return true;
        }

        return false;
    }

    /// <summary>
    /// 转成 EnemyGenerate 可用的波次配置。infiniteRefresh 时条目会循环刷。
    /// </summary>
    public EnemyWaveConfig[] BuildWaves()
    {
        if (minions == null || minions.Length == 0)
            return System.Array.Empty<EnemyWaveConfig>();

        int valid = 0;
        for (int i = 0; i < minions.Length; i++)
        {
            var minion = minions[i];
            if (minion != null && minion.enemyPrefab != null && minion.count > 0)
                valid++;
        }

        if (valid <= 0)
            return System.Array.Empty<EnemyWaveConfig>();

        var entries = new EnemyWaveEntry[valid];
        int write = 0;
        for (int i = 0; i < minions.Length; i++)
        {
            var minion = minions[i];
            if (minion == null || minion.enemyPrefab == null || minion.count <= 0)
                continue;

            entries[write++] = new EnemyWaveEntry
            {
                enemyPrefab = minion.enemyPrefab,
                count = minion.count,
                drops = CloneDrops(minion.drops, minion.count),
                infiniteRefresh = infiniteRefresh
            };
        }

        return new[]
        {
            new EnemyWaveConfig
            {
                enemies = entries,
                intraWaveInterval = intraWaveInterval,
                delayAfterWave = spawnInterval,
                waitUntilCleared = false,
                hpScale = 1f,
                speedScale = 1f
            }
        };
    }

    static EnemyInstanceDropConfig[] CloneDrops(EnemyInstanceDropConfig[] source, int count)
    {
        int len = Mathf.Max(0, count);
        var result = new EnemyInstanceDropConfig[len];
        for (int i = 0; i < len; i++)
        {
            if (source != null && i < source.Length && source[i] != null)
            {
                result[i] = new EnemyInstanceDropConfig
                {
                    dropAmmoOnDeath = source[i].dropAmmoOnDeath,
                    ammoType = source[i].ammoType,
                    dropHealthOnDeath = source[i].dropHealthOnDeath
                };
            }
            else
            {
                result[i] = new EnemyInstanceDropConfig();
            }
        }

        return result;
    }
}
