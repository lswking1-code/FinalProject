using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public static class GunnerClipTimingChecks
{
    [MenuItem("Tools/Gunner/验证动画周期修改", true)]
    static bool CanRun() => !EditorApplication.isPlayingOrWillChangePlaymode;

    [MenuItem("Tools/Gunner/验证动画周期修改")]
    public static void Run()
    {
        const string art = "Assets/Arts/PlayerG/jane the gunner/";
        var source = AssetDatabase.LoadAssetAtPath<AnimationClip>(art + "gunner-idle-upper.anim");
        var sourceLower = AssetDatabase.LoadAssetAtPath<AnimationClip>(art + "gunner-idle-lower.anim");
        Require(source != null && sourceLower != null, "Idle test fixtures exist");
        string original = EditorJsonUtility.ToJson(source);
        string originalLower = EditorJsonUtility.ToJson(sourceLower);
        string folder = "Assets/GunnerTimingCheck_" + Guid.NewGuid().ToString("N");
        AssetDatabase.CreateFolder("Assets", folder.Substring("Assets/".Length));
        var upper = Object.Instantiate(source);
        var lower = Object.Instantiate(sourceLower);
        var invalid = new AnimationClip();
        try
        {
            string path = folder + "/upper.anim";
            AssetDatabase.CreateAsset(upper, path);
            AssetDatabase.CreateAsset(lower, folder + "/lower.anim");
            var settings = AnimationUtility.GetAnimationClipSettings(upper);
            settings.loopTime = true;
            AnimationUtility.SetAnimationClipSettings(upper, settings);
            float oldDuration = upper.length;
            float oldLowerDuration = lower.length;
            float oldRate = upper.frameRate;
            var before = new GunnerClipFrames(upper);
            string guid = AssetDatabase.AssetPathToGUID(path);
            AnimationUtility.SetAnimationEvents(upper, new[]
            {
                new AnimationEvent { time = oldDuration * 0.4f, functionName = "TimingTest", intParameter = 7, stringParameter = "preserved" }
            });
            AssetDatabase.SaveAssetIfDirty(upper);
            string beforePreview = EditorJsonUtility.ToJson(upper);
            var preview = new GunnerClipFrames(upper, 1.2f);
            Near(preview.Duration, 1.2f, "Preview duration");
            Near(preview.Keys[1].time, before.Keys[1].time / oldDuration * 1.2f, "Preview scales key times");
            Require(EditorJsonUtility.ToJson(upper) == beforePreview, "Preview never modifies clip");
            Require(preview.Sample(preview.Keys[1].time) == before.Keys[1].value, "Preview selects the correct sprite");

            GunnerClipTiming.Apply(new[] { upper, lower }, 1.2f);
            Near(upper.length, 1.2f, "Upper duration is exact");
            Near(lower.length, 1.2f, "Lower duration matches upper");
            Near(upper.frameRate, oldRate * oldDuration / 1.2f, "Final sample interval scales with duration");
            var after = new GunnerClipFrames(upper);
            Require(after.Keys.Length == before.Keys.Length, "No sprite keys added or removed");
            for (int i = 0; i < before.Keys.Length; i++)
            {
                Require(after.Keys[i].value == before.Keys[i].value, "Sprite references and ordering remain unchanged");
                Near(after.Keys[i].time, before.Keys[i].time / oldDuration * 1.2f, "Relative frame timing preserved");
            }
            Require(upper.isLooping, "Loop setting survives retiming");
            var evt = AnimationUtility.GetAnimationEvents(upper).Single();
            Near(evt.time, 0.48f, "Animation event time scales");
            Require(evt.functionName == "TimingTest" && evt.intParameter == 7 && evt.stringParameter == "preserved", "Event payload preserved");

            Undo.PerformUndo();
            Near(upper.length, oldDuration, "One undo restores upper");
            Near(lower.length, oldLowerDuration, "Same undo restores lower");
            Undo.PerformRedo();
            Near(upper.length, 1.2f, "Redo restores target duration");
            AssetDatabase.SaveAssetIfDirty(upper);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            upper = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            Near(upper.length, 1.2f, "Save/reimport preserves duration");
            Require(AssetDatabase.AssetPathToGUID(path) == guid, "Clip GUID preserved");

            GunnerClipTiming.Apply(new[] { upper }, 0.8f);
            Near(upper.length, 0.8f, "Can retime an already retimed clip");
            Near(lower.length, 1.2f, "Single-half scope leaves other half unchanged");
            Near(new GunnerClipFrames(upper).Keys[1].time, before.Keys[1].time / oldDuration * 0.8f, "Repeated retiming does not drift");
            Require(GunnerClipTiming.Validate(upper, 0) != null && GunnerClipTiming.Validate(upper, float.NaN) != null
                && GunnerClipTiming.Validate(upper, float.PositiveInfinity) != null, "Invalid durations rejected");
            bool rejected = false;
            try { GunnerClipTiming.Apply(new[] { upper, invalid }, 1.6f); }
            catch (InvalidOperationException) { rejected = true; }
            Require(rejected, "Invalid second clip rejected");
            Near(upper.length, 0.8f, "Validation failure does not partially edit first clip");
            Require(EditorJsonUtility.ToJson(source) == original && EditorJsonUtility.ToJson(sourceLower) == originalLower, "Real Idle clips remain untouched");
            Debug.Log("Gunner timing checks PASSED (preview, two-half retime, keys, events, undo/redo, persistence, validation, isolation).");
        }
        finally
        {
            Undo.ClearUndo(upper); Undo.ClearUndo(lower);
            Object.DestroyImmediate(invalid);
            AssetDatabase.DeleteAsset(folder);
        }
    }

    public static void RunBatch()
    {
        try { Run(); GunnerCalibrationChecks.RunAllBatch(); }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
    }

    static void Require(bool condition, string label)
    {
        if (!condition) throw new InvalidOperationException("Gunner timing check: " + label);
    }
    static void Near(float actual, float expected, string label) => Require(Mathf.Abs(actual - expected) < 0.0001f, $"{label}: {actual} vs {expected}");
}
