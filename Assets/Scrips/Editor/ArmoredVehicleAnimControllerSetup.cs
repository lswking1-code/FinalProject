#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// 生成 / 修复装甲车 Animator Controller。
/// 参数与 ArmoredVehicleEnemy 状态脚本对齐：
/// walk / shoot / missile / ramWindup / ram / reload / hurt / dead。
/// 状态名 Missile / RamWindup / Die 需与脚本里的 IsNamedAnimFinished / DieStateName 一致。
/// Clip 可先留空，之后放到 ArmoredVehicle 目录再跑一次菜单即可挂上。
/// </summary>
public static class ArmoredVehicleAnimControllerSetup
{
    const string ControllerPath = "Assets/Animation/enemy_armored.controller";
    const string PrefabPath = "Assets/Prefabs/Enemy/ArmoredVehicle.prefab";
    const string ClipFolder = "Assets/Animations/Enemy/ArmoredVehicle";

    const string IdleClipPath = ClipFolder + "/ArmoredVehicle_idle.anim";
    const string WalkClipPath = ClipFolder + "/ArmoredVehicle_walk.anim";
    const string ShootClipPath = ClipFolder + "/ArmoredVehicle_shoot.anim";
    const string MissileClipPath = ClipFolder + "/ArmoredVehicle_missile.anim";
    const string RamWindupClipPath = ClipFolder + "/ArmoredVehicle_ramWindup.anim";
    const string RamClipPath = ClipFolder + "/ArmoredVehicle_ram.anim";
    const string HitClipPath = ClipFolder + "/ArmoredVehicle_hit.anim";
    const string DieClipPath = ClipFolder + "/ArmoredVehicle_die.anim";

    [InitializeOnLoadMethod]
    static void AutoEnsure()
    {
        EditorApplication.delayCall += () =>
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return;

            if (AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath) == null)
                CreateOrRebuildController(silent: true, assignPrefab: true);
        };
    }

    [MenuItem("Lost Division/Create Armored Vehicle Animator Controller")]
    public static void CreateArmoredVehicleAnimatorController()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            return;

        CreateOrRebuildController(silent: false, assignPrefab: true);
    }

    static void CreateOrRebuildController(bool silent, bool assignPrefab)
    {
        EnsureClipFolder();

        var idle = EnsureEmptyClip(IdleClipPath, "ArmoredVehicle_idle", 0.5f, loop: true, addDestroyEvent: false);
        var walk = EnsureEmptyClip(WalkClipPath, "ArmoredVehicle_walk", 0.5f, loop: true, addDestroyEvent: false);
        var shoot = EnsureEmptyClip(ShootClipPath, "ArmoredVehicle_shoot", 0.5f, loop: true, addDestroyEvent: false);
        var missile = EnsureEmptyClip(MissileClipPath, "ArmoredVehicle_missile", 0.8f, loop: false, addDestroyEvent: false);
        var ramWindup = EnsureEmptyClip(RamWindupClipPath, "ArmoredVehicle_ramWindup", 0.6f, loop: false, addDestroyEvent: false);
        var ram = EnsureEmptyClip(RamClipPath, "ArmoredVehicle_ram", 0.5f, loop: true, addDestroyEvent: false);
        var hit = EnsureEmptyClip(HitClipPath, "ArmoredVehicle_hit", 0.5f, loop: false, addDestroyEvent: false);
        var die = EnsureEmptyClip(DieClipPath, "ArmoredVehicle_die", 0.75f, loop: false, addDestroyEvent: true);

        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller == null)
            controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);

        EnsureParameter(controller, "walk", AnimatorControllerParameterType.Bool);
        EnsureParameter(controller, "shoot", AnimatorControllerParameterType.Bool);
        EnsureParameter(controller, "missile", AnimatorControllerParameterType.Bool);
        EnsureParameter(controller, "ramWindup", AnimatorControllerParameterType.Bool);
        EnsureParameter(controller, "ram", AnimatorControllerParameterType.Bool);
        EnsureParameter(controller, "reload", AnimatorControllerParameterType.Bool);
        EnsureParameter(controller, "hurt", AnimatorControllerParameterType.Trigger);
        EnsureParameter(controller, "dead", AnimatorControllerParameterType.Bool);

        var sm = controller.layers[0].stateMachine;
        var states = EnsureStates(sm);
        states["Idle"].motion = idle;
        states["Walk"].motion = walk;
        states["Shoot"].motion = shoot;
        states["Missile"].motion = missile;
        states["RamWindup"].motion = ramWindup;
        states["Ram"].motion = ram;
        states["Hit"].motion = hit;
        states["Die"].motion = die;
        sm.defaultState = states["Idle"];

        EnsureBoolTransition(states["Idle"], states["Walk"], "walk", true);
        EnsureBoolTransition(states["Walk"], states["Idle"], "walk", false);

        EnsureAnyStateBool(sm, states["Shoot"], "shoot", true, canTransitionToSelf: false);
        EnsureBoolTransition(states["Shoot"], states["Walk"], "shoot", false, extraWalkTrue: true);
        EnsureBoolTransition(states["Shoot"], states["Idle"], "shoot", false, extraWalkTrue: false);

        EnsureAnyStateBool(sm, states["Missile"], "missile", true, canTransitionToSelf: false);
        EnsureBoolTransition(states["Missile"], states["Walk"], "missile", false, extraWalkTrue: true);
        EnsureBoolTransition(states["Missile"], states["Idle"], "missile", false, extraWalkTrue: false);

        EnsureAnyStateBool(sm, states["RamWindup"], "ramWindup", true, canTransitionToSelf: false);
        EnsureBoolTransition(states["RamWindup"], states["Ram"], "ram", true);
        EnsureBoolTransition(states["RamWindup"], states["Walk"], "ramWindup", false, extraWalkTrue: true);
        EnsureBoolTransition(states["RamWindup"], states["Idle"], "ramWindup", false, extraWalkTrue: false);

        EnsureAnyStateBool(sm, states["Ram"], "ram", true, canTransitionToSelf: false);
        EnsureBoolTransition(states["Ram"], states["Walk"], "ram", false, extraWalkTrue: true);
        EnsureBoolTransition(states["Ram"], states["Idle"], "ram", false, extraWalkTrue: false);

        EnsureAnyStateTrigger(sm, states["Hit"], "hurt");
        EnsureExitTimeTransition(states["Hit"], states["Idle"], 0.9f);
        EnsureAnyStateBool(sm, states["Die"], "dead", true, canTransitionToSelf: false);

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();

        if (assignPrefab)
            AssignControllerToPrefab(controller);

        if (!silent)
            Debug.Log($"已创建/更新 {ControllerPath}。Clip 可稍后补到 {ClipFolder}/");
    }

    static void EnsureClipFolder()
    {
        if (AssetDatabase.IsValidFolder(ClipFolder))
            return;

        if (!AssetDatabase.IsValidFolder("Assets/Animations/Enemy"))
        {
            if (!AssetDatabase.IsValidFolder("Assets/Animations"))
                AssetDatabase.CreateFolder("Assets", "Animations");
            AssetDatabase.CreateFolder("Assets/Animations", "Enemy");
        }

        AssetDatabase.CreateFolder("Assets/Animations/Enemy", "ArmoredVehicle");
    }

    static AnimationClip EnsureEmptyClip(string path, string name, float length, bool loop, bool addDestroyEvent)
    {
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (clip != null)
            return clip;

        clip = new AnimationClip
        {
            name = name,
            frameRate = 12
        };

        var settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = loop;
        settings.stopTime = length;
        AnimationUtility.SetAnimationClipSettings(clip, settings);

        if (addDestroyEvent)
        {
            AnimationUtility.SetAnimationEvents(clip, new[]
            {
                new AnimationEvent
                {
                    time = length,
                    functionName = "DestroyAfterAnimation"
                }
            });
        }

        AssetDatabase.CreateAsset(clip, path);
        return clip;
    }

    static void AssignControllerToPrefab(AnimatorController controller)
    {
        var root = PrefabUtility.LoadPrefabContents(PrefabPath);
        if (root == null)
            return;

        try
        {
            var anim = root.GetComponent<Animator>();
            if (anim != null)
            {
                anim.runtimeAnimatorController = controller;
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
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
            "Walk" => new Vector3(580f, -40f, 0f),
            "Shoot" => new Vector3(580f, 100f, 0f),
            "Missile" => new Vector3(580f, 220f, 0f),
            "RamWindup" => new Vector3(820f, 100f, 0f),
            "Ram" => new Vector3(820f, -40f, 0f),
            "Hit" => new Vector3(300f, 240f, 0f),
            "Die" => new Vector3(50f, -120f, 0f),
            _ => new Vector3(200f, 200f, 0f)
        };

        foreach (var name in new[] { "Idle", "Walk", "Shoot", "Missile", "RamWindup", "Ram", "Hit", "Die" })
        {
            if (!map.ContainsKey(name))
                map[name] = sm.AddState(name, Pos(name));
        }

        return map;
    }

    static void EnsureParameter(AnimatorController controller, string name, AnimatorControllerParameterType type)
    {
        foreach (var p in controller.parameters)
        {
            if (p.name == name)
                return;
        }

        controller.AddParameter(name, type);
    }

    static void EnsureBoolTransition(
        AnimatorState source,
        AnimatorState dest,
        string param,
        bool value,
        bool? extraWalkTrue = null)
    {
        foreach (var t in source.transitions)
        {
            if (t.destinationState != dest || !HasBoolCondition(t, param, value))
                continue;

            if (!extraWalkTrue.HasValue)
                return;

            if (extraWalkTrue.Value && HasBoolCondition(t, "walk", true))
                return;
            if (!extraWalkTrue.Value && HasBoolCondition(t, "walk", false))
                return;
        }

        var nt = source.AddTransition(dest);
        nt.hasExitTime = false;
        nt.duration = 0f;
        nt.hasFixedDuration = true;
        nt.canTransitionToSelf = false;
        nt.AddCondition(value ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot, 0f, param);
        if (extraWalkTrue.HasValue)
        {
            nt.AddCondition(
                extraWalkTrue.Value ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot,
                0f,
                "walk");
        }
    }

    static void EnsureExitTimeTransition(AnimatorState source, AnimatorState dest, float exitTime)
    {
        foreach (var t in source.transitions)
        {
            if (t.destinationState == dest && t.hasExitTime)
                return;
        }

        var nt = source.AddTransition(dest);
        nt.hasExitTime = true;
        nt.exitTime = exitTime;
        nt.duration = 0f;
        nt.hasFixedDuration = true;
    }

    static void EnsureAnyStateBool(
        AnimatorStateMachine sm,
        AnimatorState dest,
        string param,
        bool value,
        bool canTransitionToSelf)
    {
        foreach (var t in sm.anyStateTransitions)
        {
            if (t.destinationState == dest && HasBoolCondition(t, param, value))
                return;
        }

        var nt = sm.AddAnyStateTransition(dest);
        nt.hasExitTime = false;
        nt.duration = 0f;
        nt.hasFixedDuration = true;
        nt.canTransitionToSelf = canTransitionToSelf;
        nt.AddCondition(value ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot, 0f, param);
    }

    static void EnsureAnyStateTrigger(AnimatorStateMachine sm, AnimatorState dest, string param)
    {
        foreach (var t in sm.anyStateTransitions)
        {
            if (t.destinationState == dest && HasTriggerCondition(t, param))
                return;
        }

        var nt = sm.AddAnyStateTransition(dest);
        nt.hasExitTime = false;
        nt.duration = 0f;
        nt.hasFixedDuration = true;
        nt.canTransitionToSelf = false;
        nt.AddCondition(AnimatorConditionMode.If, 0f, param);
    }

    static bool HasBoolCondition(AnimatorStateTransition t, string param, bool value)
    {
        var mode = value ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot;
        foreach (var c in t.conditions)
        {
            if (c.parameter == param && c.mode == mode)
                return true;
        }

        return false;
    }

    static bool HasTriggerCondition(AnimatorStateTransition t, string param)
    {
        foreach (var c in t.conditions)
        {
            if (c.parameter == param && c.mode == AnimatorConditionMode.If)
                return true;
        }

        return false;
    }
}
#endif
