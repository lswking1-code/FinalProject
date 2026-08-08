#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// 生成 / 修复近战敌人 Animator Controller（Idle / Run / Melee / Hit / Die）。
/// </summary>
public static class EnemyMeleeAnimControllerSetup
{
    const string ControllerPath = "Assets/Animation/enemy_melee.controller";
    const string IdleClipPath = "Assets/Arts/Metal Slug/enemy/enemy_rifle_idle.anim";
    const string RunClipPath = "Assets/Arts/Metal Slug/enemy/enemy_rifle_run.anim";
    const string MeleeClipPath = "Assets/Arts/Metal Slug/enemy/enemy_rifle_melee.anim";
    const string HitClipPath = "Assets/Arts/Metal Slug/enemy/enemy_hit.anim";
    const string DieClipPath = "Assets/Arts/Metal Slug/enemy/enemy_die.anim";

    [InitializeOnLoadMethod]
    static void AutoEnsure()
    {
        EditorApplication.delayCall += () =>
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return;

            if (AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath) == null)
                CreateOrRebuildController(silent: true);
        };
    }

    [MenuItem("Lost Division/Create Enemy Melee Animator Controller")]
    public static void CreateEnemyMeleeAnimatorController()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            return;

        CreateOrRebuildController(silent: false);
    }

    static void CreateOrRebuildController(bool silent)
    {
        var idle = AssetDatabase.LoadAssetAtPath<AnimationClip>(IdleClipPath);
        var run = AssetDatabase.LoadAssetAtPath<AnimationClip>(RunClipPath);
        var melee = AssetDatabase.LoadAssetAtPath<AnimationClip>(MeleeClipPath);
        var hit = AssetDatabase.LoadAssetAtPath<AnimationClip>(HitClipPath);
        var die = AssetDatabase.LoadAssetAtPath<AnimationClip>(DieClipPath);

        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller == null)
            controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);

        EnsureParameter(controller, "walk", AnimatorControllerParameterType.Bool);
        EnsureParameter(controller, "melee", AnimatorControllerParameterType.Bool);
        EnsureParameter(controller, "meleeWindup", AnimatorControllerParameterType.Bool);
        EnsureParameter(controller, "hurt", AnimatorControllerParameterType.Trigger);
        EnsureParameter(controller, "dead", AnimatorControllerParameterType.Bool);

        var sm = controller.layers[0].stateMachine;
        var states = EnsureStates(sm);
        states["Idle"].motion = idle;
        states["Run"].motion = run;
        states["Melee"].motion = melee;
        states["Hit"].motion = hit;
        states["Die"].motion = die;
        sm.defaultState = states["Idle"];

        EnsureBoolTransition(states["Idle"], states["Run"], "walk", true);
        EnsureBoolTransition(states["Run"], states["Idle"], "walk", false);
        EnsureAnyStateBool(sm, states["Melee"], "melee", true, canTransitionToSelf: false);
        EnsureBoolTransition(states["Melee"], states["Idle"], "melee", false);
        EnsureAnyStateTrigger(sm, states["Hit"], "hurt");
        EnsureExitTimeTransition(states["Hit"], states["Idle"], 0.9f);
        EnsureAnyStateBool(sm, states["Die"], "dead", true, canTransitionToSelf: false);

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();

        if (!silent)
            Debug.Log($"已创建/更新 {ControllerPath}");
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
            "Run" => new Vector3(580f, -40f, 0f),
            "Melee" => new Vector3(580f, 100f, 0f),
            "Hit" => new Vector3(300f, 240f, 0f),
            "Die" => new Vector3(50f, -120f, 0f),
            _ => new Vector3(200f, 200f, 0f)
        };

        foreach (var name in new[] { "Idle", "Run", "Melee", "Hit", "Die" })
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

    static void EnsureBoolTransition(AnimatorState source, AnimatorState dest, string param, bool value)
    {
        foreach (var t in source.transitions)
        {
            if (t.destinationState == dest && HasBoolCondition(t, param, value))
                return;
        }

        var nt = source.AddTransition(dest);
        nt.hasExitTime = false;
        nt.duration = 0f;
        nt.hasFixedDuration = true;
        nt.canTransitionToSelf = false;
        nt.AddCondition(value ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot, 0f, param);
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
