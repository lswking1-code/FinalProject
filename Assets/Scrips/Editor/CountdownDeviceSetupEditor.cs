#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Stage2 倒计时装置接线：ToggleSwitch + CountdownDevice + 门 + EncounterZone(manual)。
/// </summary>
public static class CountdownDeviceSetupEditor
{
    const string Stage2Path = "Assets/Scenes/Stage2.unity";
    const string ToggleSwitchPrefabPath = "Assets/Prefabs/ToggleSwitch.prefab";
    const string GroundPrefabPath = "Assets/Prefabs/Ground.prefab";
    const string EncounterZoneName = "EncounterZone (6)";
    const string DeviceRootName = "CountdownGate";
    const string SwitchName = "ToggleSwitch_Countdown";
    const string DoorName = "CountdownDoor";

    [MenuItem("Lost Division/Setup Stage2 Countdown Device")]
    public static void SetupStage2()
    {
        var scene = EditorSceneManager.OpenScene(Stage2Path, OpenSceneMode.Single);
        if (!scene.IsValid())
        {
            Debug.LogError($"无法打开场景：{Stage2Path}");
            return;
        }

        EncounterZone zone = FindEncounterByName(EncounterZoneName);
        if (zone == null)
        {
            Debug.LogError($"未找到 {EncounterZoneName}，请先在 Stage2 放置遭遇区。");
            return;
        }

        SetStartOnPlayerEnter(zone, false);

        Vector3 zonePos = zone.transform.position;
        ToggleSwitch sw = EnsureToggleSwitch(zonePos + new Vector3(-10f, -2f, 0f));
        AnimatedDestroy door = EnsureDoor(zonePos + new Vector3(12f, -2f, 0f));
        CountdownDevice device = EnsureDevice(zonePos, sw, zone, door);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Selection.activeGameObject = device.gameObject;
        Debug.Log(
            $"Stage2 CountdownDevice 已接线：开关={sw.name}，门={door.name}，遭遇区={zone.name}（startOnPlayerEnter=false）。音效请在 Inspector 挂 clip。",
            device);
    }

    [MenuItem("Lost Division/Validate Stage2 Countdown Device")]
    public static void ValidateSetup()
    {
        var devices = Object.FindObjectsByType<CountdownDevice>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        if (devices.Length == 0)
        {
            Debug.LogWarning("当前场景没有 CountdownDevice。可运行 Lost Division/Setup Stage2 Countdown Device。");
            return;
        }

        int issues = 0;
        for (int i = 0; i < devices.Length; i++)
        {
            var so = new SerializedObject(devices[i]);
            if (so.FindProperty("activationSwitch").objectReferenceValue == null)
            {
                Debug.LogError($"[{devices[i].name}] activationSwitch 未配置", devices[i]);
                issues++;
            }
            if (so.FindProperty("encounterZone").objectReferenceValue == null)
            {
                Debug.LogError($"[{devices[i].name}] encounterZone 未配置", devices[i]);
                issues++;
            }
            if (so.FindProperty("doorOnComplete").objectReferenceValue == null)
            {
                Debug.LogError($"[{devices[i].name}] doorOnComplete 未配置", devices[i]);
                issues++;
            }

            var zone = so.FindProperty("encounterZone").objectReferenceValue as EncounterZone;
            if (zone != null)
            {
                var zso = new SerializedObject(zone);
                if (zso.FindProperty("startOnPlayerEnter").boolValue)
                {
                    Debug.LogError($"[{zone.name}] startOnPlayerEnter 仍为 true，提前进区会开战", zone);
                    issues++;
                }
            }
        }

        if (issues == 0)
            Debug.Log($"CountdownDevice 校验通过：{devices.Length} 个装置。");
        else
            Debug.LogError($"CountdownDevice 校验发现 {issues} 个问题。");
    }

    static EncounterZone FindEncounterByName(string objectName)
    {
        var zones = Object.FindObjectsByType<EncounterZone>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < zones.Length; i++)
        {
            if (zones[i] != null && zones[i].name == objectName)
                return zones[i];
        }
        return null;
    }

    static void SetStartOnPlayerEnter(EncounterZone zone, bool value)
    {
        var so = new SerializedObject(zone);
        var prop = so.FindProperty("startOnPlayerEnter");
        if (prop == null)
            return;
        prop.boolValue = value;
        so.ApplyModifiedPropertiesWithoutUndo();
        PrefabUtility.RecordPrefabInstancePropertyModifications(zone);
    }

    static ToggleSwitch EnsureToggleSwitch(Vector3 worldPos)
    {
        var existing = GameObject.Find(SwitchName);
        if (existing != null)
        {
            var sw = existing.GetComponent<ToggleSwitch>();
            if (sw != null)
                return sw;
        }

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ToggleSwitchPrefabPath);
        if (prefab == null)
        {
            Debug.LogError($"找不到预制体：{ToggleSwitchPrefabPath}");
            return null;
        }

        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        instance.name = SwitchName;
        instance.transform.position = worldPos;
        instance.layer = 16;

        var swComp = instance.GetComponent<ToggleSwitch>();
        var so = new SerializedObject(swComp);
        so.FindProperty("isOn").boolValue = false;
        so.FindProperty("targets").arraySize = 0;
        so.ApplyModifiedPropertiesWithoutUndo();
        PrefabUtility.RecordPrefabInstancePropertyModifications(swComp);
        return swComp;
    }

    static AnimatedDestroy EnsureDoor(Vector3 worldPos)
    {
        var existing = GameObject.Find(DoorName);
        if (existing != null)
        {
            var ad = existing.GetComponent<AnimatedDestroy>();
            if (ad != null)
                return ad;
        }

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(GroundPrefabPath);
        GameObject doorGo;
        if (prefab != null)
        {
            doorGo = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            doorGo.name = DoorName;
        }
        else
        {
            doorGo = new GameObject(DoorName);
            doorGo.AddComponent<SpriteRenderer>();
            doorGo.AddComponent<BoxCollider2D>();
        }

        doorGo.transform.position = worldPos;
        doorGo.transform.rotation = Quaternion.Euler(0f, 0f, 90f);
        doorGo.transform.localScale = new Vector3(10f, 1f, 1f);

        var destroy = doorGo.GetComponent<AnimatedDestroy>();
        if (destroy == null)
            destroy = doorGo.AddComponent<AnimatedDestroy>();

        var so = new SerializedObject(destroy);
        so.FindProperty("hideWhenFinished").boolValue = true;
        so.FindProperty("openWorldOffset").vector2Value = new Vector2(0f, 10f);
        so.FindProperty("openMoveDuration").floatValue = 0.5f;
        so.FindProperty("destroyStateName").stringValue = string.Empty;
        so.ApplyModifiedPropertiesWithoutUndo();
        return destroy;
    }

    static CountdownDevice EnsureDevice(
        Vector3 worldPos,
        ToggleSwitch sw,
        EncounterZone zone,
        AnimatedDestroy door)
    {
        var existing = GameObject.Find(DeviceRootName);
        GameObject root = existing != null ? existing : new GameObject(DeviceRootName);
        root.transform.position = worldPos;

        if (root.GetComponent<DataDefination>() == null)
        {
            var def = root.AddComponent<DataDefination>();
            var dso = new SerializedObject(def);
            dso.FindProperty("persistentType").enumValueIndex = (int)PersistentType.ReadWrite;
            dso.FindProperty("ID").stringValue = System.Guid.NewGuid().ToString();
            dso.ApplyModifiedPropertiesWithoutUndo();
        }

        if (root.GetComponent<AudioSource>() == null)
        {
            var src = root.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.loop = false;
            src.spatialBlend = 0f;
        }

        var device = root.GetComponent<CountdownDevice>();
        if (device == null)
            device = root.AddComponent<CountdownDevice>();

        var so = new SerializedObject(device);
        so.FindProperty("activationSwitch").objectReferenceValue = sw;
        so.FindProperty("encounterZone").objectReferenceValue = zone;
        so.FindProperty("doorOnComplete").objectReferenceValue = door;
        so.FindProperty("countdownDuration").floatValue = 15f;
        so.FindProperty("floorCount").intValue = 5;
        so.FindProperty("sfxSource").objectReferenceValue = root.GetComponent<AudioSource>();
        so.ApplyModifiedPropertiesWithoutUndo();
        return device;
    }
}
#endif
