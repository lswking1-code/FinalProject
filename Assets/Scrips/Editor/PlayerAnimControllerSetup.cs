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
    const string MeleeFullBodyPath = "Assets/Animation/meleeFullBody.controller";
    const string CrouchShootClipPath = "Assets/Arts/Metal Slug/crouch_shoot.anim";
    const string ThrowClipPath = "Assets/Arts/Metal Slug/throw.anim";
    const string AirThrowClipPath = "Assets/Arts/Metal Slug/air_throw.anim";
    const string CrouchThrowClipPath = "Assets/Arts/Metal Slug/crouch_throw.anim";
    const string MeleeClipPath = "Assets/Arts/Metal Slug/melee.anim";
    const string AirMeleeClipPath = "Assets/Arts/Metal Slug/air_melee.anim";
    const string CrouchMeleeClipPath = "Assets/Arts/Metal Slug/crouch_melee.anim";

    [InitializeOnLoadMethod]
    static void ScheduleFixup()
    {
        EditorApplication.delayCall += () =>
        {
            EnsureAirPhaseParameters();
            EnsureLookAnimatorParamDrivenTransitions();
            EnsureLookShootAnimatorTransitions();
            RepairAnimatorControllers();
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

    [MenuItem("Lost Division/Create Melee Full Body Animator Controller")]
    public static void CreateMeleeFullBodyAnimatorController()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            return;

        var existing = AssetDatabase.LoadAssetAtPath<AnimatorController>(MeleeFullBodyPath);
        if (existing != null)
        {
            if (!EditorUtility.DisplayDialog(
                    "Melee Full Body Controller",
                    "已存在 meleeFullBody.controller，是否重新生成（会覆盖状态与过渡，剪辑仍为空）？",
                    "重新生成",
                    "取消"))
                return;

            AssetDatabase.DeleteAsset(MeleeFullBodyPath);
        }

        var controller = AnimatorController.CreateAnimatorControllerAtPath(MeleeFullBodyPath);
        var sm = controller.layers[0].stateMachine;

        controller.AddParameter("AirPhase", AnimatorControllerParameterType.Int);
        controller.AddParameter("IsRun", AnimatorControllerParameterType.Bool);

        string[] locomotion = { "Idle", "Run", "Jump", "Fall", "Leap", "LeapAir" };
        string[] oneShots =
        {
            "Land", "Turn", "CrouchStart", "Crouch", "CrouchTurn", "CrouchMove",
            "Melee", "AirMelee", "CrouchMelee", "Die",
        };

        var states = new Dictionary<string, AnimatorState>();
        float x = 350f;
        float y = 70f;
        foreach (var name in locomotion)
        {
            var state = sm.AddState(name, new Vector3(x, y, 0f));
            state.motion = null;
            state.writeDefaultValues = false;
            states[name] = state;
            y += 90f;
        }

        x = 750f;
        y = -110f;
        foreach (var name in oneShots)
        {
            var state = sm.AddState(name, new Vector3(x, y, 0f));
            state.motion = null;
            state.writeDefaultValues = true;
            states[name] = state;
            y += 70f;
        }

        sm.defaultState = states["Idle"];

        AddBoolTransition(states["Idle"], states["Run"], "IsRun", true, 0.25f);
        AddBoolTransition(states["Run"], states["Idle"], "IsRun", false, 0.25f);

        AddAirPhaseAnyState(sm, states["Idle"], 0);
        AddAirPhaseAnyState(sm, states["Jump"], 1);
        AddAirPhaseAnyState(sm, states["Fall"], 2);
        AddAirPhaseAnyState(sm, states["Leap"], 3);
        AddAirPhaseAnyState(sm, states["LeapAir"], 4);

        var crouchStartToCrouch = states["CrouchStart"].AddTransition(states["Crouch"]);
        crouchStartToCrouch.hasExitTime = true;
        crouchStartToCrouch.exitTime = 0.85f;
        crouchStartToCrouch.duration = 0.1f;
        crouchStartToCrouch.hasFixedDuration = true;

        AddBoolTransition(states["Crouch"], states["CrouchMove"], "IsRun", true, 0.25f);
        AddBoolTransition(states["CrouchMove"], states["Crouch"], "IsRun", false, 0.25f);

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        Debug.Log($"已创建 {MeleeFullBodyPath}（状态齐全、剪辑为空）。请赋给 PlayerFullBodyAnim.bodyAnimator。");
    }

    static void AddBoolTransition(
        AnimatorState source,
        AnimatorState dest,
        string param,
        bool value,
        float duration)
    {
        var t = source.AddTransition(dest);
        t.hasExitTime = false;
        t.duration = duration;
        t.hasFixedDuration = true;
        t.canTransitionToSelf = false;
        t.AddCondition(value ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot, 0f, param);
    }

    static void AddAirPhaseAnyState(AnimatorStateMachine sm, AnimatorState dest, int phase)
    {
        var t = sm.AddAnyStateTransition(dest);
        t.hasExitTime = false;
        t.duration = 0.25f;
        t.hasFixedDuration = true;
        t.canTransitionToSelf = false;
        t.AddCondition(AnimatorConditionMode.Equals, phase, "AirPhase");
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

    const string ShootClipPath = "Assets/Arts/Metal Slug/shoot.anim";
    const string LookUpShootClipPath = "Assets/Arts/Metal Slug/lookup_shoot.anim";
    const string LookDownShootClipPath = "Assets/Arts/Metal Slug/lookdown_shoot.anim";

    const string MachinistUpMPath = "Assets/Animations/Machinist/upM.controller";
    const string MachinistDownMPath = "Assets/Animations/Machinist/downM.controller";
    const string MachinistFullBodyMPath = "Assets/Animations/Machinist/fullbodyM.controller";
    const string MachinistExComboClipPath = "Assets/Animations/Machinist/EXShoot/EXcombo_shoot.anim";
    const string MachinistLookUpExComboClipPath = "Assets/Animations/Machinist/EXShoot/lookup_EXcombo_shoot.anim";
    const string MachinistLookDownExComboClipPath = "Assets/Animations/Machinist/EXShoot/lookdown_EXcombo_shoot.anim";
    const string MachinistCrouchExComboClipPath = "Assets/Animations/Machinist/EXShoot/crouch_EXcombo_shoot.anim";
    const string MachinistChargeShootClipPath = "Assets/Animations/Machinist/EXShoot/charge_shoot.anim";
    const string MachinistLookUpChargeStartClipPath = "Assets/Animations/Machinist/EXShoot/lookup_charge_start.anim";
    const string MachinistLookUpChargeLoopClipPath = "Assets/Animations/Machinist/EXShoot/lookup_charge_loop.anim";
    const string MachinistLookUpChargeShootClipPath = "Assets/Animations/Machinist/EXShoot/lookup_charge_shoot.anim";
    const string MachinistLookDownChargeStartClipPath = "Assets/Animations/Machinist/EXShoot/lookdown_charge_start.anim";
    const string MachinistLookDownChargeLoopClipPath = "Assets/Animations/Machinist/EXShoot/lookdown_charge_loop.anim";
    const string MachinistLookDownChargeShootClipPath = "Assets/Animations/Machinist/EXShoot/lookdown_charge_shoot.anim";
    const string MachinistCrouchChargeShootClipPath = "Assets/Animations/Machinist/EXShoot/crouch_charge_shoot.anim";
    const string MachinistCrouchShootClipPath = "Assets/Arts/Metal Slug/crouch_shoot.anim";
    const string MachinistLoadBulletClipPath = "Assets/Animations/Machinist/M_LoadBullet.anim";

    const string ComboShootStateName = "ComboShoot";
    const string LookUpComboShootStateName = "LookUpComboShoot";
    const string LookDownComboShootStateName = "LookDownComboShoot";
    const string CrouchComboShootStateName = "CrouchComboShoot";
    const string LoadBulletStateName = "LoadBullet";
    const string CrouchStateName = "Crouch";
    const string ChargeStartStateName = "ChargeStart";
    const string ChargeLoopStateName = "ChargeLoop";
    const string ChargeShootStateName = "ChargeShoot";
    const string LookUpChargeStartStateName = "LookUpChargeStart";
    const string LookUpChargeLoopStateName = "LookUpChargeLoop";
    const string LookUpChargeShootStateName = "LookUpChargeShoot";
    const string LookDownChargeStartStateName = "LookDownChargeStart";
    const string LookDownChargeLoopStateName = "LookDownChargeLoop";
    const string LookDownChargeShootStateName = "LookDownChargeShoot";
    const string IsChargingParam = "IsCharging";

    [MenuItem("Lost Division/Ensure Machinist Shoot Animator States")]
    public static void EnsureMachinistShootAnimatorStates()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            return;

        bool changed = false;
        changed |= EnsureMachinistUpControllerStates();
        changed |= EnsureMachinistDownControllerStates();
        changed |= EnsureMachinistFullBodyControllerStates();

        if (changed)
        {
            AssetDatabase.SaveAssets();
            Debug.Log("已为 Machinist 动画机配置射击状态：upM.controller（上半身射击/连击/蓄力）、fullbodyM.controller（蹲姿）。");
        }
    }

    static bool EnsureMachinistUpControllerStates()
    {
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(MachinistUpMPath);
        if (controller == null)
        {
            Debug.LogWarning($"未找到 Animator Controller: {MachinistUpMPath}");
            return false;
        }

        bool changed = false;
        changed |= EnsureParameter(MachinistUpMPath, IsChargingParam, AnimatorControllerParameterType.Bool);

        var sm = controller.layers[0].stateMachine;
        var states = BuildStateMap(sm);

        changed |= EnsureMachinistState(sm, states, ComboShootStateName, MachinistExComboClipPath, new Vector3(900f, 0f, 0f));
        changed |= EnsureMachinistState(sm, states, LookUpComboShootStateName, MachinistLookUpExComboClipPath, new Vector3(900f, 120f, 0f));
        changed |= EnsureMachinistState(sm, states, LookDownComboShootStateName, MachinistLookDownExComboClipPath, new Vector3(900f, -120f, 0f));
        changed |= EnsureMachinistState(sm, states, ChargeStartStateName, ShootClipPath, new Vector3(1050f, 0f, 0f));
        changed |= EnsureMachinistState(sm, states, ChargeLoopStateName, ShootClipPath, new Vector3(1200f, 0f, 0f));
        changed |= EnsureMachinistState(sm, states, ChargeShootStateName, MachinistChargeShootClipPath, new Vector3(1350f, 0f, 0f));
        changed |= EnsureMachinistState(sm, states, LookUpChargeStartStateName, MachinistLookUpChargeStartClipPath, new Vector3(1050f, 120f, 0f));
        changed |= EnsureMachinistState(sm, states, LookUpChargeLoopStateName, MachinistLookUpChargeLoopClipPath, new Vector3(1200f, 120f, 0f));
        changed |= EnsureMachinistState(sm, states, LookUpChargeShootStateName, MachinistLookUpChargeShootClipPath, new Vector3(1350f, 120f, 0f));
        changed |= EnsureMachinistState(sm, states, LookDownChargeStartStateName, MachinistLookDownChargeStartClipPath, new Vector3(1050f, -120f, 0f));
        changed |= EnsureMachinistState(sm, states, LookDownChargeLoopStateName, MachinistLookDownChargeLoopClipPath, new Vector3(1200f, -120f, 0f));
        changed |= EnsureMachinistState(sm, states, LookDownChargeShootStateName, MachinistLookDownChargeShootClipPath, new Vector3(1350f, -120f, 0f));
        changed |= EnsureMachinistState(sm, states, LoadBulletStateName, MachinistLoadBulletClipPath, new Vector3(900f, 200f, 0f));

        states = BuildStateMap(sm);
        changed |= EnsureExitTimeTransition(states, ChargeStartStateName, ChargeLoopStateName, 0.95f);
        changed |= EnsureExitTimeTransition(states, LookUpChargeStartStateName, LookUpChargeLoopStateName, 0.95f);
        changed |= EnsureExitTimeTransition(states, LookDownChargeStartStateName, LookDownChargeLoopStateName, 0.95f);
        changed |= EnsureTriggerTransition(states, "LookUp", LookUpComboShootStateName, ShootTriggerParam);
        changed |= EnsureTriggerTransition(states, "LookDown", LookDownComboShootStateName, ShootTriggerParam);
        changed |= EnsureTriggerSelfTransition(states, ComboShootStateName, ShootTriggerParam);
        changed |= EnsureTriggerSelfTransition(states, LookUpComboShootStateName, ShootTriggerParam);
        changed |= EnsureTriggerSelfTransition(states, LookDownComboShootStateName, ShootTriggerParam);

        if (changed)
            EditorUtility.SetDirty(controller);

        return changed;
    }

    static bool EnsureMachinistDownControllerStates()
    {
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(MachinistDownMPath);
        if (controller == null)
        {
            Debug.LogWarning($"未找到 Animator Controller: {MachinistDownMPath}");
            return false;
        }

        // 下半身仅 locomotion（Idle/Run/Jump/Fall），射击状态在 upM.controller
        bool changed = false;
        changed |= EnsureParameter(MachinistDownMPath, "AirPhase", AnimatorControllerParameterType.Int);
        changed |= EnsureParameter(MachinistDownMPath, "IsRun", AnimatorControllerParameterType.Bool);

        if (changed)
            EditorUtility.SetDirty(controller);

        return changed;
    }

    static bool EnsureMachinistFullBodyControllerStates()
    {
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(MachinistFullBodyMPath);
        if (controller == null)
        {
            Debug.LogWarning($"未找到 Animator Controller: {MachinistFullBodyMPath}");
            return false;
        }

        var sm = controller.layers[0].stateMachine;
        var states = BuildStateMap(sm);

        bool changed = false;
        changed |= EnsureParameter(MachinistFullBodyMPath, IsChargingParam, AnimatorControllerParameterType.Bool);
        changed |= EnsureMachinistState(sm, states, CrouchComboShootStateName, MachinistCrouchExComboClipPath, new Vector3(750f, 300f, 0f));
        changed |= EnsureMachinistState(sm, states, ChargeStartStateName, MachinistCrouchShootClipPath, new Vector3(900f, 300f, 0f));
        changed |= EnsureMachinistState(sm, states, ChargeLoopStateName, MachinistCrouchShootClipPath, new Vector3(1050f, 300f, 0f));
        changed |= EnsureMachinistState(sm, states, ChargeShootStateName, MachinistCrouchChargeShootClipPath, new Vector3(1200f, 300f, 0f));
        changed |= EnsureMachinistState(sm, states, LoadBulletStateName, MachinistLoadBulletClipPath, new Vector3(870f, 100f, 0f));

        states = BuildStateMap(sm);
        changed |= EnsureExitTimeTransition(states, ChargeStartStateName, ChargeLoopStateName, 0.95f);
        changed |= EnsureExitTimeTransition(states, CrouchComboShootStateName, CrouchStateName, 0.95f);

        if (changed)
            EditorUtility.SetDirty(controller);

        return changed;
    }

    static bool EnsureMachinistState(
        AnimatorStateMachine sm,
        Dictionary<string, AnimatorState> states,
        string stateName,
        string clipPath,
        Vector3 position)
    {
        bool changed = false;
        if (!states.ContainsKey(stateName))
        {
            var state = sm.AddState(stateName, position);
            states[stateName] = state;
            changed = true;
        }

        // clipPath 为空：只建状态占位，不挂 motion（后续补剪辑）
        if (!string.IsNullOrEmpty(clipPath))
            changed |= EnsureStateMotion(states, stateName, clipPath);
        return changed;
    }

    static readonly string[] RepairControllerPaths =
    {
        "Assets/Animation/explosion.controller",
        "Assets/Animation/grenade.controller",
        UpPath,
        FullBodyPath,
        DownPath,
    };

    [MenuItem("Lost Division/Repair Animator Controllers")]
    public static void RepairAnimatorControllers()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            return;

        bool changed = false;
        foreach (var path in RepairControllerPaths)
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            if (controller == null)
                continue;

            bool controllerChanged = false;
            foreach (var layer in controller.layers)
                controllerChanged |= RepairStateMachine(layer.stateMachine, path);

            if (controllerChanged)
            {
                EditorUtility.SetDirty(controller);
                changed = true;
            }
        }

        EnsureThrowAnimatorStates();
        EnsureMeleeAnimatorStates();

        if (changed)
        {
            AssetDatabase.SaveAssets();
            Debug.Log("已修复 Animator Controller 中的无效过渡。");
        }
    }

    [MenuItem("Lost Division/Ensure Throw Animator States")]
    public static void EnsureThrowAnimatorStates()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            return;

        bool changed = false;

        var upController = AssetDatabase.LoadAssetAtPath<AnimatorController>(UpPath);
        if (upController != null)
        {
            var upSm = upController.layers[0].stateMachine;
            changed |= EnsureDirectPlayState(upSm, "Throw", ThrowClipPath, new Vector3(800f, 400f, 0f));
            changed |= EnsureDirectPlayState(upSm, "AirThrow", AirThrowClipPath, new Vector3(800f, 500f, 0f));

            if (changed)
                EditorUtility.SetDirty(upController);
        }

        var fullBodyController = AssetDatabase.LoadAssetAtPath<AnimatorController>(FullBodyPath);
        if (fullBodyController != null)
        {
            var sm = fullBodyController.layers[0].stateMachine;
            bool fullBodyChanged = EnsureDirectPlayState(sm, "CrouchThrow", CrouchThrowClipPath, new Vector3(600f, 400f, 0f));

            if (fullBodyChanged)
            {
                EditorUtility.SetDirty(fullBodyController);
                changed = true;
            }
        }

        if (changed)
        {
            AssetDatabase.SaveAssets();
            Debug.Log("已为 up/fullbody.controller 配置投掷动画状态。");
        }
    }

    [MenuItem("Lost Division/Ensure Melee Animator States")]
    public static void EnsureMeleeAnimatorStates()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            return;

        bool changed = false;

        var upController = AssetDatabase.LoadAssetAtPath<AnimatorController>(UpPath);
        if (upController != null)
        {
            var upSm = upController.layers[0].stateMachine;
            changed |= EnsureDirectPlayState(upSm, "Melee", MeleeClipPath, new Vector3(900f, 400f, 0f));
            changed |= EnsureDirectPlayState(upSm, "AirMelee", AirMeleeClipPath, new Vector3(900f, 500f, 0f));

            if (changed)
                EditorUtility.SetDirty(upController);
        }

        var fullBodyController = AssetDatabase.LoadAssetAtPath<AnimatorController>(FullBodyPath);
        if (fullBodyController != null)
        {
            var sm = fullBodyController.layers[0].stateMachine;
            bool fullBodyChanged = EnsureDirectPlayState(sm, "CrouchMelee", CrouchMeleeClipPath, new Vector3(600f, 500f, 0f));

            if (fullBodyChanged)
            {
                EditorUtility.SetDirty(fullBodyController);
                changed = true;
            }
        }

        if (changed)
        {
            AssetDatabase.SaveAssets();
            Debug.Log("已为 up/fullbody.controller 配置近战动画状态。");
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

    static bool RepairStateMachine(AnimatorStateMachine sm, string assetPath)
    {
        bool changed = false;

        for (int i = sm.anyStateTransitions.Length - 1; i >= 0; i--)
        {
            var transition = sm.anyStateTransitions[i];
            if (!IsBrokenTransition(transition))
                continue;

            sm.RemoveAnyStateTransition(transition);
            Object.DestroyImmediate(transition, true);
            changed = true;
            Debug.LogWarning($"已从 {assetPath} 移除损坏的 Any State 过渡。", sm);
        }

        foreach (var child in sm.states)
        {
            var state = child.state;
            for (int i = state.transitions.Length - 1; i >= 0; i--)
            {
                var transition = state.transitions[i];
                if (!IsBrokenTransition(transition))
                    continue;

                state.RemoveTransition(transition);
                Object.DestroyImmediate(transition, true);
                changed = true;
                Debug.LogWarning($"已从 {assetPath}/{state.name} 移除损坏的过渡。", state);
            }
        }

        foreach (var child in sm.stateMachines)
            changed |= RepairStateMachine(child.stateMachine, assetPath);

        return changed;
    }

    static bool IsBrokenTransition(AnimatorStateTransition transition)
    {
        if (transition == null)
            return true;

        if (transition.isExit)
            return false;

        if (transition.destinationStateMachine != null)
            return false;

        return transition.destinationState == null;
    }

    static bool EnsureDirectPlayState(
        AnimatorStateMachine sm,
        string stateName,
        string clipPath,
        Vector3 position)
    {
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
        if (clip == null)
            return false;

        var states = BuildStateMap(sm);
        if (states.TryGetValue(stateName, out var existing))
        {
            if (existing.motion == clip && existing.transitions.Length == 0)
                return false;

            sm.RemoveState(existing);
        }

        var state = sm.AddState(stateName, position);
        state.motion = clip;
        return true;
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
