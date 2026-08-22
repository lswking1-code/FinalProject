#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// 生成 / 修复直升机 Animator Controller（Idle / Fly / Shoot / ShootDown / Hit / Die）。
/// 参数与 HelicopterEnemy / FlyingEnemy 状态脚本对齐：walk / shoot / shootDown / hurt / dead。
/// Clip 可先留空，之后放到 Helicopter 目录再跑一次菜单即可挂上。
/// </summary>
public static class HelicopterAnimControllerSetup
{
    const string ControllerPath = "Assets/Animation/enemy_helicopter.controller";
    const string PrefabPath = "Assets/Prefabs/Enemy/Helicopter.prefab";

    const string IdleClipPath = "Assets/Animations/Enemy/Helicopter/Helicopter_idle.anim";
    const string FlyClipPath = "Assets/Animations/Enemy/Helicopter/Helicopter_fly.anim";
    const string ShootClipPath = "Assets/Animations/Enemy/Helicopter/Helicopter_shoot.anim";
    const string ShootDownClipPath = "Assets/Animations/Enemy/Helicopter/Helicopter_downshoot.anim";
    const string HitClipPath = "Assets/Animations/Enemy/Helicopter/Helicopter_hit.anim";
    const string DieClipPath = "Assets/Animations/Enemy/Helicopter/Helicopter_die.anim";

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

    [MenuItem("Lost Division/Create Helicopter Animator Controller")]
    public static void CreateHelicopterAnimatorController()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            return;

        CreateOrRebuildController(silent: false, assignPrefab: true);
    }

    static void CreateOrRebuildController(bool silent, bool assignPrefab)
    {
        var idle = AssetDatabase.LoadAssetAtPath<AnimationClip>(IdleClipPath);
        var fly = AssetDatabase.LoadAssetAtPath<AnimationClip>(FlyClipPath);
        var shoot = AssetDatabase.LoadAssetAtPath<AnimationClip>(ShootClipPath);
        var shootDown = AssetDatabase.LoadAssetAtPath<AnimationClip>(ShootDownClipPath);
        var hit = AssetDatabase.LoadAssetAtPath<AnimationClip>(HitClipPath);
        var die = AssetDatabase.LoadAssetAtPath<AnimationClip>(DieClipPath);

        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller == null)
            controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);

        EnsureParameter(controller, "walk", AnimatorControllerParameterType.Bool);
        EnsureParameter(controller, "shoot", AnimatorControllerParameterType.Bool);
        EnsureParameter(controller, "shootDown", AnimatorControllerParameterType.Bool);
        EnsureParameter(controller, "hurt", AnimatorControllerParameterType.Trigger);
        EnsureParameter(controller, "dead", AnimatorControllerParameterType.Bool);

        var sm = controller.layers[0].stateMachine;
        var states = EnsureStates(sm);
        states["Idle"].motion = idle;
        states["Fly"].motion = fly;
        states["Shoot"].motion = shoot;
        states["ShootDown"].motion = shootDown;
        states["Hit"].motion = hit;
        states["Die"].motion = die;
        sm.defaultState = states["Idle"];

        EnsureBoolTransition(states["Idle"], states["Fly"], "walk", true);
        EnsureBoolTransition(states["Fly"], states["Idle"], "walk", false);

        EnsureAnyStateBool(sm, states["Shoot"], "shoot", true, canTransitionToSelf: false);
        EnsureBoolTransition(states["Shoot"], states["Fly"], "shoot", false, extraWalkTrue: true);
        EnsureBoolTransition(states["Shoot"], states["Idle"], "shoot", false, extraWalkTrue: false);

        EnsureAnyStateBool(sm, states["ShootDown"], "shootDown", true, canTransitionToSelf: false);
        EnsureBoolTransition(states["ShootDown"], states["Fly"], "shootDown", false, extraWalkTrue: true);
        EnsureBoolTransition(states["ShootDown"], states["Idle"], "shootDown", false, extraWalkTrue: false);

        EnsureAnyStateTrigger(sm, states["Hit"], "hurt");
        EnsureExitTimeTransition(states["Hit"], states["Idle"], 0.9f);
        EnsureAnyStateBool(sm, states["Die"], "dead", true, canTransitionToSelf: false);

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();

        if (assignPrefab)
            AssignControllerToPrefab(controller);

        if (!silent)
            Debug.Log($"已创建/更新 {ControllerPath}。Clip 可稍后补到 Assets/Animations/Enemy/Helicopter/");
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
            "Fly" => new Vector3(580f, -40f, 0f),
            "Shoot" => new Vector3(580f, 100f, 0f),
            "ShootDown" => new Vector3(580f, 200f, 0f),
            "Hit" => new Vector3(300f, 240f, 0f),
            "Die" => new Vector3(50f, -120f, 0f),
            _ => new Vector3(200f, 200f, 0f)
        };

        foreach (var name in new[] { "Idle", "Fly", "Shoot", "ShootDown", "Hit", "Die" })
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
