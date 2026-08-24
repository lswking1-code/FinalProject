#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// 填充枪兵/火箭兵射击预备 clip，并在 enemy_rifle / enemy_roket Controller 接入 ShotPrep。
/// </summary>
public static class EnemyRangedShotPrepSetup
{
    const string RifleControllerPath = "Assets/Animation/enemy_rifle.controller";
    const string RocketControllerPath = "Assets/Animation/enemy_roket.controller";

    const string GunPrepClipPath = "Assets/Animations/Enemy/enemy_gun_preparation.anim";
    const string RocketPrepClipPath = "Assets/Animations/Enemy/Rocket/RocketEnemy_preparation.anim";

    const string GunPrepSpriteSheet = "Assets/Arts/Enemies/enemy_gun_attack_pro.png";
    const string RocketAttackSpriteSheet = "Assets/Arts/Enemies/enemy_rocket_attack.png";

    const float SampleRate = 12f;
    const string SpriteChildPath = "Sprite";

    [InitializeOnLoadMethod]
    static void AutoEnsure()
    {
        EditorApplication.delayCall += () =>
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return;

            EnsureAll(forceRewriteClips: false, silent: true);
        };
    }

    [MenuItem("Lost Division/Ensure Ranged Shot Prep Animations")]
    public static void MenuEnsure()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            return;

        EnsureAll(forceRewriteClips: true, silent: false);
    }

    static void EnsureAll(bool forceRewriteClips, bool silent)
    {
        EnsureGunPrepClip(forceRewriteClips);
        EnsureRocketPrepClip(forceRewriteClips);
        EnsureController(RifleControllerPath, GunPrepClipPath);
        EnsureController(RocketControllerPath, RocketPrepClipPath);

        AssetDatabase.SaveAssets();
        if (!silent)
            Debug.Log("[EnemyRangedShotPrepSetup] 已更新预备 clip 与 rifle/roket Animator。");
    }

    static void EnsureGunPrepClip(bool forceRewrite)
    {
        if (!forceRewrite && ClipHasSpriteCurve(GunPrepClipPath))
            return;

        var sprites = LoadSortedSprites(GunPrepSpriteSheet, "enemy_gun_attack_pro_");
        WriteSpriteClip(GunPrepClipPath, "enemy_gun_preparation", sprites);
    }

    static void EnsureRocketPrepClip(bool forceRewrite)
    {
        if (!forceRewrite && ClipHasSpriteCurve(RocketPrepClipPath))
            return;

        // 火箭预备：攻击表前半段（后半留给 shoot）
        var all = LoadSortedSprites(RocketAttackSpriteSheet, "enemy_rocket_attack_");
        int count = Mathf.Max(1, all.Count / 2);
        var prep = all.GetRange(0, count);
        WriteSpriteClip(RocketPrepClipPath, "RocketEnemy_preparation", prep);
    }

    static bool ClipHasSpriteCurve(string clipPath)
    {
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
        if (clip == null)
            return false;

        var bindings = AnimationUtility.GetObjectReferenceCurveBindings(clip);
        foreach (var binding in bindings)
        {
            var keys = AnimationUtility.GetObjectReferenceCurve(clip, binding);
            if (keys != null && keys.Length > 0)
                return true;
        }

        return false;
    }

    static List<Sprite> LoadSortedSprites(string texturePath, string namePrefix)
    {
        var result = new List<Sprite>();
        Object[] assets = AssetDatabase.LoadAllAssetsAtPath(texturePath);
        if (assets == null)
            return result;

        foreach (var asset in assets)
        {
            if (asset is Sprite sprite && sprite.name.StartsWith(namePrefix))
                result.Add(sprite);
        }

        result.Sort((a, b) => ExtractIndex(a.name, namePrefix).CompareTo(ExtractIndex(b.name, namePrefix)));
        return result;
    }

    static int ExtractIndex(string name, string prefix)
    {
        if (string.IsNullOrEmpty(name) || !name.StartsWith(prefix))
            return int.MaxValue;
        string suffix = name.Substring(prefix.Length);
        return int.TryParse(suffix, out int index) ? index : int.MaxValue;
    }

    static void WriteSpriteClip(string clipPath, string clipName, List<Sprite> sprites)
    {
        if (sprites == null || sprites.Count == 0)
        {
            Debug.LogWarning($"[EnemyRangedShotPrepSetup] 无精灵可写入 {clipPath}");
            return;
        }

        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
        if (clip == null)
        {
            clip = new AnimationClip { name = clipName };
            AssetDatabase.CreateAsset(clip, clipPath);
        }

        clip.name = clipName;
        clip.frameRate = SampleRate;
        clip.ClearCurves();

        // 清除旧 Sprite 绑定
        foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(clip))
            AnimationUtility.SetObjectReferenceCurve(clip, binding, null);

        var keys = new ObjectReferenceKeyframe[sprites.Count];
        float dt = 1f / SampleRate;
        for (int i = 0; i < sprites.Count; i++)
        {
            keys[i] = new ObjectReferenceKeyframe
            {
                time = i * dt,
                value = sprites[i]
            };
        }

        var spriteBinding = new EditorCurveBinding
        {
            path = SpriteChildPath,
            type = typeof(SpriteRenderer),
            propertyName = "m_Sprite"
        };
        AnimationUtility.SetObjectReferenceCurve(clip, spriteBinding, keys);

        var settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = false;
        settings.stopTime = sprites.Count * dt;
        AnimationUtility.SetAnimationClipSettings(clip, settings);

        EditorUtility.SetDirty(clip);
    }

    static void EnsureController(string controllerPath, string prepClipPath)
    {
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
        if (controller == null)
        {
            Debug.LogWarning($"[EnemyRangedShotPrepSetup] 找不到 Controller: {controllerPath}");
            return;
        }

        var prepClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(prepClipPath);
        EnsureParameter(controller, "shotPrep", AnimatorControllerParameterType.Bool);

        var sm = controller.layers[0].stateMachine;
        var idle = FindState(sm, "Idle");
        var shotPrep = FindOrAddState(sm, "ShotPrep", new Vector3(580f, 160f, 0f));
        if (prepClip != null)
            shotPrep.motion = prepClip;

        EnsureAnyStateBool(sm, shotPrep, "shotPrep", true, canTransitionToSelf: false);
        if (idle != null)
            EnsureBoolTransition(shotPrep, idle, "shotPrep", false);

        EditorUtility.SetDirty(controller);
    }

    static AnimatorState FindState(AnimatorStateMachine sm, string name)
    {
        foreach (var child in sm.states)
        {
            if (child.state != null && child.state.name == name)
                return child.state;
        }

        return null;
    }

    static AnimatorState FindOrAddState(AnimatorStateMachine sm, string name, Vector3 position)
    {
        var existing = FindState(sm, name);
        if (existing != null)
            return existing;

        return sm.AddState(name, position);
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
}
#endif
