#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

public static class PlayerAnimControllerSetup
{
    const string UpPath = "Assets/Animation/up.controller";
    const string DownPath = "Assets/Animation/down.controller";

    static readonly string[] LookStateNames =
    {
        "LookUpStart", "LookUp", "LookUpEnd",
        "LookDownStart", "LookDown", "LookDownEnd",
    };

    const string LookUpShootStateName = "LookUpShoot";
    const string LookDownShootStateName = "LookDownShoot";
    const string ShootStateName = "Shoot";
    const string ShootTriggerParam = "Shoot";

    static readonly string[] LocomotionStateNames =
    {
        "Idle", "Run", "Jump", "Fall", "Leap", "LeapAir",
    };

    const string LookUpEndClipPath = "Assets/Arts/Metal Slug/lookup_end.anim";
    const string LookDownEndClipPath = "Assets/Arts/Metal Slug/lookdown_end.anim";
    const string FullBodyPath = "Assets/Animation/fullbody.controller";
    const string CrouchShootClipPath = "Assets/Arts/Metal Slug/crouch_shoot.anim";

    [InitializeOnLoadMethod]
    static void ScheduleFixup()
    {
        EditorApplication.delayCall += () =>
        {
            EnsureAirPhaseParameters();
            EnsureLookAnimatorParamDrivenTransitions();
            EnsureLookShootAnimatorTransitions();
        };
    }

    [MenuItem("Lost Division/Fix Player AirPhase Animator Parameters")]
    public static void EnsureAirPhaseParameters()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            return;

        bool changed = false;
        changed |= EnsureParameter(UpPath, "AirPhase", AnimatorControllerParameterType.Int);
        changed |= EnsureParameter(UpPath, "IsRun", AnimatorControllerParameterType.Bool);
        changed |= EnsureParameter(UpPath, "IsShoot", AnimatorControllerParameterType.Bool);
        changed |= EnsureParameter(UpPath, "Shoot", AnimatorControllerParameterType.Trigger);
        changed |= EnsureParameter(UpPath, "IsLookUp", AnimatorControllerParameterType.Bool);
        changed |= EnsureParameter(UpPath, "IsLookDown", AnimatorControllerParameterType.Bool);
        changed |= EnsureParameter(DownPath, "AirPhase", AnimatorControllerParameterType.Int);
        changed |= EnsureParameter(DownPath, "IsRun", AnimatorControllerParameterType.Bool);

        if (changed)
        {
            AssetDatabase.SaveAssets();
            Debug.Log("已为玩家 up/down Animator Controller 补全参数。");
        }
    }

    [MenuItem("Lost Division/Ensure Look Animator Param-Driven Transitions")]
    public static void EnsureLookAnimatorParamDrivenTransitions()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            return;

        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(UpPath);
        if (controller == null)
        {
            Debug.LogWarning($"未找到 Animator Controller: {UpPath}");
            return;
        }

        bool changed = false;
        changed |= EnsureParameter(UpPath, "IsLookUp", AnimatorControllerParameterType.Bool);
        changed |= EnsureParameter(UpPath, "IsLookDown", AnimatorControllerParameterType.Bool);

        var sm = controller.layers[0].stateMachine;
        var states = BuildStateMap(sm);

        changed |= EnsureStateMotion(states, "LookUpEnd", LookUpEndClipPath);
        changed |= EnsureStateMotion(states, "LookDownEnd", LookDownEndClipPath);

        changed |= EnsureStartLoopTransition(states, "LookUpStart", "LookUp");
        changed |= EnsureStartLoopTransition(states, "LookDownStart", "LookDown");

        foreach (var locoName in LocomotionStateNames)
        {
            changed |= EnsureBoolTransition(states, locoName, "LookUpStart", "IsLookUp", true);
            changed |= EnsureBoolTransition(states, locoName, "LookDownStart", "IsLookDown", true);
        }

        changed |= EnsureBoolTransition(states, "LookUpStart", "LookUpEnd", "IsLookUp", false);
        changed |= EnsureBoolTransition(states, "LookUp", "LookUpEnd", "IsLookUp", false);
        changed |= EnsureBoolTransition(states, "LookDownStart", "LookDownEnd", "IsLookDown", false);
        changed |= EnsureBoolTransition(states, "LookDown", "LookDownEnd", "IsLookDown", false);

        changed |= EnsureBoolTransition(states, "LookUpEnd", "LookUpStart", "IsLookUp", true);
        changed |= EnsureBoolTransition(states, "LookDownEnd", "LookDownStart", "IsLookDown", true);

        changed |= EnsureLookFalseOnAirPhaseAnyState(sm);
        changed |= DisableWriteDefaultValuesOnLookStates(states);
        changed |= DisableWriteDefaultValuesOnLocomotionStates(states);
        changed |= EnsureRunIdleRequiresNoLook(states);

        if (changed)
        {
            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            Debug.Log("已为 up.controller 配置 IsLookUp/IsLookDown 驱动的 Look 过渡。");
        }
    }

    [MenuItem("Lost Division/Ensure Look Shoot Animator Transitions")]
    public static void EnsureLookShootAnimatorTransitions()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            return;

        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(UpPath);
        if (controller == null)
        {
            Debug.LogWarning($"未找到 Animator Controller: {UpPath}");
            return;
        }

        bool changed = false;
        changed |= EnsureParameter(UpPath, "Shoot", AnimatorControllerParameterType.Trigger);

        var sm = controller.layers[0].stateMachine;
        var states = BuildStateMap(sm);

        changed |= EnsureTriggerTransition(states, "LookUp", LookUpShootStateName, ShootTriggerParam);
        changed |= EnsureTriggerTransition(states, "LookDown", LookDownShootStateName, ShootTriggerParam);
        changed |= EnsureTriggerTransition(states, "LookUpStart", LookUpShootStateName, ShootTriggerParam);
        changed |= EnsureTriggerTransition(states, "LookDownStart", LookDownShootStateName, ShootTriggerParam);

        changed |= EnsureExitTimeTransition(states, LookUpShootStateName, "LookUp", 0.95f);
        changed |= EnsureExitTimeTransition(states, LookDownShootStateName, "LookDown", 0.95f);

        changed |= EnsureTriggerSelfTransition(states, LookUpShootStateName, ShootTriggerParam);
        changed |= EnsureTriggerSelfTransition(states, LookDownShootStateName, ShootTriggerParam);

        changed |= EnsureTriggerBoolTransition(states, ShootStateName, LookUpShootStateName, ShootTriggerParam, "IsLookUp", true);
        changed |= EnsureTriggerBoolTransition(states, ShootStateName, LookDownShootStateName, ShootTriggerParam, "IsLookDown", true);

        changed |= EnsureShootFalseOnLookAnyState(sm);

        if (changed)
        {
            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            Debug.Log("已为 up.controller 配置 Look 射击的 Shoot Trigger 过渡。");
        }
    }

    [MenuItem("Lost Division/Ensure Shoot Animator States")]
    public static void EnsureShootAnimatorStates()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            return;

        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(FullBodyPath);
        if (controller == null)
        {
            Debug.LogWarning($"未找到 Animator Controller: {FullBodyPath}");
            return;
        }

        var sm = controller.layers[0].stateMachine;
        var states = BuildStateMap(sm);
        bool changed = EnsureStateMotion(states, "CrouchShoot", CrouchShootClipPath);

        if (!states.ContainsKey("CrouchShoot"))
        {
            var state = sm.AddState("CrouchShoot", new Vector3(600f, 300f, 0f));
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(CrouchShootClipPath);
            if (clip != null)
            {
                state.motion = clip;
                changed = true;
            }
        }

        if (changed)
        {
            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            Debug.Log("已为 fullbody.controller 配置 CrouchShoot 状态。");
        }
    }

    [MenuItem("Lost Division/Ensure Player Look End Motion Clips")]
    public static void EnsureLookEndMotionClips()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            return;

        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(UpPath);
        if (controller == null)
            return;

        var states = BuildStateMap(controller.layers[0].stateMachine);
        bool changed = EnsureStateMotion(states, "LookUpEnd", LookUpEndClipPath);
        changed |= EnsureStateMotion(states, "LookDownEnd", LookDownEndClipPath);

        if (changed)
        {
            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
        }
    }

    [MenuItem("Lost Division/Disable Upper AirPhase AnyState Self-Transition")]
    public static void DisableUpperAirPhaseAnyStateSelfTransition()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            return;

        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(UpPath);
        if (controller == null)
            return;

        var sm = controller.layers[0].stateMachine;
        bool changed = false;
        foreach (var transition in sm.anyStateTransitions)
        {
            if (!HasAirPhaseCondition(transition))
                continue;

            if (transition.canTransitionToSelf)
            {
                transition.canTransitionToSelf = false;
                changed = true;
            }
        }

        if (changed)
        {
            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
        }
    }

    static bool DisableWriteDefaultValuesOnLocomotionStates(Dictionary<string, AnimatorState> states)
    {
        bool changed = false;
        foreach (var stateName in LocomotionStateNames)
        {
            if (!states.TryGetValue(stateName, out var state))
                continue;

            if (!state.writeDefaultValues)
                continue;

            state.writeDefaultValues = false;
            changed = true;
        }

        return changed;
    }

    static bool EnsureRunIdleRequiresNoLook(Dictionary<string, AnimatorState> states)
    {
        if (!states.TryGetValue("Run", out var run))
            return false;

        bool changed = false;
        foreach (var transition in run.transitions)
        {
            if (transition.destinationState == null || transition.destinationState.name != "Idle")
                continue;

            changed |= EnsureCondition(transition, "IsLookUp", false);
            changed |= EnsureCondition(transition, "IsLookDown", false);
        }

        return changed;
    }

    static bool DisableWriteDefaultValuesOnLookStates(Dictionary<string, AnimatorState> states)
    {
        bool changed = false;
        foreach (var stateName in LookStateNames)
        {
            if (!states.TryGetValue(stateName, out var state))
                continue;

            if (!state.writeDefaultValues)
                continue;

            state.writeDefaultValues = false;
            changed = true;
        }

        return changed;
    }

    static bool EnsureLookFalseOnAirPhaseAnyState(AnimatorStateMachine sm)
    {
        bool changed = false;
        foreach (var transition in sm.anyStateTransitions)
        {
            if (!HasAirPhaseCondition(transition))
                continue;

            changed |= EnsureCondition(transition, "IsLookUp", false);
            changed |= EnsureCondition(transition, "IsLookDown", false);
        }

        return changed;
    }

    static bool EnsureCondition(AnimatorStateTransition transition, string param, bool value)
    {
        foreach (var condition in transition.conditions)
        {
            if (condition.parameter == param && (condition.mode == AnimatorConditionMode.If) == value)
                return false;
        }

        var conditions = new List<AnimatorCondition>(transition.conditions)
        {
            new AnimatorCondition
            {
                mode = value ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot,
                parameter = param,
                threshold = 0f,
            },
        };
        transition.conditions = conditions.ToArray();
        return true;
    }

    static bool EnsureShootFalseOnLookAnyState(AnimatorStateMachine sm)
    {
        bool changed = false;
        foreach (var transition in sm.anyStateTransitions)
        {
            if (!HasShootCondition(transition))
                continue;

            changed |= EnsureCondition(transition, "IsLookUp", false);
            changed |= EnsureCondition(transition, "IsLookDown", false);
        }

        return changed;
    }

    static bool HasShootCondition(AnimatorStateTransition transition)
    {
        foreach (var condition in transition.conditions)
        {
            if (condition.parameter == "IsShoot")
                return true;
        }

        return false;
    }

    static bool EnsureTriggerTransition(
        Dictionary<string, AnimatorState> states,
        string sourceName,
        string destName,
        string triggerParam)
    {
        if (!states.TryGetValue(sourceName, out var source) || !states.TryGetValue(destName, out var dest))
            return false;

        foreach (var existing in source.transitions)
        {
            if (existing.destinationState != dest)
                continue;

            foreach (var condition in existing.conditions)
            {
                if (condition.parameter == triggerParam)
                    return false;
            }
        }

        var transition = source.AddTransition(dest);
        transition.hasExitTime = false;
        transition.duration = 0.05f;
        transition.hasFixedDuration = true;
        transition.canTransitionToSelf = false;
        transition.AddCondition(AnimatorConditionMode.If, 0f, triggerParam);
        return true;
    }

    static bool EnsureExitTimeTransition(
        Dictionary<string, AnimatorState> states,
        string sourceName,
        string destName,
        float exitTime)
    {
        if (!states.TryGetValue(sourceName, out var source) || !states.TryGetValue(destName, out var dest))
            return false;

        foreach (var existing in source.transitions)
        {
            if (existing.destinationState != dest)
                continue;

            if (existing.hasExitTime && Mathf.Approximately(existing.exitTime, exitTime))
                return false;
        }

        var transition = source.AddTransition(dest);
        transition.hasExitTime = true;
        transition.exitTime = exitTime;
        transition.duration = 0.05f;
        transition.hasFixedDuration = true;
        transition.canTransitionToSelf = false;
        return true;
    }

    static bool EnsureTriggerBoolTransition(
        Dictionary<string, AnimatorState> states,
        string sourceName,
        string destName,
        string triggerParam,
        string boolParam,
        bool boolValue)
    {
        if (!states.TryGetValue(sourceName, out var source) || !states.TryGetValue(destName, out var dest))
            return false;

        foreach (var existing in source.transitions)
        {
            if (existing.destinationState != dest)
                continue;

            bool hasTrigger = false;
            bool hasBool = false;
            foreach (var condition in existing.conditions)
            {
                if (condition.parameter == triggerParam)
                    hasTrigger = true;
                if (condition.parameter == boolParam && (condition.mode == AnimatorConditionMode.If) == boolValue)
                    hasBool = true;
            }

            if (hasTrigger && hasBool)
                return false;
        }

        var transition = source.AddTransition(dest);
        transition.hasExitTime = false;
        transition.duration = 0.05f;
        transition.hasFixedDuration = true;
        transition.canTransitionToSelf = false;
        transition.AddCondition(AnimatorConditionMode.If, 0f, triggerParam);
        transition.AddCondition(boolValue ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot, 0f, boolParam);
        return true;
    }

    static bool EnsureTriggerSelfTransition(
        Dictionary<string, AnimatorState> states,
        string stateName,
        string triggerParam)
    {
        if (!states.TryGetValue(stateName, out var state))
            return false;

        foreach (var existing in state.transitions)
        {
            if (existing.destinationState != state)
                continue;

            foreach (var condition in existing.conditions)
            {
                if (condition.parameter == triggerParam)
                    return false;
            }
        }

        var transition = state.AddTransition(state);
        transition.hasExitTime = false;
        transition.duration = 0.05f;
        transition.hasFixedDuration = true;
        transition.canTransitionToSelf = true;
        transition.AddCondition(AnimatorConditionMode.If, 0f, triggerParam);
        return true;
    }

    static bool EnsureBoolTransition(
        Dictionary<string, AnimatorState> states,
        string sourceName,
        string destName,
        string param,
        bool value)
    {
        if (!states.TryGetValue(sourceName, out var source) || !states.TryGetValue(destName, out var dest))
            return false;

        foreach (var existing in source.transitions)
        {
            if (existing.destinationState != dest)
                continue;

            if (existing.conditions.Length != 1)
                continue;

            var condition = existing.conditions[0];
            if (condition.parameter == param && (condition.mode == AnimatorConditionMode.If) == value)
                return false;
        }

        var transition = source.AddTransition(dest);
        transition.hasExitTime = false;
        transition.duration = 0.05f;
        transition.hasFixedDuration = true;
        transition.canTransitionToSelf = false;
        transition.AddCondition(value ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot, 0f, param);
        return true;
    }

    static bool HasAirPhaseCondition(AnimatorStateTransition transition)
    {
        foreach (var condition in transition.conditions)
        {
            if (condition.parameter == "AirPhase")
                return true;
        }

        return false;
    }

    static Dictionary<string, AnimatorState> BuildStateMap(AnimatorStateMachine sm)
    {
        var map = new Dictionary<string, AnimatorState>();
        foreach (var child in sm.states)
            map[child.state.name] = child.state;
        return map;
    }

    static bool EnsureStartLoopTransition(Dictionary<string, AnimatorState> states, string startName, string loopName)
    {
        if (!states.TryGetValue(startName, out var start) || !states.TryGetValue(loopName, out var loop))
            return false;

        foreach (var transition in start.transitions)
        {
            if (transition.destinationState == loop)
                return false;
        }

        var t = start.AddTransition(loop);
        t.hasExitTime = true;
        t.exitTime = 0.75f;
        t.duration = 0.25f;
        t.hasFixedDuration = true;
        t.canTransitionToSelf = false;
        return true;
    }

    static bool EnsureStateMotion(Dictionary<string, AnimatorState> states, string stateName, string clipPath)
    {
        if (!states.TryGetValue(stateName, out var state))
            return false;

        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
        if (clip == null)
            return false;

        if (state.motion == clip)
            return false;

        state.motion = clip;
        return true;
    }

    static bool EnsureParameter(string assetPath, string name, AnimatorControllerParameterType type)
    {
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(assetPath);
        if (controller == null)
        {
            Debug.LogWarning($"未找到 Animator Controller: {assetPath}");
            return false;
        }

        foreach (var parameter in controller.parameters)
        {
            if (parameter.name == name)
                return false;
        }

        controller.AddParameter(name, type);
        EditorUtility.SetDirty(controller);
        return true;
    }
}
#endif
