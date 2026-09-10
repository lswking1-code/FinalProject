using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>Read-only sprite track sampling: no AnimationMode and no scene objects.</summary>
public sealed class GunnerClipFrames
{
    public readonly AnimationClip Clip;
    public readonly ObjectReferenceKeyframe[] Keys;
    public readonly string Error;
    public float Duration { get; }

    public GunnerClipFrames(AnimationClip clip, float previewDuration = 0)
    {
        Clip = clip;
        float sourceDuration = clip != null ? Mathf.Max(clip.length, 0.0001f) : 0.0001f;
        Duration = previewDuration > 0 && !float.IsInfinity(previewDuration) ? previewDuration : sourceDuration;
        Keys = Array.Empty<ObjectReferenceKeyframe>();
        if (clip == null) { Error = "请选择动画片段。"; return; }
        var bindings = AnimationUtility.GetObjectReferenceCurveBindings(clip)
            .Where(b => b.type == typeof(SpriteRenderer) && b.propertyName == "m_Sprite").ToArray();
        if (bindings.Length != 1)
        {
            Error = "片段必须只有一条 SpriteRenderer Sprite 轨道。";
            return;
        }
        // Transform animation would make this sprite-only preview disagree with runtime.
        if (AnimationUtility.GetCurveBindings(clip).Length != 0)
        {
            Error = "此片段含数值动画轨道；首版仅支持直接切换 Sprite 的片段。";
            return;
        }
        Keys = AnimationUtility.GetObjectReferenceCurve(clip, bindings[0]).OrderBy(k => k.time).ToArray();
        float ratio = Duration / sourceDuration;
        for (int i = 0; i < Keys.Length; i++) Keys[i].time *= ratio;
        if (Keys.Length == 0) Error = "片段没有 Sprite 帧。";
    }

    public Sprite Sample(float time, bool wrap = false)
    {
        if (wrap) time = Mathf.Repeat(time, Duration);
        Sprite result = null;
        foreach (var key in Keys)
        {
            if (key.time > time + 0.000001f) break;
            result = key.value as Sprite;
        }
        return result;
    }

    public float Step(float time, int direction)
    {
        if (Keys.Length == 0) return 0;
        if (direction > 0)
        {
            foreach (var key in Keys) if (key.time > time + 0.00001f) return key.time;
            return Clip != null && Clip.isLooping ? Keys[0].time : Mathf.Max(time, Keys[Keys.Length - 1].time);
        }
        for (int i = Keys.Length - 1; i >= 0; i--)
            if (Keys[i].time < time - 0.00001f) return Keys[i].time;
        return Clip != null && Clip.isLooping ? Keys[Keys.Length - 1].time : Keys[0].time;
    }
}
