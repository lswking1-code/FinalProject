#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// 生成 / 修复镭射炮塔 Animator Controller（Idle / Activate / Inactivate / Inactivate_Idle）。
/// 帧来源：Assets/Arts/Tilemap/Traps/Individual_PNGs/laser_turret/tile000~016。
/// </summary>
public static class LaserTurretAnimControllerSetup
{
    const string AnimRoot = "Assets/Animations/Items";
    const string SpriteRoot = "Assets/Arts/Tilemap/Traps/Individual_PNGs/laser_turret";
    const string ControllerPath = AnimRoot + "/LaserTurret.controller";
    const string IdleClipPath = AnimRoot + "/LaserTurret_Idle.anim";
    const string ActivateClipPath = AnimRoot + "/LaserTurret_Activate.anim";
    const string InactivateClipPath = AnimRoot + "/LaserTurret_Inactivate.anim";
    const string InactivateIdleClipPath = AnimRoot + "/LaserTurret_InactivateIdle.anim";
    const float SampleRate = 10f;

    [MenuItem("Lost Division/Create Laser Turret Animator Controller")]
    public static void CreateLaserTurretAnimatorController()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            return;

        CreateOrRebuild();
    }

    static void CreateOrRebuild()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Animations"))
            AssetDatabase.CreateFolder("Assets", "Animations");
        if (!AssetDatabase.IsValidFolder(AnimRoot))
            AssetDatabase.CreateFolder("Assets/Animations", "Items");

        var frames = LoadTurretFrames();
        if (frames.Count < 17)
        {
            Debug.LogWarning($"[LaserTurretAnimControllerSetup] 期望 17 帧，实际 {frames.Count}，跳过重建。");
            return;
        }

        WriteSpriteClip(IdleClipPath, "LaserTurret_Idle", Slice(frames, 8, 16), loop: true);
        WriteSpriteClip(ActivateClipPath, "LaserTurret_Activate", Slice(frames, 0, 8), loop: false);
        WriteSpriteClip(InactivateClipPath, "LaserTurret_Inactivate", ReverseSlice(frames, 0, 8), loop: false);
        WriteSpriteClip(InactivateIdleClipPath, "LaserTurret_InactivateIdle", Slice(frames, 0, 0), loop: true);

        var idle = AssetDatabase.LoadAssetAtPath<AnimationClip>(IdleClipPath);
        var activate = AssetDatabase.LoadAssetAtPath<AnimationClip>(ActivateClipPath);
        var inactivate = AssetDatabase.LoadAssetAtPath<AnimationClip>(InactivateClipPath);
        var inactivateIdle = AssetDatabase.LoadAssetAtPath<AnimationClip>(InactivateIdleClipPath);

        // 用 API 重建，避免手写 YAML 导致 Animator 窗口 NRE
        if (AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath) != null)
            AssetDatabase.DeleteAsset(ControllerPath);

        var controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        var sm = controller.layers[0].stateMachine;

        // 清掉 CreateAnimatorControllerAtPath 自带的默认状态
        var existing = sm.states;
        for (int i = existing.Length - 1; i >= 0; i--)
            sm.RemoveState(existing[i].state);

        var idleState = sm.AddState("Idle", new Vector3(350f, 100f, 0f));
        var activateState = sm.AddState("Activate", new Vector3(120f, 100f, 0f));
        var inactivateState = sm.AddState("Inactivate", new Vector3(580f, 100f, 0f));
        var inactivateIdleState = sm.AddState("Inactivate_Idle", new Vector3(810f, 100f, 0f));

        idleState.motion = idle;
        activateState.motion = activate;
        inactivateState.motion = inactivate;
        inactivateIdleState.motion = inactivateIdle;
        sm.defaultState = idleState;

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        Debug.Log($"已创建/更新 {ControllerPath}");
    }

    static List<Sprite> LoadTurretFrames()
    {
        var result = new List<Sprite>(17);
        for (int i = 0; i <= 16; i++)
        {
            string path = $"{SpriteRoot}/tile{i:000}.png";
            var assets = AssetDatabase.LoadAllAssetsAtPath(path);
            Sprite chosen = null;
            string prefer = $"tile{i:000}_0";
            foreach (var asset in assets)
            {
                if (asset is not Sprite sprite)
                    continue;
                if (sprite.name == prefer)
                {
                    chosen = sprite;
                    break;
                }

                chosen ??= sprite;
            }

            if (chosen != null)
                result.Add(chosen);
        }

        return result;
    }

    static List<Sprite> Slice(List<Sprite> frames, int from, int to)
    {
        var list = new List<Sprite>();
        for (int i = from; i <= to && i < frames.Count; i++)
            list.Add(frames[i]);
        return list;
    }

    static List<Sprite> ReverseSlice(List<Sprite> frames, int from, int to)
    {
        var list = new List<Sprite>();
        for (int i = to; i >= from; i--)
        {
            if (i >= 0 && i < frames.Count)
                list.Add(frames[i]);
        }

        return list;
    }

    static void WriteSpriteClip(string clipPath, string clipName, List<Sprite> sprites, bool loop)
    {
        if (sprites == null || sprites.Count == 0)
            return;

        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
        if (clip == null)
        {
            clip = new AnimationClip { name = clipName };
            AssetDatabase.CreateAsset(clip, clipPath);
        }

        clip.name = clipName;
        clip.frameRate = SampleRate;
        clip.ClearCurves();

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
            path = string.Empty,
            type = typeof(SpriteRenderer),
            propertyName = "m_Sprite"
        };
        AnimationUtility.SetObjectReferenceCurve(clip, spriteBinding, keys);

        var settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = loop;
        settings.stopTime = Mathf.Max(dt, sprites.Count * dt);
        AnimationUtility.SetAnimationClipSettings(clip, settings);
        EditorUtility.SetDirty(clip);
    }
}
#endif
