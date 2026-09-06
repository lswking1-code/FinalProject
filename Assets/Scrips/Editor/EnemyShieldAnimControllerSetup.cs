#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// 生成 / 修复盾兵 Animator Controller（Idle / Run / Hit / ShieldHit / Die / Shooting）。
/// 参数与现有状态脚本对齐：walk / hurt / shieldHurt / dead / shoot。
/// </summary>
public static class EnemyShieldAnimControllerSetup
{
    const string ControllerPath = "Assets/Animation/enemy_shield.controller";
    const string PrefabPath = "Assets/Prefabs/Enemy/EnemyShield.prefab";
    const string IdleClipPath = "Assets/Arts/Enemies/enemy_shield_idle.anim";
    const string RunClipPath = "Assets/Arts/Enemies/enemy_shield_walk.anim";
    const string HitClipPath = "Assets/Arts/Enemies/enemy_shield_hurt.anim";
    const string ShieldHitClipPath = "Assets/Animations/Enemy/enemy_shield_Shurt.anim";
    const string ShootClipPath = "Assets/Animations/Enemy/enemy_shield_shooting.anim";
    const string ShootStateName = "enemy_shield_shooting";

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

    [MenuItem("Lost Division/Create Enemy Shield Animator Controller")]
    public static void CreateEnemyShieldAnimatorController()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            return;

        CreateOrRebuildController(silent: false, assignPrefab: true);
    }

    static void CreateOrRebuildController(bool silent, bool assignPrefab)
    {
        var idle = AssetDatabase.LoadAssetAtPath<AnimationClip>(IdleClipPath);
        var run = AssetDatabase.LoadAssetAtPath<AnimationClip>(RunClipPath);
        var hit = AssetDatabase.LoadAssetAtPath<AnimationClip>(HitClipPath);
        var shieldHit = AssetDatabase.LoadAssetAtPath<AnimationClip>(ShieldHitClipPath);
        var shoot = AssetDatabase.LoadAssetAtPath<AnimationClip>(ShootClipPath);
        var die = hit;

        if (shoot != null && shoot.legacy == false)
        {
            var settings = AnimationUtility.GetAnimationClipSettings(shoot);
            if (settings.loopTime)
            {
                settings.loopTime = false;
                AnimationUtility.SetAnimationClipSettings(shoot, settings);
                EditorUtility.SetDirty(shoot);
            }
        }

        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller == null)
            controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);

        EnsureParameter(controller, "walk", AnimatorControllerParameterType.Bool);
        EnsureParameter(controller, "hurt", AnimatorControllerParameterType.Trigger);
        EnsureParameter(controller, "shieldHurt", AnimatorControllerParameterType.Trigger);
        EnsureParameter(controller, "dead", AnimatorControllerParameterType.Bool);
        EnsureParameter(controller, "shoot", AnimatorControllerParameterType.Bool);

        var sm = controller.layers[0].stateMachine;
        var states = EnsureStates(sm);
        states["Idle"].motion = idle;
        states["Run"].motion = run;
        states["Hit"].motion = hit;
        states["ShieldHit"].motion = shieldHit;
        states["Die"].motion = die;
        if (states.TryGetValue(ShootStateName, out var shootState))
            shootState.motion = shoot;
        sm.defaultState = states["Idle"];

        EnsureBoolTransition(states["Idle"], states["Run"], "walk", true);
        EnsureBoolTransition(states["Run"], states["Idle"], "walk", false);
        EnsureAnyStateTrigger(sm, states["Hit"], "hurt");
        EnsureExitTimeTransition(states["Hit"], states["Idle"], 0.9f);
        EnsureAnyStateTrigger(sm, states["ShieldHit"], "shieldHurt");
        EnsureExitTimeTransition(states["ShieldHit"], states["Idle"], 0.9f);
        EnsureAnyStateBool(sm, states["Die"], "dead", true, canTransitionToSelf: false);
        if (states.TryGetValue(ShootStateName, out var wiredShoot))
        {
            EnsureAnyStateBool(sm, wiredShoot, "shoot", true, canTransitionToSelf: false);
            EnsureBoolTransition(wiredShoot, states["Idle"], "shoot", false);
            EnsureBoolTransition(states["ShieldHit"], wiredShoot, "shoot", true);
        }

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();

        if (assignPrefab)
            AssignControllerToPrefab(controller);

        if (!silent)
            Debug.Log($"已创建/更新 {ControllerPath}");
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
                anim.runtimeAnimatorController = controller;

            var shieldEnemy = root.GetComponent<ShieldEnemy>();
            if (shieldEnemy != null)
            {
                var melee = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(
                    "Assets/Animation/enemy_melee.controller");
                if (melee != null)
                    shieldEnemy.meleeAnimatorController = melee;
            }

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
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
            "Run" => new Vector3(580f, -40f, 0f),
            "Hit" => new Vector3(300f, 240f, 0f),
            "ShieldHit" => new Vector3(520f, 240f, 0f),
            "Die" => new Vector3(50f, -120f, 0f),
            ShootStateName => new Vector3(350f, -80f, 0f),
            _ => new Vector3(200f, 200f, 0f)
        };

        foreach (var name in new[] { "Idle", "Run", "Hit", "ShieldHit", "Die", ShootStateName })
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
