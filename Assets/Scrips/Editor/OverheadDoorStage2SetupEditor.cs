#if UNITY_EDITOR
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Stage2 OverheadDoor：挂载 OverheadDoor / OverheadDoorCore，并做结构与逻辑校验。
/// </summary>
public static class OverheadDoorStage2SetupEditor
{
    const string Stage2Path = "Assets/Scenes/Stage2.unity";
    const string DoorName = "OverheadDoor";
    const string CoreName = "Core";
    const string CircleSpriteGuid = "a86470a33a6bf42c4b3595704624658b";
    const string DefaultSpriteMatGuid = "a97c105638bdf8b4a8650670310a4cd3";

    [MenuItem("Lost Division/Setup Stage2 Overhead Door")]
    public static void SetupStage2()
    {
        if (!OpenStage2(out var scene))
            return;

        var doorGo = FindRootByName(DoorName);
        if (doorGo == null)
        {
            Debug.LogError($"未找到名为 {DoorName} 的物体。");
            return;
        }

        ConfigureDoor(doorGo);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Selection.activeGameObject = doorGo;
        Debug.Log($"Stage2 {DoorName} 已挂载 OverheadDoor + Core。", doorGo);
    }

    [MenuItem("Lost Division/Validate Stage2 Overhead Door")]
    public static void ValidateStage2()
    {
        if (!OpenStage2(out _))
            return;

        int issues = ValidateStructure(log: true);
        int logicIssues = ValidateLogicInIsolation(log: true);
        if (issues + logicIssues == 0)
            Debug.Log("Stage2 OverheadDoor 结构与逻辑校验通过。");
        else
            Debug.LogError($"Stage2 OverheadDoor 校验失败：结构 {issues}，逻辑 {logicIssues}。");
    }

    /// <summary>供 -executeMethod 批处理：接线 + 校验，失败时 Exit(1)。</summary>
    public static void SetupAndVerify()
    {
        SetupStage2();
        int issues = ValidateStructure(log: true);
        int logicIssues = ValidateLogicInIsolation(log: true);
        if (issues + logicIssues != 0)
        {
            Debug.LogError($"OverheadDoor SetupAndVerify 失败：结构 {issues}，逻辑 {logicIssues}。");
            EditorApplication.Exit(1);
            return;
        }

        Debug.Log("OverheadDoor SetupAndVerify 通过。");
        EditorApplication.Exit(0);
    }

    static bool OpenStage2(out UnityEngine.SceneManagement.Scene scene)
    {
        scene = EditorSceneManager.OpenScene(Stage2Path, OpenSceneMode.Single);
        if (!scene.IsValid())
        {
            Debug.LogError($"无法打开场景：{Stage2Path}");
            return false;
        }
        return true;
    }

    static GameObject FindRootByName(string name)
    {
        var transforms = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < transforms.Length; i++)
        {
            if (transforms[i] != null && transforms[i].parent == null && transforms[i].name == name)
                return transforms[i].gameObject;
        }
        return null;
    }

    static void ConfigureDoor(GameObject doorGo)
    {
        // 去掉一次性 AnimatedDestroy，避免与升降门逻辑冲突
        var animated = doorGo.GetComponent<AnimatedDestroy>();
        if (animated != null)
            Object.DestroyImmediate(animated, true);

        var door = doorGo.GetComponent<OverheadDoor>();
        if (door == null)
            door = doorGo.AddComponent<OverheadDoor>();

        var doorSo = new SerializedObject(door);
        doorSo.FindProperty("openWorldOffset").vector2Value = new Vector2(0f, 10f);
        doorSo.FindProperty("damageToFullyOpen").intValue = 100;
        doorSo.FindProperty("idleDelay").floatValue = 1f;
        doorSo.FindProperty("returnSpeed").floatValue = 0.35f;
        doorSo.FindProperty("passThroughProgress").floatValue = 0.85f;

        // 仅引用根上的门体碰撞，不含 Core
        var rootCols = doorGo.GetComponents<Collider2D>();
        var colsProp = doorSo.FindProperty("doorColliders");
        colsProp.arraySize = rootCols.Length;
        for (int i = 0; i < rootCols.Length; i++)
            colsProp.GetArrayElementAtIndex(i).objectReferenceValue = rootCols[i];
        doorSo.ApplyModifiedPropertiesWithoutUndo();

        Transform coreTf = doorGo.transform.Find(CoreName);
        GameObject coreGo;
        if (coreTf == null)
        {
            coreGo = new GameObject(CoreName);
            coreGo.transform.SetParent(doorGo.transform, false);
        }
        else
        {
            coreGo = coreTf.gameObject;
        }

        // 父物体 scale (8,1,1) + rot Z90 → lossy ≈ (1,8,1)；子物体补偿后约为单位大小
        coreGo.transform.localPosition = new Vector3(0f, -1.5f, 0f);
        coreGo.transform.localRotation = Quaternion.identity;
        coreGo.transform.localScale = new Vector3(1f, 0.125f, 1f);
        coreGo.layer = LayerMask.NameToLayer("Enemy");

        var core = coreGo.GetComponent<OverheadDoorCore>();
        if (core == null)
            core = coreGo.AddComponent<OverheadDoorCore>();

        var coreSo = new SerializedObject(core);
        coreSo.FindProperty("door").objectReferenceValue = door;
        coreSo.ApplyModifiedPropertiesWithoutUndo();

        var circleCol = coreGo.GetComponent<CircleCollider2D>();
        if (circleCol == null)
            circleCol = coreGo.AddComponent<CircleCollider2D>();
        circleCol.radius = 0.5f;
        circleCol.isTrigger = false;

        EnsureCoreVisual(coreGo);
        EditorUtility.SetDirty(doorGo);
        EditorUtility.SetDirty(coreGo);
    }

    static void EnsureCoreVisual(GameObject coreGo)
    {
        Transform visual = coreGo.transform.Find("Circle");
        GameObject visualGo;
        if (visual == null)
        {
            visualGo = new GameObject("Circle");
            visualGo.transform.SetParent(coreGo.transform, false);
        }
        else
        {
            visualGo = visual.gameObject;
        }

        visualGo.transform.localPosition = Vector3.zero;
        visualGo.transform.localRotation = Quaternion.identity;
        visualGo.transform.localScale = Vector3.one;
        visualGo.layer = coreGo.layer;

        var sr = visualGo.GetComponent<SpriteRenderer>();
        if (sr == null)
            sr = visualGo.AddComponent<SpriteRenderer>();

        string spritePath = AssetDatabase.GUIDToAssetPath(CircleSpriteGuid);
        if (!string.IsNullOrEmpty(spritePath))
            sr.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(spritePath);

        string matPath = AssetDatabase.GUIDToAssetPath(DefaultSpriteMatGuid);
        if (!string.IsNullOrEmpty(matPath))
            sr.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(matPath);

        sr.color = new Color(1f, 0.89620304f, 0.33018857f, 1f);
        sr.sortingOrder = 1;
    }

    static int ValidateStructure(bool log)
    {
        int issues = 0;
        var doorGo = FindRootByName(DoorName);
        if (doorGo == null)
        {
            if (log) Debug.LogError($"缺少根物体 {DoorName}");
            return 1;
        }

        var door = doorGo.GetComponent<OverheadDoor>();
        if (door == null)
        {
            if (log) Debug.LogError($"{DoorName} 缺少 OverheadDoor", doorGo);
            issues++;
        }

        if (doorGo.GetComponent<AnimatedDestroy>() != null)
        {
            if (log) Debug.LogError($"{DoorName} 仍挂有 AnimatedDestroy，应移除以免冲突", doorGo);
            issues++;
        }

        var coreTf = doorGo.transform.Find(CoreName);
        if (coreTf == null)
        {
            if (log) Debug.LogError($"{DoorName} 缺少子物体 {CoreName}", doorGo);
            return issues + 1;
        }

        var core = coreTf.GetComponent<OverheadDoorCore>();
        if (core == null)
        {
            if (log) Debug.LogError($"{CoreName} 缺少 OverheadDoorCore", coreTf);
            issues++;
        }
        else
        {
            var so = new SerializedObject(core);
            if (so.FindProperty("door").objectReferenceValue == null)
            {
                if (log) Debug.LogError($"{CoreName}.door 未赋值", core);
                issues++;
            }
        }

        if (coreTf.GetComponent<Collider2D>() == null)
        {
            if (log) Debug.LogError($"{CoreName} 缺少 Collider2D", coreTf);
            issues++;
        }

        if (door != null)
        {
            var so = new SerializedObject(door);
            var cols = so.FindProperty("doorColliders");
            if (cols == null || cols.arraySize == 0)
            {
                if (log) Debug.LogError($"{DoorName}.doorColliders 为空", door);
                issues++;
            }
            else
            {
                for (int i = 0; i < cols.arraySize; i++)
                {
                    var col = cols.GetArrayElementAtIndex(i).objectReferenceValue as Collider2D;
                    if (col != null && col.transform.IsChildOf(coreTf))
                    {
                        if (log) Debug.LogError($"{DoorName}.doorColliders 不应包含 Core 碰撞", door);
                        issues++;
                    }
                }
            }
        }

        return issues;
    }

    /// <summary>隔离场景下验证伤害推进与回落逻辑（不依赖 Stage2 完整游玩）。</summary>
    static int ValidateLogicInIsolation(bool log)
    {
        int issues = 0;
        var prevScene = EditorSceneManager.GetActiveScene();
        var testScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);

        try
        {
            var doorGo = new GameObject("TestOverheadDoor");
            SceneManagerMove(doorGo, testScene);
            doorGo.transform.position = Vector3.zero;

            var box = doorGo.AddComponent<BoxCollider2D>();
            var door = doorGo.AddComponent<OverheadDoor>();
            var doorSo = new SerializedObject(door);
            doorSo.FindProperty("openWorldOffset").vector2Value = new Vector2(0f, 10f);
            doorSo.FindProperty("damageToFullyOpen").intValue = 100;
            doorSo.FindProperty("idleDelay").floatValue = 0f;
            doorSo.FindProperty("returnSpeed").floatValue = 1f;
            doorSo.FindProperty("passThroughProgress").floatValue = 0.85f;
            var colsProp = doorSo.FindProperty("doorColliders");
            colsProp.arraySize = 1;
            colsProp.GetArrayElementAtIndex(0).objectReferenceValue = box;
            doorSo.ApplyModifiedPropertiesWithoutUndo();

            var coreGo = new GameObject("TestCore");
            coreGo.transform.SetParent(doorGo.transform, false);
            SceneManagerMove(coreGo, testScene);
            var core = coreGo.AddComponent<OverheadDoorCore>();
            var coreSo = new SerializedObject(core);
            coreSo.FindProperty("door").objectReferenceValue = door;
            coreSo.ApplyModifiedPropertiesWithoutUndo();
            coreGo.AddComponent<CircleCollider2D>();

            InvokeAwake(door);
            InvokeAwake(core);

            var attackGo = new GameObject("TestAttack");
            SceneManagerMove(attackGo, testScene);
            var attack = attackGo.AddComponent<Attack>();
            attack.damage = 50;

            if (!core.RegisterHit(attack))
            {
                if (log) Debug.LogError("RegisterHit 应返回 true");
                issues++;
            }

            if (!Mathf.Approximately(door.Progress, 0.5f))
            {
                if (log) Debug.LogError($"伤害 50/100 后 Progress 应为 0.5，实际 {door.Progress}");
                issues++;
            }

            Vector3 expectedPos = Vector3.Lerp(Vector3.zero, new Vector3(0f, 10f, 0f), 0.5f);
            if ((doorGo.transform.position - expectedPos).sqrMagnitude > 0.0001f)
            {
                if (log) Debug.LogError($"门位置应为 {expectedPos}，实际 {doorGo.transform.position}");
                issues++;
            }

            attack.damage = 100;
            // 同帧同 Attack 去重：应仍为 0.5
            if (!core.RegisterHit(attack))
            {
                if (log) Debug.LogError("去重路径 RegisterHit 应仍返回 true");
                issues++;
            }
            if (!Mathf.Approximately(door.Progress, 0.5f))
            {
                if (log) Debug.LogError($"同帧去重后 Progress 应保持 0.5，实际 {door.Progress}");
                issues++;
            }

            // 换新 Attack 再打满
            var attack2Go = new GameObject("TestAttack2");
            SceneManagerMove(attack2Go, testScene);
            var attack2 = attack2Go.AddComponent<Attack>();
            attack2.damage = 50;
            core.RegisterHit(attack2);
            if (!Mathf.Approximately(door.Progress, 1f))
            {
                if (log) Debug.LogError($"累计满伤后 Progress 应为 1，实际 {door.Progress}");
                issues++;
            }

            if (box.enabled)
            {
                if (log) Debug.LogError("全开后门体碰撞应禁用");
                issues++;
            }
        }
        finally
        {
            EditorSceneManager.CloseScene(testScene, true);
            if (prevScene.IsValid())
                EditorSceneManager.SetActiveScene(prevScene);
        }

        return issues;
    }

    static void SceneManagerMove(GameObject go, UnityEngine.SceneManagement.Scene scene)
    {
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, scene);
    }

    static void InvokeAwake(MonoBehaviour behaviour)
    {
        if (behaviour == null)
            return;

        var awake = behaviour.GetType().GetMethod(
            "Awake",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        awake?.Invoke(behaviour, null);
    }
}
#endif
