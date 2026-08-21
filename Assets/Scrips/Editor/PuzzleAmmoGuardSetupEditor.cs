#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 在 Stage2 解谜区挂载 PuzzleAmmoGuard，并放置带 M 弹保底的入口存档点。
/// </summary>
public static class PuzzleAmmoGuardSetupEditor
{
    const string Stage2Path = "Assets/Scenes/Stage2.unity";
    const string EnemyMeleePath = "Assets/Prefabs/Enemy/EnemyMelee.prefab";
    const string BulletBoxMPath = "Assets/Prefabs/BulletBoxM.prefab";
    const string SaveDataEventPath = "Assets/Data SO/Event/Save Game Data Event SO.asset";

    [MenuItem("Lost Division/Validate Puzzle Ammo Guard Setup")]
    public static void ValidateSetup()
    {
        var guards = UnityEngine.Object.FindObjectsByType<PuzzleAmmoGuard>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        if (guards.Length == 0)
        {
            Debug.LogWarning("当前场景没有 PuzzleAmmoGuard。可运行 Lost Division/Setup Stage2 Puzzle Ammo Guard。");
            return;
        }

        int issues = 0;
        for (int i = 0; i < guards.Length; i++)
        {
            var so = new SerializedObject(guards[i]);
            if (so.FindProperty("assistEnemyPrefab").objectReferenceValue == null)
            {
                Debug.LogError($"[{guards[i].name}] assistEnemyPrefab 未配置", guards[i]);
                issues++;
            }
            if (so.FindProperty("ammoDropPrefab").objectReferenceValue == null)
            {
                Debug.LogError($"[{guards[i].name}] ammoDropPrefab 未配置", guards[i]);
                issues++;
            }
            var points = so.FindProperty("spawnPoints");
            if (points == null || points.arraySize == 0)
            {
                Debug.LogWarning($"[{guards[i].name}] spawnPoints 为空，将落在自身位置", guards[i]);
            }

            var col = guards[i].GetComponent<Collider2D>();
            if (col == null || !col.isTrigger)
            {
                Debug.LogError($"[{guards[i].name}] 需要 Is Trigger 的 Collider2D", guards[i]);
                issues++;
            }
        }

        var savepoints = UnityEngine.Object.FindObjectsByType<Savepoint>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        int ammoGuaranteed = 0;
        for (int i = 0; i < savepoints.Length; i++)
        {
            if (savepoints[i].ensureMinAmmoOnSave && savepoints[i].minBulletM > 0)
                ammoGuaranteed++;
        }

        if (ammoGuaranteed == 0)
            Debug.LogWarning("场景中没有开启 ensureMinAmmoOnSave 的存档点。");

        if (issues == 0)
            Debug.Log($"PuzzleAmmoGuard 校验通过：{guards.Length} 个 Guard，{ammoGuaranteed} 个弹药保底存档点。");
        else
            Debug.LogError($"PuzzleAmmoGuard 校验发现 {issues} 个问题。");
    }

    [MenuItem("Lost Division/Setup Stage2 Puzzle Ammo Guard")]
    public static void SetupStage2()
    {
        var scene = EditorSceneManager.OpenScene(Stage2Path, OpenSceneMode.Single);
        if (!scene.IsValid())
        {
            Debug.LogError("无法打开 Stage2。");
            return;
        }

        var existing = UnityEngine.Object.FindObjectsByType<PuzzleAmmoGuard>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < existing.Length; i++)
        {
            if (existing[i] != null)
                UnityEngine.Object.DestroyImmediate(existing[i].gameObject);
        }

        var bound = UnityEngine.Object.FindFirstObjectByType<BoundDevice>();
        var nodes = UnityEngine.Object.FindObjectsByType<EnergyNode>(
            FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        if (nodes == null || nodes.Length == 0)
        {
            Debug.LogError("Stage2 中未找到 EnergyNode。");
            return;
        }

        var enemyPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(EnemyMeleePath);
        var ammoPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BulletBoxMPath);
        if (enemyPrefab == null || ammoPrefab == null)
        {
            Debug.LogError("找不到 EnemyMelee 或 BulletBoxM 预制体。");
            return;
        }

        Bounds nodeBounds = new Bounds(nodes[0].transform.position, Vector3.zero);
        for (int i = 1; i < nodes.Length; i++)
            nodeBounds.Encapsulate(nodes[i].transform.position);
        nodeBounds.Expand(new Vector3(14f, 10f, 0f));

        var root = new GameObject("PuzzleAmmoGuard");
        Undo.RegisterCreatedObjectUndo(root, "Create PuzzleAmmoGuard");
        root.transform.position = nodeBounds.center;

        var box = root.AddComponent<BoxCollider2D>();
        box.isTrigger = true;
        box.size = new Vector2(
            Mathf.Max(12f, nodeBounds.size.x),
            Mathf.Max(14f, nodeBounds.size.y));

        var spawnPoints = new Transform[nodes.Length];
        for (int i = 0; i < nodes.Length; i++)
        {
            var pointGo = new GameObject($"AssistSpawn_{i}");
            Undo.RegisterCreatedObjectUndo(pointGo, "Create AssistSpawn");
            pointGo.transform.SetParent(root.transform, true);
            Vector3 nodePos = nodes[i].transform.position;
            pointGo.transform.position = new Vector3(nodePos.x - 2.5f, nodePos.y, 0f);
            spawnPoints[i] = pointGo.transform;
        }

        var guard = root.AddComponent<PuzzleAmmoGuard>();
        var so = new SerializedObject(guard);
        so.FindProperty("requiredAmmo").enumValueIndex = (int)AmmoType.M;
        so.FindProperty("assistEnemyPrefab").objectReferenceValue = enemyPrefab;
        so.FindProperty("ammoDropPrefab").objectReferenceValue = ammoPrefab;
        so.FindProperty("assistInterval").floatValue = 10f;
        so.FindProperty("firstCheckDelay").floatValue = 3f;
        so.FindProperty("boundDevice").objectReferenceValue = bound;

        var nodesProp = so.FindProperty("energyNodes");
        nodesProp.arraySize = nodes.Length;
        for (int i = 0; i < nodes.Length; i++)
            nodesProp.GetArrayElementAtIndex(i).objectReferenceValue = nodes[i];

        var pointsProp = so.FindProperty("spawnPoints");
        pointsProp.arraySize = spawnPoints.Length;
        for (int i = 0; i < spawnPoints.Length; i++)
            pointsProp.GetArrayElementAtIndex(i).objectReferenceValue = spawnPoints[i];

        so.ApplyModifiedPropertiesWithoutUndo();

        EnsurePuzzleSavepoint(nodeBounds, scene);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("Stage2 PuzzleAmmoGuard 与谜题入口存档点已配置完成。");
    }

    static void EnsurePuzzleSavepoint(Bounds puzzleBounds, Scene scene)
    {
        Vector3 entrance = new Vector3(puzzleBounds.min.x - 2f, puzzleBounds.min.y + 2f, 0f);

        Savepoint existingNear = null;
        var savepoints = UnityEngine.Object.FindObjectsByType<Savepoint>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        float bestDist = float.MaxValue;
        for (int i = 0; i < savepoints.Length; i++)
        {
            float d = Vector3.Distance(savepoints[i].transform.position, entrance);
            if (d < 12f && d < bestDist)
            {
                bestDist = d;
                existingNear = savepoints[i];
            }
        }

        Savepoint target = existingNear;
        if (target == null)
        {
            var go = new GameObject("Savepoint_PuzzleEntrance");
            Undo.RegisterCreatedObjectUndo(go, "Create Puzzle Savepoint");
            go.transform.position = new Vector3(26f, 1.5f, 0f);

            var sr = go.AddComponent<SpriteRenderer>();

            var col = go.AddComponent<CircleCollider2D>();
            col.isTrigger = true;
            col.radius = 2.22f;

            var dataDef = go.AddComponent<DataDefination>();
            var dataSo = new SerializedObject(dataDef);
            var persistentProp = dataSo.FindProperty("persistentType");
            if (persistentProp != null)
                persistentProp.enumValueIndex = 0; // ReadWrite if first
            dataSo.FindProperty("ID").stringValue = Guid.NewGuid().ToString();
            dataSo.ApplyModifiedPropertiesWithoutUndo();

            target = go.AddComponent<Savepoint>();
            var saveSo = new SerializedObject(target);
            saveSo.FindProperty("saveDataEvent").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<VoidEventSO>(SaveDataEventPath);
            saveSo.FindProperty("spriteRenderer").objectReferenceValue = sr;

            Sprite dark = LoadSpriteByGuid("6074414106593ad4fb2d9101c77c4082");
            Sprite light = LoadSpriteByGuid("bb72312c8d08da84ba8cf3c7089083a6");
            if (dark != null)
            {
                sr.sprite = dark;
                saveSo.FindProperty("darkSprite").objectReferenceValue = dark;
            }
            if (light != null)
                saveSo.FindProperty("lightSprite").objectReferenceValue = light;
            saveSo.ApplyModifiedPropertiesWithoutUndo();
        }

        target.ensureMinAmmoOnSave = true;
        target.minBulletM = 3;
        target.minBulletS = 0;
        target.minBulletL = 0;
        EditorUtility.SetDirty(target);
    }

    static Sprite LoadSpriteByGuid(string guid)
    {
        string path = AssetDatabase.GUIDToAssetPath(guid);
        if (string.IsNullOrEmpty(path))
            return null;

        var sprites = AssetDatabase.LoadAllAssetsAtPath(path);
        for (int i = 0; i < sprites.Length; i++)
        {
            if (sprites[i] is Sprite sprite)
                return sprite;
        }

        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }
}
#endif
