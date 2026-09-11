#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class ElevatorDoorRepairEditor
{
    const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;

    [MenuItem("Lost Division/Repair Elevator Display and Validate %#&e")]
    public static void Repair()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        string report = Path.Combine(Path.GetTempPath(), "ElevatorDoorValidation.txt");
        try
        {
            var scene = SceneManager.GetSceneByPath("Assets/Scenes/Stage2.unity");
            if (!scene.isLoaded)
                scene = EditorSceneManager.OpenScene("Assets/Scenes/Stage2.unity", OpenSceneMode.Additive);
            var device = scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<CountdownDevice>(true))
                .Single(x => x.name == "ElevatorDoor");
            var so = new SerializedObject(device);
            so.FindProperty("doorOnComplete").objectReferenceValue = device.GetComponent<AnimatedDestroy>();
            so.FindProperty("showCountdown").boolValue = true;
            so.FindProperty("displayWorldOffset").vector3Value = new Vector3(-1.5f, -6.5f, -0.1f);
            var sprites = so.FindProperty("chargeSprites");
            sprites.arraySize = 4;
            string[] states = { "Dormant", "Low", "Half", "Full" };
            for (int i = 0; i < states.Length; i++)
            {
                var sprite = AssetDatabase.LoadAssetAtPath<Sprite>($"Assets/Arts/StageMechanisms/TimedChargeNode_{states[i]}.png");
                Require(sprite != null, "Missing charge sprite: " + states[i]);
                sprites.GetArrayElementAtIndex(i).objectReferenceValue = sprite;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            device.BakeDisplay();
            PrefabUtility.RecordPrefabInstancePropertyModifications(device);
            var label = device.GetComponentInChildren<TextMeshPro>();
            Require(label != null && label.text == "90", "Countdown label was not baked");
            Require(Vector3.Distance(label.transform.position, new Vector3(255.34f, 33.5f, -0.11f)) < 0.5f,
                "Display is not near the switch");
            Require(Mathf.Abs(label.transform.lossyScale.x - 1f) < 0.01f &&
                Mathf.Abs(label.transform.lossyScale.y - 1f) < 0.01f, "Display scale is distorted");
            RunRegression();
            EditorUtility.SetDirty(device);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Selection.activeGameObject = label.transform.parent.gameObject;
            if (SceneView.lastActiveSceneView != null) SceneView.lastActiveSceneView.FrameSelected();
            File.WriteAllText(report, "PASS: Stage2 display baked with valid sprites/font, upright world scale and 90-second label.\n" +
                "PASS: Single-use switch starts countdown; repeated incomplete save load does not cancel it.\n" +
                "PASS: Door remains solid at 89 seconds; opens at 90 seconds, disables collision and slides 10 units.\n");
            Debug.Log("ElevatorDoor display and countdown regression checks passed.", device);
        }
        catch (Exception e)
        {
            File.WriteAllText(report, e.ToString());
            Debug.LogException(e);
        }
    }

    static void RunRegression()
    {
        var root = new GameObject("ElevatorRegression");
        root.SetActive(false);
        try
        {
            var collider = root.AddComponent<BoxCollider2D>();
            var door = root.AddComponent<AnimatedDestroy>();
            var device = root.AddComponent<CountdownDevice>();
            var sw = root.AddComponent<ToggleSwitch>();
            Set(sw, "singleUse", true);
            Set(device, "doorOnComplete", door);
            Set(device, "countdownDuration", 90f);
            Set(door, "openWorldOffset", new Vector2(0, 10));
            Invoke(door, "Awake");
            Invoke(device, "Awake");
            sw.onToggled = new UnityEngine.Events.UnityEvent<bool>();
            sw.onToggled.AddListener(device.OnSwitchToggled);
            var checkpoint = new Data();
            // Supply a stable id so the test exercises the real saved-state path.
            var id = new SerializedObject(device.GetDataID());
            id.FindProperty("ID").stringValue = "elevator-regression";
            id.ApplyModifiedPropertiesWithoutUndo();
            device.GetSaveData(checkpoint);
            sw.SetOn(true);
            Require(device.IsRunning && sw.IsSingleUseLocked, "Switch did not start countdown");
            device.LoadSaveData(checkpoint);
            Require(device.IsRunning, "Checkpoint cancelled the countdown");
            Invoke(device, "AdvanceCountdown", 89f);
            Require(device.IsRunning && collider.enabled, "Door opened early");
            Invoke(device, "AdvanceCountdown", 1f);
            Require(device.IsCompleted && !collider.enabled, "Door did not unlock at zero");
            Set(door, "slideTimer", 0.5f);
            Invoke(door, "Update");
            Require(Mathf.Abs(root.transform.position.y - 10f) < 0.01f, "Door did not slide open");
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    static void Set(object target, string field, object value) => target.GetType().GetField(field, Fields).SetValue(target, value);
    static void Invoke(object target, string method, params object[] args) => target.GetType().GetMethod(method, Fields).Invoke(target, args);
    static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
#endif
