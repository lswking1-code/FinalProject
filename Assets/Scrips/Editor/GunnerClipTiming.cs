using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>Retimes standalone sprite clips while preserving relative holds, events and identity.</summary>
public static class GunnerClipTiming
{
    public static bool ValidDuration(float duration) => duration >= 0.05f && duration <= 60f && !float.IsNaN(duration);

    public static string Validate(AnimationClip clip, float duration)
    {
        if (!ValidDuration(duration)) return "周期须在 0.05～60 秒之间。";
        if (clip == null) return "请选择要调整的动画片段。";
        string path = AssetDatabase.GetAssetPath(clip);
        if (!path.StartsWith("Assets/", StringComparison.Ordinal) || !path.EndsWith(".anim", StringComparison.OrdinalIgnoreCase)
            || AssetDatabase.IsSubAsset(clip)) return "只支持 Assets 中可编辑的独立 .anim 资源。";
        if (!AssetDatabase.IsOpenForEdit(clip, out var reason)) return "动画不可编辑：" + reason;
        if ((File.GetAttributes(path) & FileAttributes.ReadOnly) != 0) return "动画文件为只读。";
        var frames = new GunnerClipFrames(clip);
        if (frames.Error != null) return frames.Error;
        if (AnimationUtility.GetObjectReferenceCurveBindings(clip).Length != 1)
            return "调整周期仅支持一条 Sprite 轨道的片段。";
        if (clip.length <= 0 || clip.frameRate <= 0) return "动画时长或采样率无效。";
        if (Mathf.Abs(AnimationUtility.GetAnimationClipSettings(clip).startTime) > 0.00001f)
            return "仅支持从 0 秒开始的完整片段。";
        return null;
    }

    public static void Apply(AnimationClip[] clips, float duration)
    {
        if (clips == null || clips.Length == 0) throw new ArgumentException("没有要修改的动画片段。");
        var targets = clips.Distinct().ToArray();
        // Validate the whole selection before editing either half.
        foreach (var clip in targets)
        {
            string error = Validate(clip, duration);
            if (error != null) throw new InvalidOperationException(error);
        }
        Undo.IncrementCurrentGroup();
        int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("修改 Gunner 动画周期");
        Undo.RegisterCompleteObjectUndo(targets, "修改 Gunner 动画周期");
        try
        {
            foreach (var clip in targets)
            {
                float ratio = duration / clip.length;
                var settings = AnimationUtility.GetAnimationClipSettings(clip);
                var events = AnimationUtility.GetAnimationEvents(clip);
                var binding = AnimationUtility.GetObjectReferenceCurveBindings(clip)[0];
                var keys = AnimationUtility.GetObjectReferenceCurve(clip, binding);
                for (int i = 0; i < keys.Length; i++) keys[i].time *= ratio;
                foreach (var evt in events) evt.time *= ratio;
                // Sprite curves retain one final sample interval. Scale it too, so the
                // final frame's hold and clip.length remain proportional after reimport.
                clip.frameRate /= ratio;
                AnimationUtility.SetObjectReferenceCurve(clip, binding, keys);
                AnimationUtility.SetAnimationEvents(clip, events);
                settings.stopTime = duration;
                settings.additiveReferencePoseTime *= ratio;
                AnimationUtility.SetAnimationClipSettings(clip, settings);
                EditorUtility.SetDirty(clip);
            }
            foreach (var clip in targets) AssetDatabase.SaveAssetIfDirty(clip);
            Undo.CollapseUndoOperations(group);
        }
        catch
        {
            Undo.RevertAllDownToGroup(group);
            foreach (var clip in targets) AssetDatabase.SaveAssetIfDirty(clip);
            throw;
        }
        finally { Undo.IncrementCurrentGroup(); }
    }
}
