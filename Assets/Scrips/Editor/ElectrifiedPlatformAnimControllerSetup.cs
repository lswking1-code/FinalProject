#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// 生成 / 修复通电平台 Animator Controller（Idle / Activate / Inactivate / Inactivate_Idle）。
/// </summary>
public static class ElectrifiedPlatformAnimControllerSetup
{
    const string ControllerPath = "Assets/Animations/Items/ElectrifiedPlatform.controller";
    const string IdleClipPath = "Assets/Animations/Items/TrapA_Idle.anim";
    const string ActivateClipPath = "Assets/Animations/Items/TrapA_activate.anim";
    const string InactivateClipPath = "Assets/Animations/Items/TrapA_inactivate.anim";
    const string InactivateIdleClipPath = "Assets/Animations/Items/TrapA_deactivate_Idle.anim";

    [InitializeOnLoadMethod]
    static void AutoEnsure()
    {
        EditorApplication.delayCall += () =>
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return;

            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (controller == null)
                CreateOrRebuildController(silent: true);
        };
    }

    [MenuItem("Lost Division/Create Electrified Platform Animator Controller")]
    public static void CreateElectrifiedPlatformAnimatorController()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            return;

        CreateOrRebuildController(silent: false);
    }

    static void CreateOrRebuildController(bool silent)
    {
        var idle = AssetDatabase.LoadAssetAtPath<AnimationClip>(IdleClipPath);
        var activate = AssetDatabase.LoadAssetAtPath<AnimationClip>(ActivateClipPath);
        var inactivate = AssetDatabase.LoadAssetAtPath<AnimationClip>(InactivateClipPath);
        var inactivateIdle = AssetDatabase.LoadAssetAtPath<AnimationClip>(InactivateIdleClipPath);

        SetLoopTime(idle, true);
        SetLoopTime(inactivateIdle, true);
        SetLoopTime(activate, false);
        SetLoopTime(inactivate, false);

        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller == null)
            controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);

        if (controller.layers == null || controller.layers.Length == 0)
            controller.AddLayer("Base Layer");

        var sm = controller.layers[0].stateMachine;
        var states = EnsureStates(sm);
        states["Idle"].motion = idle;
        states["Activate"].motion = activate;
        states["Inactivate"].motion = inactivate;
        states["Inactivate_Idle"].motion = inactivateIdle;
        sm.defaultState = states["Idle"];

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();

        if (!silent)
            Debug.Log($"已创建/更新 {ControllerPath}");
    }

    static bool HasRequiredStates(AnimatorController controller)
    {
        if (controller == null || controller.layers == null || controller.layers.Length == 0)
            return false;

        var sm = controller.layers[0].stateMachine;
        if (sm == null)
            return false;

        bool idle = false, activate = false, inactivate = false, inactivateIdle = false;
        foreach (var child in sm.states)
        {
            if (child.state == null)
                continue;
            switch (child.state.name)
            {
                case "Idle": idle = true; break;
                case "Activate": activate = true; break;
                case "Inactivate": inactivate = true; break;
                case "Inactivate_Idle": inactivateIdle = true; break;
            }
        }

        return idle && activate && inactivate && inactivateIdle;
    }

    static Dictionary<string, AnimatorState> EnsureStates(AnimatorStateMachine sm)
    {
        var map = new Dictionary<string, AnimatorState>();
        foreach (var child in sm.states)
        {
            if (child.state != null && !string.IsNullOrEmpty(child.state.name))
                map[child.state.name] = child.state;
        }

        Vector3 Pos(string name) => name switch
        {
            "Idle" => new Vector3(350f, 100f, 0f),
            "Activate" => new Vector3(120f, 100f, 0f),
            "Inactivate" => new Vector3(580f, 100f, 0f),
            "Inactivate_Idle" => new Vector3(810f, 100f, 0f),
            _ => new Vector3(200f, 200f, 0f)
        };

        foreach (var name in new[] { "Idle", "Activate", "Inactivate", "Inactivate_Idle" })
        {
            if (!map.ContainsKey(name))
                map[name] = sm.AddState(name, Pos(name));
        }

        return map;
    }

    static void SetLoopTime(AnimationClip clip, bool loop)
    {
        if (clip == null)
            return;

        var settings = AnimationUtility.GetAnimationClipSettings(clip);
        if (settings.loopTime == loop)
            return;

        settings.loopTime = loop;
        AnimationUtility.SetAnimationClipSettings(clip, settings);
        EditorUtility.SetDirty(clip);
    }
}
#endif
