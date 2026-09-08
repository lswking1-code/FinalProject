#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.Events;
using UnityEngine;
/// <summary>
/// 接线 AE-74 Animator，并补齐预制体 2D 战斗组件 / 冲击波 Prefab。
/// </summary>
public static class AE74AnimControllerSetup
{
    const string ControllerPath = "Assets/Animations/Enemy/AE74/AE-74.controller";
    const string ClipFolder = "Assets/Animations/Enemy/AE74";
    const string PrefabPath = "Assets/Prefabs/Enemy/AE-74.prefab";
    const string ShockwavePath = "Assets/Prefabs/Bullets/AE74Shockwave.prefab";
    const string DieClipPath = ClipFolder + "/RobotWhite_Die.anim";
    const string IdleClipPath = ClipFolder + "/RobotWhite_Idle.anim";

    const string BulletPath = "Assets/Prefabs/Bullets/Bullet.prefab";
    const string MissilePath = "Assets/Prefabs/Bullets/EnemyMissile.prefab";
    const string HomingPath = "Assets/Prefabs/Bullets/EnemyHomingMissile.prefab";

    [InitializeOnLoadMethod]
    static void AutoEnsure()
    {
        EditorApplication.delayCall += () =>
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return;

            EnsureAll(silent: true);
        };
    }

    [MenuItem("Lost Division/Create AE-74 Animator Controller")]
    public static void CreateAE74AnimatorController()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            return;

        EnsureAll(silent: false);
    }

    static void EnsureAll(bool silent)
    {
        EnsureController();
        EnsureShockwavePrefab();
        EnsureEnemyPrefab();

        if (!silent)
            Debug.Log("已更新 AE-74 Animator / Prefab / 冲击波。");
    }

    static void EnsureController()
    {
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller == null)
            return;

        var idle = AssetDatabase.LoadAssetAtPath<AnimationClip>(IdleClipPath);
        var die = EnsureDieClip();

        EnsureParameter(controller, "walk", AnimatorControllerParameterType.Bool);
        EnsureParameter(controller, "dashStart", AnimatorControllerParameterType.Bool);
        EnsureParameter(controller, "dash", AnimatorControllerParameterType.Bool);
        EnsureParameter(controller, "shoot", AnimatorControllerParameterType.Bool);
        EnsureParameter(controller, "melee", AnimatorControllerParameterType.Bool);
        EnsureParameter(controller, "jump", AnimatorControllerParameterType.Bool);
        EnsureParameter(controller, "fly", AnimatorControllerParameterType.Bool);
        EnsureParameter(controller, "airAttack", AnimatorControllerParameterType.Bool);
        EnsureParameter(controller, "landStart", AnimatorControllerParameterType.Bool);
        EnsureParameter(controller, "landEnd", AnimatorControllerParameterType.Bool);
        EnsureParameter(controller, "hurt", AnimatorControllerParameterType.Trigger);
        EnsureParameter(controller, "dead", AnimatorControllerParameterType.Bool);

        var sm = controller.layers[0].stateMachine;
        var states = CollectStates(sm);
        EnsureState(sm, states, "Die", new Vector3(50f, -80f, 0f));
        EnsureState(sm, states, "Hit", new Vector3(50f, 220f, 0f));

        if (states.TryGetValue("Die", out var dieState) && die != null)
            dieState.motion = die;
        if (states.TryGetValue("Hit", out var hitState) && idle != null)
            hitState.motion = idle;
        if (states.TryGetValue("RobotWhite_Idle", out var idleState))
            sm.defaultState = idleState;

        if (states.TryGetValue("RobotWhite_Idle", out idleState)
            && states.TryGetValue("RobotWhite_Run", out var runState))
        {
            EnsureBoolTransition(idleState, runState, "walk", true);
            EnsureBoolTransition(runState, idleState, "walk", false);
        }

        WireAction(sm, states, "RobotWhite_Dash_Start", "dashStart");
        WireAction(sm, states, "RobotWhite_Dash_Loop", "dash");
        WireAction(sm, states, "RobotWhite_Shoot", "shoot");
        WireAction(sm, states, "RobotWhite_MeleeAttack", "melee");
        WireAction(sm, states, "RobotWhite_Jump", "jump");
        WireAction(sm, states, "RobotWhite_Fly", "fly");
        WireAction(sm, states, "RobotWhite_AirAttack", "airAttack");
        WireAction(sm, states, "RobotWhite_LandAttack_Start", "landStart");
        WireAction(sm, states, "RobotWhite_LandAttack_End", "landEnd");

        if (states.TryGetValue("RobotWhite_Dash_Start", out var dashStart)
            && states.TryGetValue("RobotWhite_Dash_Loop", out var dashLoop))
            EnsureBoolTransition(dashStart, dashLoop, "dash", true);

        if (states.TryGetValue("Hit", out hitState))
        {
            EnsureAnyStateTrigger(sm, hitState, "hurt");
            if (states.TryGetValue("RobotWhite_Idle", out idleState))
                EnsureExitTimeTransition(hitState, idleState, 0.9f);
        }

        if (states.TryGetValue("Die", out dieState))
            EnsureAnyStateBool(sm, dieState, "dead", true, canTransitionToSelf: false);

        // Shoot / AirAttack 只关键帧 Gun 位置；Write Defaults 会把 Idle 的炮口旋转写回去，盖掉脚本瞄准
        DisableWriteDefaults(states, "RobotWhite_Shoot");
        DisableWriteDefaults(states, "RobotWhite_AirAttack");

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
    }

    static AnimationClip EnsureDieClip()
    {
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(DieClipPath);
        if (clip == null)
        {
            clip = new AnimationClip
            {
                name = "RobotWhite_Die",
                frameRate = 12
            };
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = false;
            settings.stopTime = 0.75f;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            AssetDatabase.CreateAsset(clip, DieClipPath);
        }

        var events = AnimationUtility.GetAnimationEvents(clip);
        bool hasDestroy = false;
        for (int i = 0; i < events.Length; i++)
        {
            if (events[i].functionName == "DestroyAfterAnimation")
                hasDestroy = true;
        }

        if (!hasDestroy)
        {
            var list = new List<AnimationEvent>(events)
            {
                new AnimationEvent
                {
                    time = 0.74f,
                    functionName = "DestroyAfterAnimation"
                }
            };
            AnimationUtility.SetAnimationEvents(clip, list.ToArray());
            EditorUtility.SetDirty(clip);
        }

        return clip;
    }

    static void WireAction(AnimatorStateMachine sm, Dictionary<string, AnimatorState> states, string stateName, string param)
    {
        if (!states.TryGetValue(stateName, out var state))
            return;

        EnsureAnyStateBool(sm, state, param, true, canTransitionToSelf: false);
        if (states.TryGetValue("RobotWhite_Run", out var run))
            EnsureBoolTransition(state, run, param, false, extraWalkTrue: true);
        if (states.TryGetValue("RobotWhite_Idle", out var idle))
            EnsureBoolTransition(state, idle, param, false, extraWalkTrue: false);
    }

    static Dictionary<string, AnimatorState> CollectStates(AnimatorStateMachine sm)
    {
        var map = new Dictionary<string, AnimatorState>();
        foreach (var child in sm.states)
        {
            if (child.state != null && !string.IsNullOrEmpty(child.state.name))
                map[child.state.name] = child.state;
        }

        return map;
    }

    static void DisableWriteDefaults(Dictionary<string, AnimatorState> states, string name)
    {
        if (states.TryGetValue(name, out var state) && state != null)
            state.writeDefaultValues = false;
    }

    static void EnsureState(AnimatorStateMachine sm, Dictionary<string, AnimatorState> map, string name, Vector3 pos)
    {
        if (map.ContainsKey(name))
            return;

        map[name] = sm.AddState(name, pos);
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

    static void EnsureShockwavePrefab()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(ShockwavePath) != null)
            return;

        var go = new GameObject("AE74Shockwave");
        go.layer = LayerMask.NameToLayer("EnemyBullet");
        var rb = go.AddComponent<Rigidbody2D>();
        rb.gravityScale = 0f;
        rb.constraints = RigidbodyConstraints2D.FreezeRotation;
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

        var box = go.AddComponent<BoxCollider2D>();
        box.isTrigger = true;
        box.size = new Vector2(1.2f, 0.32f);

        var sr = go.AddComponent<SpriteRenderer>();
        sr.color = new Color(1f, 0.85f, 0.35f, 0.9f);
        sr.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Background.psd");
        sr.sortingLayerName = "Default";
        go.transform.localScale = new Vector3(1.2f, 0.28f, 1f);

        var attack = go.AddComponent<Attack>();
        attack.damage = 12;
        attack.attackType = AttackType.Melee;
        attack.requireTag = "Player";

        go.AddComponent<AE74Shockwave>();

        if (!AssetDatabase.IsValidFolder("Assets/Prefabs/Bullets"))
        {
            if (!AssetDatabase.IsValidFolder("Assets/Prefabs"))
                AssetDatabase.CreateFolder("Assets", "Prefabs");
            AssetDatabase.CreateFolder("Assets/Prefabs", "Bullets");
        }

        PrefabUtility.SaveAsPrefabAsset(go, ShockwavePath);
        Object.DestroyImmediate(go);
    }

    static void EnsureEnemyPrefab()
    {
        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        var existingRobot = existing != null ? existing.GetComponent<AE74Enemy>() : null;
        if (existingRobot != null
            && existing.GetComponent<Rigidbody2D>() != null
            && existingRobot.projectilePrefab != null
            && existingRobot.shockwavePrefab != null)
            return;

        var root = PrefabUtility.LoadPrefabContents(PrefabPath);
        if (root == null)
            return;

        try
        {
            root.tag = "Enemy";
            int enemyLayer = LayerMask.NameToLayer("Enemy");
            if (enemyLayer >= 0)
                SetLayerRecursively(root, enemyLayer);

            var col3d = root.GetComponent<CapsuleCollider>();
            if (col3d != null)
                Object.DestroyImmediate(col3d);

            var capsule = root.GetComponent<CapsuleCollider2D>();
            if (capsule == null)
                capsule = root.AddComponent<CapsuleCollider2D>();
            capsule.direction = CapsuleDirection2D.Vertical;
            capsule.size = new Vector2(1.54f, 2.7f);
            capsule.offset = new Vector2(0.1f, 0.13f);
            capsule.isTrigger = false;

            var hurt = root.GetComponent<CircleCollider2D>();
            if (hurt == null)
                hurt = root.AddComponent<CircleCollider2D>();
            hurt.isTrigger = true;
            hurt.radius = 0.95f;
            hurt.offset = new Vector2(0.1f, 0.13f);

            var rb = root.GetComponent<Rigidbody2D>();
            if (rb == null)
                rb = root.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Dynamic;
            rb.freezeRotation = true;
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            rb.gravityScale = 1f;
            rb.constraints = RigidbodyConstraints2D.FreezeRotation;

            var physics = root.GetComponent<PhysicsCheck>();
            if (physics == null)
                physics = root.AddComponent<PhysicsCheck>();
            physics.groundLayer = LayerMask.GetMask("Ground", "Platform");
            physics.checkRaduis = 0.2f;
            physics.bottomOffset = new Vector2(0.1f, -1.12f);

            var character = root.GetComponent<Character>();
            if (character == null)
                character = root.AddComponent<Character>();

            var robot = root.GetComponent<AE74Enemy>();
            if (robot == null)
                robot = root.AddComponent<AE74Enemy>();

            robot.blocksLaser = true;
            robot.normalSpeed = 3.5f;
            robot.chaseSpeed = 10f;
            robot.moveSpeed = 3.5f;

            var gun = EnsureChild(root.transform, "Gun", new Vector3(-0.85f, 0.66f, 0f));
            robot.gunBase = gun;

            var firePoint = EnsureChild(gun, "FirePoint", new Vector3(1.35f, 0f, 0f));
            robot.firePoint = firePoint;

            robot.missileFirePoint1 = EnsureChild(root.transform, "MissileFirePoint1", new Vector3(-0.45f, 0.95f, 0f));
            robot.missileFirePoint2 = EnsureChild(root.transform, "MissileFirePoint2", new Vector3(0.45f, 0.95f, 0f));
            robot.shockwavePoint = EnsureChild(root.transform, "ShockwavePoint", new Vector3(0f, -1.2f, 0f));

            robot.meleeAttacker = EnsureHitbox(
                root.transform,
                "Attacker1",
                new Vector3(1.15f, 0.15f, 0f),
                new Vector2(1.7f, 1.8f),
                robot.meleeDamage);
            robot.stompHitbox = EnsureHitbox(
                root.transform,
                "StompHitbox",
                new Vector3(0f, -1.05f, 0f),
                new Vector2(2.6f, 0.7f),
                robot.stompDamage);

            var bullet = AssetDatabase.LoadAssetAtPath<GameObject>(BulletPath);
            if (bullet != null)
                robot.projectilePrefab = bullet.GetComponent<EnemyProjectile>();

            var missile = AssetDatabase.LoadAssetAtPath<GameObject>(MissilePath);
            if (missile != null)
                robot.cannonMissilePrefab = missile.GetComponent<EnemyMissile>();

            var homing = AssetDatabase.LoadAssetAtPath<GameObject>(HomingPath);
            if (homing != null)
                robot.dashMissilePrefab = homing.GetComponent<EnemyHomingMissile>();

            var shockwave = AssetDatabase.LoadAssetAtPath<GameObject>(ShockwavePath);
            if (shockwave != null)
                robot.shockwavePrefab = shockwave.GetComponent<AE74Shockwave>();

            var charSo = new SerializedObject(character);
            charSo.FindProperty("maxHealth").floatValue = 80f;
            charSo.FindProperty("currentHealth").floatValue = 80f;
            charSo.FindProperty("invulnerableDuration").floatValue = 0.12f;
            var knockback = charSo.FindProperty("immuneToKnockback");
            if (knockback != null)
                knockback.boolValue = true;
            var newGame = charSo.FindProperty("newGameEvent");
            if (newGame != null && newGame.objectReferenceValue == null)
            {
                var ev = AssetDatabase.LoadAssetAtPath<VoidEventSO>("Assets/Data SO/Event/New Game Event SO.asset");
                if (ev != null)
                    newGame.objectReferenceValue = ev;
            }

            charSo.ApplyModifiedPropertiesWithoutUndo();

            BindCharacterEvents(character, robot);

            var anim = root.GetComponent<Animator>();
            var controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(ControllerPath);
            if (anim != null && controller != null)
                anim.runtimeAnimatorController = controller;

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    static void BindCharacterEvents(Character character, AE74Enemy robot)
    {
        if (character.OnTakeDamage.GetPersistentEventCount() == 0)
            UnityEventTools.AddPersistentListener(character.OnTakeDamage, robot.OnTakeDamage);

        if (character.OnDie.GetPersistentEventCount() == 0)
            UnityEventTools.AddVoidPersistentListener(character.OnDie, robot.OnDie);
    }

    static Transform EnsureChild(Transform parent, string name, Vector3 localPos)
    {
        Transform child = parent.Find(name);
        if (child == null)
        {
            var go = new GameObject(name);
            child = go.transform;
            child.SetParent(parent, false);
        }

        child.localPosition = localPos;
        return child;
    }

    static GameObject EnsureHitbox(Transform parent, string name, Vector3 localPos, Vector2 size, int damage)
    {
        Transform child = parent.Find(name);
        GameObject go;
        if (child == null)
        {
            go = new GameObject(name);
            child = go.transform;
            child.SetParent(parent, false);
        }
        else
        {
            go = child.gameObject;
        }

        child.localPosition = localPos;
        go.SetActive(false);

        var box = go.GetComponent<BoxCollider2D>();
        if (box == null)
            box = go.AddComponent<BoxCollider2D>();
        box.isTrigger = true;
        box.size = size;

        var attack = go.GetComponent<Attack>();
        if (attack == null)
            attack = go.AddComponent<Attack>();
        attack.damage = damage;
        attack.attackType = AttackType.Melee;
        attack.requireTag = "Player";
        return go;
    }

    static void SetLayerRecursively(GameObject go, int layer)
    {
        go.layer = layer;
        foreach (Transform child in go.transform)
            SetLayerRecursively(child.gameObject, layer);
    }
}
#endif
