#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// 为关卡进度物体补齐稳定的 DataDefination.ID。
/// </summary>
public static class SaveProgressIdSetup
{
    const string EncounterZonePrefabPath = "Assets/Prefabs/EncounterZone.prefab";

    [MenuItem("Lost Division/Ensure Save Progress Data IDs")]
    public static void EnsureSaveProgressDataIds()
    {
        int fixedCount = 0;
        fixedCount += EnsureOnPrefab(EncounterZonePrefabPath, "encounter-zone-prefab-001");

        foreach (var zone in Object.FindObjectsByType<EncounterZone>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            fixedCount += EnsureComponentId(zone.gameObject, $"encounter-{zone.gameObject.scene.name}-{zone.name}");

        foreach (var trigger in Object.FindObjectsByType<EnemySpawnTrigger>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            fixedCount += EnsureComponentId(trigger.gameObject, $"spawn-{trigger.gameObject.scene.name}-{trigger.name}");

        foreach (var savepoint in Object.FindObjectsByType<Savepoint>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            fixedCount += EnsureComponentId(savepoint.gameObject, $"savepoint-{savepoint.gameObject.scene.name}-{savepoint.name}");

        AssetDatabase.SaveAssets();
        Debug.Log($"Ensure Save Progress Data IDs: updated {fixedCount} object(s).");
    }

    static int EnsureOnPrefab(string path, string stableId)
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
        {
            Debug.LogWarning($"[SaveProgressIdSetup] Prefab not found: {path}");
            return 0;
        }

        var root = PrefabUtility.LoadPrefabContents(path);
        if (root == null)
            return 0;

        int changed = EnsureComponentId(root, stableId);
        if (changed > 0)
            PrefabUtility.SaveAsPrefabAsset(root, path);
        PrefabUtility.UnloadPrefabContents(root);
        return changed;
    }

    static int EnsureComponentId(GameObject go, string stableId)
    {
        if (go == null)
            return 0;

        var def = go.GetComponent<DataDefination>();
        if (def == null)
            def = go.AddComponent<DataDefination>();

        bool changed = false;
        if (def.persistentType != PersistentType.ReadWrite)
        {
            def.persistentType = PersistentType.ReadWrite;
            changed = true;
        }

        if (string.IsNullOrEmpty(def.ID))
        {
            def.ID = stableId;
            changed = true;
        }

        if (changed)
            EditorUtility.SetDirty(def);

        return changed ? 1 : 0;
    }
}
#endif
