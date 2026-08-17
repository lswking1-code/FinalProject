using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 记录本局已击杀、且应写入存档的场景敌人。
/// 物体销毁后仍靠此集合在存档点把死亡旗标写进 Data。
/// </summary>
public static class EnemyDeathProgress
{
    public const string KeyPrefix = "EnemyDead:";

    static readonly HashSet<string> s_killedThisSession = new();

    public static void MarkKilled(string progressKey)
    {
        if (string.IsNullOrEmpty(progressKey))
            return;

        s_killedThisSession.Add(progressKey);
    }

    public static bool WasKilled(string progressKey)
    {
        return !string.IsNullOrEmpty(progressKey) && s_killedThisSession.Contains(progressKey);
    }

    public static void CopySessionKillsTo(Data data)
    {
        if (data?.boolSavedData == null)
            return;

        foreach (string key in s_killedThisSession)
            data.boolSavedData[key] = true;
    }

    public static void RestoreSessionFrom(Data data)
    {
        s_killedThisSession.Clear();
        if (data?.boolSavedData == null)
            return;

        foreach (var pair in data.boolSavedData)
        {
            if (pair.Value && !string.IsNullOrEmpty(pair.Key) && pair.Key.StartsWith(KeyPrefix))
                s_killedThisSession.Add(pair.Key);
        }
    }

    public static void ClearSession() => s_killedThisSession.Clear();

    public static bool IsSavedDead(Data data, string progressKey)
    {
        if (string.IsNullOrEmpty(progressKey))
            return false;

        if (WasKilled(progressKey))
            return true;

        return data?.boolSavedData != null
            && data.boolSavedData.TryGetValue(progressKey, out bool dead)
            && dead;
    }
}

/// <summary>
/// 场景预置敌人的死亡存档。运行时刷出的敌人（Instantiate 的 Clone）不会挂此组件。
/// </summary>
[DisallowMultipleComponent]
public class EnemyDeathPersist : MonoBehaviour, ISaveable
{
    Enemy enemy;
    string cachedKey;
    bool appliedSavedDeath;

    void Awake()
    {
        enemy = GetComponent<Enemy>();
        cachedKey = BuildProgressKey(transform);
    }

    void OnEnable()
    {
        if (enemy == null || !enemy.ShouldPersistDeath)
            return;

        ((ISaveable)this).RegisterSaveData();
        DataManager.instance?.ApplyLoadedData(this);
    }

    void Start()
    {
        // Additive 加载时 Awake/OnEnable 里 scene.name 可能仍为空，补一次。
        cachedKey = BuildProgressKey(transform);
        DataManager.instance?.ApplyLoadedData(this);
    }

    void OnDisable() => ((ISaveable)this).UnregisterSaveData();

    public DataDefination GetDataID() => GetComponent<DataDefination>();

    public void GetSaveData(Data data)
    {
        if (data?.boolSavedData == null || enemy == null || !enemy.ShouldPersistDeath)
            return;

        if (!enemy.isDead)
            return;

        string key = ProgressKey();
        data.boolSavedData[key] = true;
        EnemyDeathProgress.MarkKilled(key);
    }

    public void LoadSaveData(Data data)
    {
        if (appliedSavedDeath || enemy == null || !enemy.ShouldPersistDeath)
            return;

        if (!EnemyDeathProgress.IsSavedDead(data, ProgressKey()))
            return;

        appliedSavedDeath = true;
        enemy.RemoveBecauseSavedDead();
    }

    string ProgressKey()
    {
        if (string.IsNullOrEmpty(cachedKey))
            cachedKey = BuildProgressKey(transform);

        return cachedKey;
    }

    public static string BuildProgressKey(Enemy target)
    {
        return target == null ? string.Empty : BuildProgressKey(target.transform);
    }

    public static string BuildProgressKey(Transform target)
    {
        if (target == null)
            return string.Empty;

        return EnemyDeathProgress.KeyPrefix + ResolveSceneName(target.gameObject) + ":" + HierarchyPath(target);
    }

    static string ResolveSceneName(GameObject go)
    {
        var scene = go.scene;
        if (scene.IsValid() && !string.IsNullOrEmpty(scene.name))
            return scene.name;

        var active = SceneManager.GetActiveScene();
        return !string.IsNullOrEmpty(active.name) ? active.name : "unknown";
    }

    static string HierarchyPath(Transform t)
    {
        var names = new List<string>(8);
        while (t != null)
        {
            names.Add(t.name);
            t = t.parent;
        }

        var sb = new StringBuilder();
        for (int i = names.Count - 1; i >= 0; i--)
        {
            if (sb.Length > 0)
                sb.Append('/');
            sb.Append(names[i]);
        }

        return sb.ToString();
    }
}
