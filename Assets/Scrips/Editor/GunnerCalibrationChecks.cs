using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Runs in the Unity editor without starting gameplay or touching user scenes.</summary>
public static class GunnerCalibrationChecks
{
    static int assertions;

    [MenuItem("Tools/Gunner/验证拼接校准逻辑", true)]
    [MenuItem("Tools/Gunner/验证拼接窗口交互", true)]
    static bool CanRunChecks() => !EditorApplication.isPlayingOrWillChangePlaymode;

    [MenuItem("Tools/Gunner/验证拼接校准逻辑")]
    public static void Run()
    {
        assertions = 0;
        var texture = new Texture2D(16, 16);
        var a = Sprite.Create(texture, new Rect(0, 0, 8, 8), new Vector2(0.1f, 0.2f), 16);
        var b = Sprite.Create(texture, new Rect(8, 0, 8, 8), new Vector2(0.7f, 0.3f), 32);
        var c = Sprite.Create(texture, new Rect(0, 8, 8, 8), Vector2.one, 16);
        var d = Sprite.Create(texture, new Rect(8, 8, 8, 8), Vector2.zero, 16);
        var profile = ScriptableObject.CreateInstance<GunnerBodyCalibration>();
        var draft = ScriptableObject.CreateInstance<GunnerBodyCalibration>();
        var previewScene = EditorSceneManager.NewPreviewScene();
        var root = EditorUtility.CreateGameObjectWithHideFlags("Calibration checks (temporary)", HideFlags.HideAndDontSave);
        SceneManager.MoveGameObjectToScene(root, previewScene);
        var clip = new AnimationClip { frameRate = 12 };
        try
        {
            profile.SetReferences(a, b);
            Check(!profile.TryGetOffset(true, a, out _), "Missing upper must remain uncalibrated");
            Check(profile.TryGetOffset(false, b, out var zero) && zero == Vector2.zero, "Reference waist is zero");
            Near(profile.GetCombinedOffset(a, b), Vector2.zero, "Missing entries use zero");
            profile.SetOffset(true, a, new Vector2(0.25f, -0.125f));
            profile.SetOffset(false, c, new Vector2(-0.0625f, 0.1875f));
            Near(profile.GetCombinedOffset(a, c), new Vector2(0.1875f, 0.0625f), "Independent tracks compose");
            Near(profile.GetCombinedOffset(null, c), Vector2.zero, "Null frame cancels composition");
            profile.SetOffset(false, b, Vector2.one);
            Near(profile.GetOffset(false, b), Vector2.zero, "Reference waist cannot drift");
            draft.CopyFrom(profile);
            draft.SetOffset(true, a, Vector2.one);
            Near(profile.GetOffset(true, a), new Vector2(0.25f, -0.125f), "Draft cannot mutate saved source");
            draft.SetOffset(true, d, new Vector2(0.4f, 0.5f));
            draft.RemoveOffset(true, a);
            Near(draft.GetOffset(true, a), Vector2.zero, "Reset invalidates lookup cache");
            Near(draft.GetOffset(true, d), new Vector2(0.4f, 0.5f), "Reset affects only requested sprite");

            Undo.RegisterCompleteObjectUndo(draft, "Gunner check undo");
            draft.SetOffset(true, d, Vector2.one);
            Undo.FlushUndoRecordObjects();
            Undo.PerformUndo();
            draft.Invalidate();
            Near(draft.GetOffset(true, d), new Vector2(0.4f, 0.5f), "Draft undo restores entries");
            Undo.PerformRedo();
            draft.Invalidate();
            Near(draft.GetOffset(true, d), Vector2.one, "Draft redo restores entries");
            Undo.ClearUndo(draft);

            var parent = new GameObject("Upper parent").transform;
            parent.SetParent(root.transform, false);
            parent.localScale = new Vector3(2, 0.5f, 1);
            var upper = new GameObject("Upper").AddComponent<SpriteRenderer>();
            upper.transform.SetParent(parent, false);
            upper.transform.localPosition = new Vector3(0.21f, 0.722f, 0.063f);
            upper.sprite = a;
            var lower = new GameObject("Lower").AddComponent<SpriteRenderer>();
            lower.transform.SetParent(root.transform, false);
            lower.transform.localPosition = new Vector3(0.21f, -0.15f, 0.04f);
            lower.sprite = c;
            var full = new GameObject("Full body");
            full.transform.SetParent(root.transform, false);
            full.SetActive(false);
            var component = root.AddComponent<GunnerBodyAlignment>();
            component.upperRenderer = upper;
            component.lowerRenderer = lower;
            component.fullBody = full;
            component.calibration = profile;
            var rest = upper.transform.localPosition;
            var lowerRest = lower.transform.localPosition;
            Vector3 expected = rest + new Vector3(0.1875f / 2, 0.0625f / 0.5f, 0);
            component.ApplyAlignment();
            Near(upper.transform.localPosition, expected, "Converts root offset to upper parent units");
            for (int i = 0; i < 100; i++) component.ApplyAlignment();
            Near(upper.transform.localPosition, expected, "Repeated frames cannot accumulate offset");
            root.transform.localScale = new Vector3(-3, 2, 1);
            root.transform.rotation = Quaternion.Euler(0, 0, 20);
            component.ApplyAlignment();
            Near(upper.transform.localPosition, expected, "Facing/root scale/rotation do not invert local correction twice");
            Near(lower.transform.localPosition, lowerRest, "Legs never move");
            var direction = GunnerBodyCalibration.MuzzleDirection.Forward;
            var muzzlePixel = new Vector2(7, 4);
            profile.SetMuzzle(a, 0, direction, muzzlePixel);
            var muzzle = component.ResolveMuzzle(0, direction, lower.transform);
            Near(muzzle.position, upper.transform.TransformPoint((muzzlePixel - a.pivot) / a.pixelsPerUnit), "Muzzle follows corrected body, pivot and root transform");
            upper.flipX = true; upper.flipY = true;
            Near(component.ResolveMuzzle(0, direction, lower.transform).position,
                upper.transform.TransformPoint(-(muzzlePixel - a.pivot) / a.pixelsPerUnit), "Muzzle respects renderer flips");
            upper.flipX = upper.flipY = false;
            Check(component.ResolveMuzzle(1, direction, lower.transform) == lower.transform, "Other weapon falls back");
            Check(component.ResolveMuzzle(0, GunnerBodyCalibration.MuzzleDirection.Up, lower.transform) == lower.transform, "Other direction falls back");
            draft.CopyFrom(profile);
            draft.SetMuzzle(a, 0, direction, Vector2.zero);
            profile.TryGetMuzzle(a, 0, direction, out var preservedPixel);
            Near(preservedPixel, muzzlePixel, "Muzzle draft does not mutate source");
            draft.ClearOffsets();
            Check(draft.TryGetMuzzle(a, 0, direction, out _), "Changing body reference preserves image-local muzzle");
            full.SetActive(true);
            Check(component.ResolveMuzzle(0, direction, lower.transform) == lower.transform, "Full body retains original fire point");
            component.ApplyAlignment();
            Near(upper.transform.localPosition, rest, "Full-body mode restores rest");
            full.SetActive(false);
            component.ApplyAlignment();
            Near(upper.transform.localPosition, expected, "Split resumes immediately");
            upper.sprite = null;
            component.ApplyAlignment();
            Near(upper.transform.localPosition, rest, "Null sprite restores rest");
            upper.sprite = a;
            component.ApplyAlignment();
            component.enabled = false;
            // Edit-mode checks explicitly dispatch the runtime callback (not ExecuteAlways).
            if (!Application.isPlaying) component.SendMessage("OnDisable");
            Near(upper.transform.localPosition, rest, "Disable callback restores original display position");

            var binding = EditorCurveBinding.PPtrCurve("", typeof(SpriteRenderer), "m_Sprite");
            AnimationUtility.SetObjectReferenceCurve(clip, binding, new[]
            {
                new ObjectReferenceKeyframe { time = 0, value = a },
                new ObjectReferenceKeyframe { time = 0.17f, value = b },
                new ObjectReferenceKeyframe { time = 0.43f, value = null },
                new ObjectReferenceKeyframe { time = 0.79f, value = c }
            });
            var frames = new GunnerClipFrames(clip);
            Check(frames.Error == null, "Accepts a single sprite track");
            Check(frames.Sample(0.16f) == a && frames.Sample(0.18f) == b, "Samples actual key times, not nominal FPS");
            Check(frames.Sample(0.5f) == null, "Preserves explicit empty frames");
            Check(Mathf.Abs(frames.Step(0.18f, 1) - 0.43f) < 0.00001f, "Step uses next sprite event");
            Check(frames.Step(0, -1) == 0, "Non-looping first frame does not wrap backwards");
            Check(Mathf.Abs(frames.Step(0.79f, 1) - 0.79f) < 0.00001f, "Non-looping last frame does not wrap forwards");
            Check(frames.Sample(frames.Duration) == c, "Non-looping endpoint holds the final sprite");
            var loopSettings = AnimationUtility.GetAnimationClipSettings(clip);
            loopSettings.loopTime = true;
            AnimationUtility.SetAnimationClipSettings(clip, loopSettings);
            Check(Mathf.Abs(frames.Step(0, -1) - 0.79f) < 0.00001f, "Looping previous still wraps");
            Check(frames.Step(0.79f, 1) == 0, "Looping next still wraps");
            Check(frames.Sample(frames.Duration + 0.1f, true) == a, "Loop wraps clip time");
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Transform), "m_LocalPosition.x"), AnimationCurve.Linear(0, 0, 1, 1));
            Check(new GunnerClipFrames(clip).Error != null, "Rejects incompatible numeric tracks explicitly");
            Check(new GunnerClipFrames(null).Sample(0) == null, "Null clip is safe");

            ValidateSavedAsset();
            Debug.Log($"Gunner calibration checks PASSED ({assertions} assertions).");
        }
        finally
        {
            Undo.ClearUndo(draft);
            Object.DestroyImmediate(root);
            EditorSceneManager.ClosePreviewScene(previewScene);
            Object.DestroyImmediate(profile);
            Object.DestroyImmediate(draft);
            Object.DestroyImmediate(clip);
            Object.DestroyImmediate(a); Object.DestroyImmediate(b); Object.DestroyImmediate(c); Object.DestroyImmediate(d);
            Object.DestroyImmediate(texture);
        }
    }

    [MenuItem("Tools/Gunner/验证拼接窗口交互")]
    public static void RunWindowChecks()
    {
        // A separate window and uniquely named test asset protect existing user drafts and resources.
        var original = AssetDatabase.LoadAssetAtPath<GunnerBodyCalibration>(GunnerBodyCalibrationWindow.ProfilePath);
        if (original == null) throw new InvalidOperationException("Missing Gunner calibration asset.");
        string originalJson = JsonUtility.ToJson(original);
        string path = "Assets/GunnerWindowCheck_" + Guid.NewGuid().ToString("N") + ".asset";
        var copy = ScriptableObject.CreateInstance<GunnerBodyCalibration>();
        copy.CopyFrom(original);
        AssetDatabase.CreateAsset(copy, path);
        GunnerBodyCalibrationWindow window = null;
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var type = typeof(GunnerBodyCalibrationWindow);
        try
        {
            window = ScriptableObject.CreateInstance<GunnerBodyCalibrationWindow>();
            type.GetField("saved", flags).SetValue(window, copy);
            type.GetMethod("LoadDraft", flags).Invoke(window, null);
            window.Show();
            window.position = new Rect(50, 50, 1050, 720);
            window.SendEvent(new Event { type = EventType.Layout });
            window.SendEvent(new Event { type = EventType.Repaint });
            var draft = (GunnerBodyCalibration)type.GetField("draft", flags).GetValue(window);
            // Match the reference frame even if a future profile changes the initial reference.
            var currentUpper = (Sprite)type.GetProperty("ActiveSprite", flags).GetValue(window);
            Vector2 before = draft.GetOffset(true, currentUpper);
            float pixel = 1f / currentUpper.pixelsPerUnit;
            Check(window.PreviewUpperRect.width > 0, "Window paints a sprite preview");
            Drag(window, 6);
            Near(draft.GetOffset(true, currentUpper), before + Vector2.right * pixel, "Mouse drag changes exactly one pixel");
            Near(copy.GetOffset(true, currentUpper), before, "Mouse drag cannot modify saved resource");
            Undo.FlushUndoRecordObjects();
            Undo.PerformUndo(); draft.Invalidate();
            Near(draft.GetOffset(true, currentUpper), before, "One undo restores drag");
            Undo.PerformRedo(); draft.Invalidate();
            Near(draft.GetOffset(true, currentUpper), before + Vector2.right * pixel, "Redo restores drag");
            type.GetField("flipped", flags).SetValue(window, true);
            window.SendEvent(new Event { type = EventType.Layout });
            window.SendEvent(new Event { type = EventType.Repaint });
            Drag(window, 6);
            Near(draft.GetOffset(true, currentUpper), before, "Flipped drag maps screen direction correctly");
            window.SaveChanges();
            Check(copy.TryGetOffset(true, currentUpper, out var offset) && offset == before, "Save records calibrated status and value");
            type.GetField("mode", flags).SetValue(window, 2);
            type.GetField("flipped", flags).SetValue(window, false);
            type.GetField("playing", flags).SetValue(window, false);
            type.GetField("combinedEditTarget", flags).SetValue(window, 0);
            var lowerTrack = (GunnerClipFrames)type.GetField("lowerFrames", flags).GetValue(window);
            foreach (var key in lowerTrack.Keys)
                if (key.value != null && key.value != draft.ReferenceLower)
                { type.GetField("time", flags).SetValue(window, key.time); break; }
            var comboUpper = (Sprite)type.GetProperty("CurrentUpper", flags).GetValue(window);
            var comboLower = (Sprite)type.GetProperty("CurrentLower", flags).GetValue(window);
            Check(comboLower != draft.ReferenceLower, "Combo test uses non-reference waist frame");
            Vector2 upperBefore = draft.GetOffset(true, comboUpper);
            Vector2 lowerBefore = draft.GetOffset(false, comboLower);
            window.SendEvent(new Event { type = EventType.Layout });
            window.SendEvent(new Event { type = EventType.Repaint });
            Drag(window, 6);
            Near(draft.GetOffset(true, comboUpper), upperBefore + Vector2.right / comboUpper.pixelsPerUnit, "Combo drag edits selected upper");
            Near(draft.GetOffset(false, comboLower), lowerBefore, "Combo upper edit preserves lower contribution");
            type.GetField("combinedEditTarget", flags).SetValue(window, 1);
            window.SendEvent(new Event { type = EventType.Layout });
            window.SendEvent(new Event { type = EventType.Repaint });
            Drag(window, 6);
            Near(draft.GetOffset(false, comboLower), lowerBefore + Vector2.right / comboLower.pixelsPerUnit, "Combo drag edits selected waist");
            Near(draft.GetOffset(true, comboUpper), upperBefore + Vector2.right / comboUpper.pixelsPerUnit, "Waist edit preserves upper contribution");
            Undo.FlushUndoRecordObjects();
            Undo.PerformUndo(); draft.Invalidate();
            Near(draft.GetOffset(false, comboLower), lowerBefore, "Combo waist edit can be undone");
            Undo.PerformRedo(); draft.Invalidate();
            Vector2 waistAfter = draft.GetOffset(false, comboLower);
            type.GetField("playing", flags).SetValue(window, true);
            Drag(window, 6);
            Near(draft.GetOffset(false, comboLower), waistAfter, "Playing combo cannot be edited");
            type.GetField("playing", flags).SetValue(window, false);
            type.GetField("time", flags).SetValue(window, 0f);
            Check(!(bool)type.GetProperty("CanEdit", flags).GetValue(window), "Reference waist remains locked in combo");
            window.SaveChanges();
            Near(copy.GetOffset(false, comboLower), waistAfter, "Combo edit saves to selected Sprite");
            Check(JsonUtility.ToJson(original) == originalJson, "Window test preserves real calibration resource");
            var jumpUpper = AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/Arts/PlayerG/jane the gunner/gunner-jump-upper.anim");
            var jumpLower = AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/Arts/PlayerG/jane the gunner/gunner-jump-lower.anim");
            Check(jumpUpper != null && jumpLower != null && !jumpUpper.isLooping && !jumpLower.isLooping, "Both Jump clips are non-looping");
            type.GetField("upperClip", flags).SetValue(window, jumpUpper);
            type.GetField("lowerClip", flags).SetValue(window, jumpLower);
            type.GetMethod("RebuildFrames", flags).Invoke(window, null);
            var jumpUpperFrames = new GunnerClipFrames(jumpUpper);
            var jumpLowerFrames = new GunnerClipFrames(jumpLower);
            type.GetField("time", flags).SetValue(window, Mathf.Max(jumpUpper.length, jumpLower.length));
            Check((Sprite)type.GetProperty("CurrentUpper", flags).GetValue(window) == jumpUpperFrames.Keys[jumpUpperFrames.Keys.Length - 1].value,
                "Jump endpoint keeps upper final sprite");
            Check((Sprite)type.GetProperty("CurrentLower", flags).GetValue(window) == jumpLowerFrames.Keys[jumpLowerFrames.Keys.Length - 1].value,
                "Jump endpoint keeps lower final sprite");
            type.GetMethod("Step", flags).Invoke(window, new object[] { 1 });
            Check((Sprite)type.GetProperty("CurrentUpper", flags).GetValue(window) == jumpUpperFrames.Keys[jumpUpperFrames.Keys.Length - 1].value,
                "Next at Jump endpoint cannot wrap upper");
            type.GetField("time", flags).SetValue(window, 0f);
            type.GetMethod("Step", flags).Invoke(window, new object[] { -1 });
            Check((float)type.GetField("time", flags).GetValue(window) == 0, "Previous at Jump start stays at start");
            type.GetField("editMuzzle", flags).SetValue(window, true);
            type.GetField("flipped", flags).SetValue(window, false);
            var muzzleSprite = (Sprite)type.GetProperty("CurrentUpper", flags).GetValue(window);
            var muzzleDirection = GunnerBodyCalibration.MuzzleDirection.Forward;
            draft.SetMuzzle(muzzleSprite, 0, muzzleDirection, new Vector2(8, 8));
            window.SendEvent(new Event { type = EventType.Layout });
            window.SendEvent(new Event { type = EventType.Repaint });
            var start = window.PreviewMuzzlePoint;
            window.SendEvent(new Event { type = EventType.MouseDown, button = 0, mousePosition = start });
            window.SendEvent(new Event { type = EventType.MouseDrag, button = 0, mousePosition = start + Vector2.right * 6 });
            window.SendEvent(new Event { type = EventType.MouseUp, button = 0, mousePosition = start + Vector2.right * 6 });
            Check(draft.TryGetMuzzle(muzzleSprite, 0, muzzleDirection, out var movedMuzzle), "Muzzle drag creates mark");
            Near(movedMuzzle, new Vector2(9, 8), "Muzzle drag uses sprite pixels");
            Undo.FlushUndoRecordObjects(); Undo.PerformUndo();
            draft.TryGetMuzzle(muzzleSprite, 0, muzzleDirection, out movedMuzzle);
            Near(movedMuzzle, new Vector2(8, 8), "Muzzle drag undo restores mark");
            Undo.PerformRedo(); window.SaveChanges();
            copy.TryGetMuzzle(muzzleSprite, 0, muzzleDirection, out movedMuzzle);
            Near(movedMuzzle, new Vector2(9, 8), "Muzzle save preserves mark");
            Debug.Log("Gunner window checks PASSED (drag, flip, undo, redo, save, combo targets, playback lock, reference lock, draft isolation).");
        }
        finally
        {
            if (window != null) { window.DiscardChanges(); window.Close(); }
            AssetDatabase.DeleteAsset(path);
        }
    }

    static void Drag(GunnerBodyCalibrationWindow window, float dx)
    {
        Vector2 start = window.PreviewUpperRect.center;
        window.SendEvent(new Event { type = EventType.MouseDown, button = 0, mousePosition = start });
        window.SendEvent(new Event { type = EventType.MouseDrag, button = 0, mousePosition = start + Vector2.right * dx, delta = Vector2.right * dx });
        window.SendEvent(new Event { type = EventType.MouseUp, button = 0, mousePosition = start + Vector2.right * dx });
    }

    static void ValidateSavedAsset()
    {
        var saved = AssetDatabase.LoadAssetAtPath<GunnerBodyCalibration>(GunnerBodyCalibrationWindow.ProfilePath);
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(GunnerBodyCalibrationWindow.PrefabPath);
        Check(saved != null && prefab != null, "Calibration resource and Player prefab exist");
        var alignment = prefab.GetComponent<GunnerBodyAlignment>();
        Check(alignment != null && alignment.calibration == saved, "Player binds calibration component");
        Check(alignment.upperRenderer != null && alignment.lowerRenderer != null && alignment.fullBody != null, "All renderer/display references resolve");
        var upper = saved.ReferenceUpper;
        Check(upper != null && saved.ReferenceLower != null, "Reference sprites resolve");
        string path = "Assets/GunnerCalibrationCheck_" + Guid.NewGuid().ToString("N") + ".asset";
        var copy = ScriptableObject.CreateInstance<GunnerBodyCalibration>();
        try
        {
            copy.CopyFrom(saved);
            copy.SetOffset(true, upper, new Vector2(0.125f, -0.25f));
            copy.SetMuzzle(upper, 3, GunnerBodyCalibration.MuzzleDirection.Up, new Vector2(11, 13));
            AssetDatabase.CreateAsset(copy, path);
            AssetDatabase.SaveAssetIfDirty(copy);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var loaded = AssetDatabase.LoadAssetAtPath<GunnerBodyCalibration>(path);
            Check(loaded.ReferenceUpper == upper, "Save/reimport preserves Sprite references");
            Near(loaded.GetOffset(true, upper), new Vector2(0.125f, -0.25f), "Save/reimport preserves offset values");
            Check(loaded.TryGetMuzzle(upper, 3, GunnerBodyCalibration.MuzzleDirection.Up, out var pixel), "Reload resolves muzzle sprite and weapon");
            Near(pixel, new Vector2(11, 13), "Reload preserves muzzle pixels");
        }
        finally { AssetDatabase.DeleteAsset(path); }
    }

    public static void RunBatch()
    {
        try { Run(); EditorApplication.Exit(0); }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
    }

    public static void RunWindowBatch()
    {
        try { RunWindowChecks(); EditorApplication.Exit(0); }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
    }

    public static void RunAllBatch()
    {
        try { Run(); RunWindowChecks(); EditorApplication.Exit(0); }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
    }

    static void Check(bool condition, string label)
    {
        assertions++;
        if (!condition) throw new InvalidOperationException("Gunner calibration: " + label);
    }
    static void Near(Vector3 actual, Vector3 expected, string label) => Check((actual - expected).sqrMagnitude < 0.0000001f, label + $" ({actual} vs {expected})");
}
