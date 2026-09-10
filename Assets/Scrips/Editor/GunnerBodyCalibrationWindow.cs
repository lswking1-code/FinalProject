using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

public sealed class GunnerBodyCalibrationWindow : EditorWindow
{
    internal const string PrefabPath = "Assets/Prefabs/Player.prefab";
    internal const string ProfilePath = "Assets/Arts/PlayerG/GunnerBodyCalibration.asset";
    const string ArtPath = "Assets/Arts/PlayerG/jane the gunner/";
    [SerializeField] GameObject prefab;
    [SerializeField] GunnerBodyCalibration saved, draft;
    [SerializeField] string cleanJson;
    [SerializeField] AnimationClip upperClip, lowerClip;
    [SerializeField] int mode;
    [SerializeField] int combinedEditTarget;
    [SerializeField] float time, zoom = 6;
    [SerializeField] bool flipped, grid = true;
    [SerializeField] float targetDuration = 1.2f;
    [SerializeField] int timingScope;
    [SerializeField] bool previewTiming;
    [SerializeField] List<Sprite> selected = new List<Sprite>();
    [SerializeField] Vector2 scroll;
    [SerializeField] bool editMuzzle;
    [SerializeField] int muzzleWeapon;
    [SerializeField] GunnerBodyCalibration.MuzzleDirection muzzleDirection;
    internal Vector2 PreviewMuzzlePoint { get; private set; }
    Rect paintedCanvas;
    GunnerClipFrames upperFrames, lowerFrames;
    SpriteRenderer upperRenderer, lowerRenderer;
    Vector2 upperOrigin, lowerOrigin, upperScale, lowerScale, center;
    bool playing, dragging;
    double lastTick;
    Vector2 dragStart, dragOffset;
    Sprite dragSprite;
    int dragUndoGroup;
    string bindingError;
    internal Rect PreviewUpperRect { get; private set; }
    readonly Dictionary<Sprite, Texture2D> textures = new Dictionary<Sprite, Texture2D>();

    bool UpperMode => mode == 0 || (mode == 2 && combinedEditTarget == 0);
    GunnerClipFrames ActiveFrames => UpperMode ? upperFrames : lowerFrames;
    Sprite CurrentUpper => mode == 1 ? draft.ReferenceUpper : upperFrames.Sample(time, mode == 2 && upperFrames.Clip != null && upperFrames.Clip.isLooping);
    Sprite CurrentLower => mode == 0 ? draft.ReferenceLower : lowerFrames.Sample(time, mode == 2 && lowerFrames.Clip != null && lowerFrames.Clip.isLooping);
    Sprite ActiveSprite => UpperMode ? CurrentUpper : CurrentLower;

    [MenuItem("Tools/Gunner/拼接校准")]
    public static void Open() => GetWindow<GunnerBodyCalibrationWindow>("Gunner 拼接校准");

    void OnEnable()
    {
        minSize = new Vector2(900, 620);
        if (prefab == null) prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (saved == null) saved = AssetDatabase.LoadAssetAtPath<GunnerBodyCalibration>(ProfilePath);
        if (upperClip == null) upperClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(ArtPath + "gunner-idle-upper.anim");
        if (lowerClip == null) lowerClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(ArtPath + "gunner-idle-lower.anim");
        RebuildFrames();
        BindPrefab();
        if (draft == null) LoadDraft();
        EditorApplication.update += Tick;
        Undo.undoRedoPerformed += UndoChanged;
        lastTick = EditorApplication.timeSinceStartup;
        saveChangesMessage = "Gunner 拼接校准有未保存的修改。";
        RefreshDirty();
    }

    void OnDisable()
    {
        EndDrag();
        EditorApplication.update -= Tick;
        Undo.undoRedoPerformed -= UndoChanged;
        playing = false;
    }

    void OnDestroy() { if (draft != null) DestroyImmediate(draft); }
    void OnLostFocus() => EndDrag();

    void RebuildFrames()
    {
        var targets = TimingTargets();
        bool preview = previewTiming && GunnerClipTiming.ValidDuration(targetDuration);
        upperFrames = new GunnerClipFrames(upperClip, preview && targets.Contains(upperClip) ? targetDuration : 0);
        lowerFrames = new GunnerClipFrames(lowerClip, preview && targets.Contains(lowerClip) ? targetDuration : 0);
        textures.Clear();
    }

    AnimationClip[] TimingTargets() => timingScope == 1 ? new[] { upperClip }
        : timingScope == 2 ? new[] { lowerClip } : new[] { upperClip, lowerClip };

    void BindPrefab()
    {
        bindingError = null;
        upperRenderer = lowerRenderer = null;
        if (prefab == null || !PrefabUtility.IsPartOfPrefabAsset(prefab))
        {
            bindingError = "请选择 Project 中的 Player 预制体资源。";
            return;
        }
        var alignment = prefab.GetComponent<GunnerBodyAlignment>();
        upperRenderer = alignment != null ? alignment.upperRenderer : prefab.transform.Find("Up/idle_up")?.GetComponent<SpriteRenderer>();
        lowerRenderer = alignment != null ? alignment.lowerRenderer : prefab.transform.Find("Down/idle_down")?.GetComponent<SpriteRenderer>();
        if (upperRenderer == null || lowerRenderer == null)
        {
            bindingError = "找不到上下半身 SpriteRenderer；请检查 GunnerBodyAlignment 引用。";
            return;
        }
        if (!ReadTransform(upperRenderer, out upperOrigin, out upperScale)
            || !ReadTransform(lowerRenderer, out lowerOrigin, out lowerScale))
            bindingError = "预览要求半身相对角色根节点无旋转、无倾斜且缩放非零。";
        center = (upperOrigin + lowerOrigin) * 0.5f + Vector2.up * 0.7f;
    }

    bool ReadTransform(SpriteRenderer renderer, out Vector2 origin, out Vector2 scale)
    {
        var matrix = prefab.transform.worldToLocalMatrix * renderer.transform.localToWorldMatrix;
        origin = matrix.MultiplyPoint3x4(Vector3.zero);
        scale = new Vector2(matrix.m00, matrix.m11);
        return Mathf.Abs(matrix.m01) < 0.00001f && Mathf.Abs(matrix.m10) < 0.00001f
            && Mathf.Abs(matrix.m20) < 0.00001f && Mathf.Abs(matrix.m21) < 0.00001f
            && Mathf.Abs(scale.x) > 0.00001f && Mathf.Abs(scale.y) > 0.00001f;
    }

    void LoadDraft()
    {
        if (draft != null) { Undo.ClearUndo(draft); DestroyImmediate(draft); }
        draft = CreateInstance<GunnerBodyCalibration>();
        draft.hideFlags = HideFlags.HideAndDontSave;
        if (saved != null) draft.CopyFrom(saved);
        else draft.SetReferences(upperFrames.Sample(0), lowerFrames.Sample(0));
        cleanJson = JsonUtility.ToJson(draft);
        selected.Clear();
        RefreshDirty();
    }

    void UndoChanged()
    {
        if (draft == null) return;
        RebuildFrames();
        time = Mathf.Min(time, mode == 2 ? Mathf.Max(upperFrames.Duration, lowerFrames.Duration) : ActiveFrames.Duration);
        draft.Invalidate();
        RefreshDirty();
        Repaint();
    }

    void RefreshDirty() => hasUnsavedChanges = draft != null && JsonUtility.ToJson(draft) != cleanJson;

    void EditDraft(string label, Action edit)
    {
        Undo.RecordObject(draft, label);
        edit();
        EditorUtility.SetDirty(draft);
        RefreshDirty();
        Repaint();
    }

    void Tick()
    {
        double now = EditorApplication.timeSinceStartup;
        if (playing && !dragging)
        {
            float duration = mode == 2 ? Mathf.Max(upperFrames.Duration, lowerFrames.Duration) : ActiveFrames.Duration;
            time += (float)(now - lastTick);
            if (time >= duration)
            {
                bool loops = mode == 2 ? upperClip != null && lowerClip != null && upperClip.isLooping && lowerClip.isLooping
                    : ActiveFrames.Clip != null && ActiveFrames.Clip.isLooping;
                if (loops) time = Mathf.Repeat(time, duration);
                else { time = duration; playing = false; }
            }
            Repaint();
        }
        lastTick = now;
    }

    void OnGUI()
    {
        if (draft == null) return;
        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
        {
            GUILayout.Label(hasUnsavedChanges ? "● 拼接草稿未保存" : "拼接草稿无修改", GUILayout.Width(130));
            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode || bindingError != null))
                if (GUILayout.Button("保存配置", EditorStyles.toolbarButton, GUILayout.Width(85))) SaveChanges();
            if (GUILayout.Button("撤销", EditorStyles.toolbarButton, GUILayout.Width(50))) Undo.PerformUndo();
            if (GUILayout.Button("重做", EditorStyles.toolbarButton, GUILayout.Width(50))) Undo.PerformRedo();
            if (GUILayout.Button("重新读取已保存配置", EditorStyles.toolbarButton, GUILayout.Width(150)))
            {
                if (!hasUnsavedChanges || EditorUtility.DisplayDialog("重新读取", "放弃尚未保存的拼接修改？", "重新读取", "取消")) LoadDraft();
            }
            GUILayout.FlexibleSpace();
        }
        using (new EditorGUILayout.HorizontalScope())
        {
            using (new EditorGUILayout.VerticalScope(GUILayout.Width(325)))
            {
                scroll = EditorGUILayout.BeginScrollView(scroll);
                DrawControls();
                EditorGUILayout.EndScrollView();
            }
            using (new EditorGUILayout.VerticalScope())
            {
                GUILayout.Label(mode == 0 ? "① 固定参考腿部 · 校准上半身" : mode == 1 ? "② 固定参考上半身 · 校准腿部腰位" : "③ 组合预览 · 暂停后校准", EditorStyles.boldLabel);
                Rect canvas = GUILayoutUtility.GetRect(300, 300, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
                DrawPreview(canvas);
                GUILayout.Label(editMuzzle ? "暂停后点击枪口或拖动十字标记；坐标以原图左下角为零。" : "拖动上半身调整拼接；像素网格以当前校准半身为准。", EditorStyles.miniLabel);
            }
        }
    }

    void DrawControls()
    {
        // One target and one profile: loading another character must not silently reuse its rest pose.
        using (new EditorGUI.DisabledScope(true))
        {
            EditorGUILayout.ObjectField("目标预制体", prefab, typeof(GameObject), false);
            EditorGUILayout.ObjectField("保存配置", saved, typeof(GunnerBodyCalibration), false);
        }
        if (bindingError != null) EditorGUILayout.HelpBox(bindingError, MessageType.Error);
        int nextMode = GUILayout.Toolbar(mode, new[] { "上半身", "腿部腰位", "组合预览" });
        if (nextMode != mode) { EndDrag(); mode = nextMode; time = 0; playing = false; selected.Clear(); }
        EditorGUI.BeginChangeCheck();
        upperClip = (AnimationClip)EditorGUILayout.ObjectField("上半身片段", upperClip, typeof(AnimationClip), false);
        lowerClip = (AnimationClip)EditorGUILayout.ObjectField("下半身片段", lowerClip, typeof(AnimationClip), false);
        if (EditorGUI.EndChangeCheck()) { EndDrag(); previewTiming = false; RebuildFrames(); time = 0; playing = false; selected.Clear(); }
        if (upperFrames.Error != null) EditorGUILayout.HelpBox("上半身：" + upperFrames.Error, MessageType.Warning);
        if (lowerFrames.Error != null) EditorGUILayout.HelpBox("下半身：" + lowerFrames.Error, MessageType.Warning);
        DrawTimingControls();
        DrawReferences();
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button(playing ? "暂停" : "播放")) { playing = !playing; lastTick = EditorApplication.timeSinceStartup; }
            if (GUILayout.Button("上一帧")) Step(-1);
            if (GUILayout.Button("下一帧")) Step(1);
        }
        float maxTime = mode == 2 ? Mathf.Max(upperFrames.Duration, lowerFrames.Duration) : ActiveFrames.Duration;
        EditorGUI.BeginChangeCheck();
        float nextTime = EditorGUILayout.Slider("时间（秒）", time, 0, maxTime);
        if (EditorGUI.EndChangeCheck()) { playing = false; time = nextTime; }
        zoom = EditorGUILayout.Slider("缩放", zoom, 1, 16);
        flipped = EditorGUILayout.Toggle("向左预览", flipped);
        grid = EditorGUILayout.Toggle("像素网格", grid);
        if (mode == 2)
        {
            int target = EditorGUILayout.Popup("修改对象", combinedEditTarget, new[] { "上半身修正", "腿部腰位" });
            if (target != combinedEditTarget) { EndDrag(); combinedEditTarget = target; selected.Clear(); }
            EditorGUILayout.HelpBox(playing ? "请先暂停，再拖动上半身或使用像素微调。"
                : "拖动上半身只修改所选对象；另一项修正保持不变。同一 Sprite 的其他组合也会使用此修改。", MessageType.Info);
        }
        bool nextMuzzle = EditorGUILayout.Toggle("编辑枪口标记", editMuzzle);
        if (nextMuzzle != editMuzzle) { EndDrag(); editMuzzle = nextMuzzle; playing = false; selected.Clear(); }
        if (editMuzzle) DrawMuzzleControls();
        else DrawOffsetControls();
        EditorGUILayout.Space();
        if (!editMuzzle) DrawFrames();
        EditorGUILayout.Space();
        EditorGUILayout.HelpBox("修改只保存在草稿中。保存后仅写入校准资源；运行时由 Player 上的对齐组件使用。", MessageType.Info);
        EditorGUILayout.HelpBox("分体射击使用当前图片的枪口标记；未标记图片和全身动作沿用原 FirePoint。保存后请在游戏内检查射击。", MessageType.None);
    }

    bool CanEditMuzzle => !playing && bindingError == null && upperFrames.Error == null
        && lowerFrames.Error == null && CurrentUpper != null && CurrentLower != null;

    void SetMuzzle(Sprite sprite, Vector2 pixel) => EditDraft("调整枪口标记",
        () => draft.SetMuzzle(sprite, muzzleWeapon, muzzleDirection, pixel));

    void DrawMuzzleControls()
    {
        EditorGUI.BeginChangeCheck();
        muzzleWeapon = EditorGUILayout.Popup("武器", muzzleWeapon, new[] { "0 · 手枪", "1 · 机枪", "2 · 霰弹枪", "3 · 激光", "4 · 火焰" });
        muzzleDirection = (GunnerBodyCalibration.MuzzleDirection)EditorGUILayout.Popup("射击方向", (int)muzzleDirection,
            new[] { "向前", "蹲射（全身动作仍用 FirePoint）", "向上", "向下" });
        if (EditorGUI.EndChangeCheck()) EndDrag();
        var sprite = CurrentUpper;
        bool exists = draft.TryGetMuzzle(sprite, muzzleWeapon, muzzleDirection, out var pixel);
        GUILayout.Label(sprite != null ? sprite.name : "当前上半身为空", EditorStyles.wordWrappedMiniLabel);
        EditorGUILayout.HelpBox(exists ? "绿色十字为已标记枪口。跟随上半身拼接与翻转。" : "橙色十字仅为待设置位置，请点击画面中的实际枪口。", MessageType.Info);
        using (new EditorGUI.DisabledScope(!CanEditMuzzle))
        {
            if (!exists && sprite != null) pixel = sprite.pivot;
            EditorGUI.BeginChangeCheck();
            var next = EditorGUILayout.Vector2Field("原图像素（左下角为零）", pixel);
            if (EditorGUI.EndChangeCheck()) SetMuzzle(sprite, next);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("← 1px")) SetMuzzle(sprite, pixel + Vector2.left);
                if (GUILayout.Button("→ 1px")) SetMuzzle(sprite, pixel + Vector2.right);
                if (GUILayout.Button("↑ 1px")) SetMuzzle(sprite, pixel + Vector2.up);
                if (GUILayout.Button("↓ 1px")) SetMuzzle(sprite, pixel + Vector2.down);
            }
            if (GUILayout.Button("删除本图枪口（恢复 FirePoint）"))
                EditDraft("删除枪口标记", () => draft.RemoveMuzzle(sprite, muzzleWeapon, muzzleDirection));
            using (new EditorGUI.DisabledScope(!exists || selected.Count == 0))
                if (GUILayout.Button($"应用枪口像素到选定图片（{selected.Count}）"))
                    EditDraft("批量设置枪口", () => { foreach (var target in selected) draft.SetMuzzle(target, muzzleWeapon, muzzleDirection, pixel); });
        }
        GUILayout.Label("上半身帧 · 勾选批量目标", EditorStyles.boldLabel);
        foreach (var key in upperFrames.Keys)
        {
            var target = key.value as Sprite;
            if (target == null) continue;
            using (new EditorGUILayout.HorizontalScope())
            {
                bool was = selected.Contains(target);
                bool check = GUILayout.Toggle(was, GUIContent.none, GUILayout.Width(18));
                if (check != was) { if (check) selected.Add(target); else selected.Remove(target); }
                bool known = draft.TryGetMuzzle(target, muzzleWeapon, muzzleDirection, out _);
                if (GUILayout.Button($"{(known ? "✓" : "○")} {key.time:0.###}s {target.name}", EditorStyles.miniButton))
                { EndDrag(); mode = 2; time = key.time; playing = false; }
            }
        }
    }

    void DrawTimingControls()
    {
        EditorGUILayout.Space();
        GUILayout.Label("动画周期", EditorStyles.boldLabel);
        GUILayout.Label($"资源时长：上 { (upperClip != null ? upperClip.length : 0):0.###}s / 下 {(lowerClip != null ? lowerClip.length : 0):0.###}s", EditorStyles.miniLabel);
        EditorGUI.BeginChangeCheck();
        timingScope = EditorGUILayout.Popup("修改范围", timingScope, new[] { "上下半身一起", "仅上半身", "仅下半身" });
        targetDuration = EditorGUILayout.FloatField("目标周期（秒）", targetDuration);
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("0.8 秒")) { targetDuration = 0.8f; GUI.changed = true; }
            if (GUILayout.Button("1.2 秒")) { targetDuration = 1.2f; GUI.changed = true; }
            if (GUILayout.Button("1.6 秒")) { targetDuration = 1.6f; GUI.changed = true; }
        }
        if (EditorGUI.EndChangeCheck()) { EndDrag(); time = 0; RebuildFrames(); }
        string error = TimingTargets().Select(c => GunnerClipTiming.Validate(c, targetDuration)).FirstOrDefault(e => e != null);
        if (error != null) EditorGUILayout.HelpBox(error, MessageType.Warning);
        using (new EditorGUILayout.HorizontalScope())
        {
            using (new EditorGUI.DisabledScope(error != null))
            {
                if (GUILayout.Button("预览新周期"))
                {
                    EndDrag(); previewTiming = true; RebuildFrames(); time = 0;
                    mode = 2; selected.Clear(); playing = true; lastTick = EditorApplication.timeSinceStartup;
                }
                using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
                    if (GUILayout.Button("应用并保存周期")) ApplyTiming();
            }
            using (new EditorGUI.DisabledScope(!previewTiming))
                if (GUILayout.Button("退出预览")) { previewTiming = false; RebuildFrames(); time = 0; playing = false; }
        }
        if (previewTiming) EditorGUILayout.HelpBox("正在预览新周期，尚未修改动画资源。", MessageType.Info);
        GUILayout.Label("按比例调整各帧停留时间。应用会修改所选 .anim，所有引用处生效。", EditorStyles.wordWrappedMiniLabel);
        if (TimingTargets().Any(c => c != null && EditorUtility.IsDirty(c)))
        {
            GUILayout.Label("动画有未保存修改（如撤销后的结果）。", EditorStyles.wordWrappedMiniLabel);
            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
                if (GUILayout.Button("保存当前动画修改"))
                    foreach (var clip in TimingTargets().Where(c => c != null).Distinct()) AssetDatabase.SaveAssetIfDirty(clip);
        }
        EditorGUILayout.Space();
    }

    void ApplyTiming()
    {
        EndDrag(); playing = false;
        try
        {
            GunnerClipTiming.Apply(TimingTargets(), targetDuration);
            previewTiming = false; RebuildFrames(); time = 0;
            ShowNotification(new GUIContent($"动画周期已保存为 {targetDuration:0.###} 秒，可撤销"));
        }
        catch (Exception error) { ShowNotification(new GUIContent(error.Message)); Debug.LogException(error); }
    }

    void DrawReferences()
    {
        using (new EditorGUI.DisabledScope(draft.HasEntries))
        {
            EditorGUI.BeginChangeCheck();
            var up = (Sprite)EditorGUILayout.ObjectField("参考上半身", draft.ReferenceUpper, typeof(Sprite), false);
            var down = (Sprite)EditorGUILayout.ObjectField("参考腿部", draft.ReferenceLower, typeof(Sprite), false);
            if (EditorGUI.EndChangeCheck()) EditDraft("选择拼接参考", () => draft.SetReferences(up, down));
        }
        if (draft.ReferenceUpper == null || draft.ReferenceLower == null)
            EditorGUILayout.HelpBox("请先选择两个参考 Sprite。", MessageType.Warning);
        if (draft.HasEntries && GUILayout.Button("清空草稿校准并解锁参考（可撤销）"))
            EditDraft("清空拼接校准", draft.ClearOffsets);
    }

    void Step(int direction)
    {
        playing = false;
        if (mode != 2) { time = ActiveFrames.Step(time, direction); return; }
        var times = upperFrames.Keys.Concat(lowerFrames.Keys).Select(k => k.time).Distinct().OrderBy(t => t).ToArray();
        if (times.Length == 0) return;
        bool loops = upperClip != null && lowerClip != null && upperClip.isLooping && lowerClip.isLooping;
        if (direction > 0)
        {
            var after = times.Where(t => t > time + 0.00001f).ToArray();
            time = after.Length != 0 ? after[0] : loops ? times[0] : Mathf.Max(time, times[times.Length - 1]);
        }
        else
        {
            var before = times.Where(t => t < time - 0.00001f).ToArray();
            time = before.Length == 0 ? (loops ? times[times.Length - 1] : times[0]) : before[before.Length - 1];
        }
    }

    bool CanEdit => bindingError == null && ActiveFrames.Error == null && ActiveSprite != null
        && draft.ReferenceUpper != null && draft.ReferenceLower != null
        && (mode != 2 || (!playing && upperFrames.Error == null && lowerFrames.Error == null && CurrentUpper != null && CurrentLower != null))
        && (UpperMode || (ActiveSprite != draft.ReferenceLower && (mode == 2 || draft.TryGetOffset(true, draft.ReferenceUpper, out _))));

    Vector2 PixelSize(Sprite sprite, bool upper)
    {
        float ppu = sprite != null ? sprite.pixelsPerUnit : 16;
        Vector2 scale = upper ? upperScale : lowerScale;
        return new Vector2(Mathf.Abs(scale.x) / ppu, Mathf.Abs(scale.y) / ppu);
    }

    void DrawOffsetControls()
    {
        Sprite sprite = ActiveSprite;
        GUILayout.Label(sprite != null ? sprite.name : "当前帧为空", EditorStyles.wordWrappedMiniLabel);
        if (mode != 2 && !UpperMode && !draft.TryGetOffset(true, draft.ReferenceUpper, out _))
            EditorGUILayout.HelpBox("请先在上半身步骤校准参考上半身（零偏移也需点击“标记已校准”）。", MessageType.Info);
        if (!UpperMode && sprite == draft.ReferenceLower)
            EditorGUILayout.HelpBox("参考腿部腰位固定为零，作为所有动作的共同基准。", MessageType.Info);
        using (new EditorGUI.DisabledScope(!CanEdit))
        {
            Vector2 unit = PixelSize(sprite, UpperMode);
            Vector2 offset = draft.GetOffset(UpperMode, sprite);
            Vector2 pixels = new Vector2(offset.x / Mathf.Max(unit.x, 0.000001f), offset.y / Mathf.Max(unit.y, 0.000001f));
            EditorGUI.BeginChangeCheck();
            Vector2 next = EditorGUILayout.Vector2Field("修正（像素，X 向右 / Y 向上）", pixels);
            if (EditorGUI.EndChangeCheck()) SetOffset(sprite, Vector2.Scale(next, unit));
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("← 1px")) SetOffset(sprite, offset + Vector2.left * unit.x);
                if (GUILayout.Button("→ 1px")) SetOffset(sprite, offset + Vector2.right * unit.x);
                if (GUILayout.Button("↑ 1px")) SetOffset(sprite, offset + Vector2.up * unit.y);
                if (GUILayout.Button("↓ 1px")) SetOffset(sprite, offset + Vector2.down * unit.y);
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("标记已校准")) SetOffset(sprite, offset);
                if (GUILayout.Button("重置本图")) EditDraft("重置拼接帧", () => draft.RemoveOffset(UpperMode, sprite));
            }
            using (new EditorGUI.DisabledScope(selected.Count == 0))
                if (GUILayout.Button($"应用当前修正到选定图片（{selected.Count}）"))
                    EditDraft("批量应用拼接修正", () =>
                    {
                        foreach (var target in selected) draft.SetOffset(UpperMode, target, offset);
                    });
        }
    }

    void SetOffset(Sprite sprite, Vector2 offset) => EditDraft("调整拼接位置", () => draft.SetOffset(UpperMode, sprite, offset));

    void DrawFrames()
    {
        if (mode == 2) { DrawMissing(upperFrames, true); DrawMissing(lowerFrames, false); return; }
        GUILayout.Label("帧列表 · 勾选批量应用目标", EditorStyles.boldLabel);
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("全选此片段")) selected = ActiveFrames.Keys.Select(k => k.value as Sprite).Where(s => s != null).Distinct().ToList();
            if (GUILayout.Button("取消全选")) selected.Clear();
        }
        foreach (var key in ActiveFrames.Keys)
        {
            Sprite sprite = key.value as Sprite;
            bool calibrated = draft.TryGetOffset(UpperMode, sprite, out _);
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(sprite == null))
                {
                    bool was = selected.Contains(sprite);
                    bool check = GUILayout.Toggle(was, GUIContent.none, GUILayout.Width(18));
                    if (check != was) { if (check) selected.Add(sprite); else selected.Remove(sprite); }
                }
                string label = $"{key.time:0.###}s  {(calibrated ? "✓" : "○")} {(sprite != null ? sprite.name : "空帧")}";
                if (GUILayout.Button(label, EditorStyles.miniButton)) { time = key.time; playing = false; }
            }
        }
        DrawMissing(ActiveFrames, UpperMode);
    }

    void DrawMissing(GunnerClipFrames frames, bool upper)
    {
        var missing = frames.Keys.Select(k => k.value as Sprite).Where(s => s != null)
            .Distinct().Where(s => !draft.TryGetOffset(upper, s, out _)).ToArray();
        GUILayout.Label($"{(upper ? "上半身" : "腿部腰位")}未校准：{missing.Length}", EditorStyles.boldLabel);
        foreach (var sprite in missing) GUILayout.Label(sprite.name, EditorStyles.wordWrappedMiniLabel);
    }

    void DrawPreview(Rect rect)
    {
        // Input must use the canvas the user actually saw, including after sidebar reflow.
        if (Event.current.type == EventType.Repaint) paintedCanvas = rect;
        else if (Event.current.isMouse && paintedCanvas.width > 0) rect = paintedCanvas;
        EditorGUI.DrawRect(rect, new Color(0.10f, 0.11f, 0.13f));
        if (bindingError != null) { GUI.Label(rect, bindingError); return; }
        if (upperFrames.Error != null || lowerFrames.Error != null)
        {
            GUI.Label(rect, "请选择可预览的上下半身 Sprite 动画。");
            return;
        }
        Sprite up = CurrentUpper, down = CurrentLower;
        float ppu = (ActiveSprite != null ? ActiveSprite.pixelsPerUnit : 16);
        float pixels = ppu * zoom;
        GUI.BeginGroup(rect);
        Rect local = new Rect(0, 0, rect.width, rect.height);
        Vector2 screenCenter = local.center;
        if (grid && Event.current.type == EventType.Repaint)
        {
            Vector2 unit = PixelSize(ActiveSprite, UpperMode);
            float sx = Mathf.Max(4, unit.x * pixels), sy = Mathf.Max(4, unit.y * pixels);
            var color = new Color(1, 1, 1, 0.045f);
            Vector2 origin = ToScreen(Vector2.zero, screenCenter, pixels);
            for (float x = Mathf.Repeat(origin.x, sx); x < local.width; x += sx) EditorGUI.DrawRect(new Rect(x, 0, 1, local.height), color);
            for (float y = Mathf.Repeat(origin.y, sy); y < local.height; y += sy) EditorGUI.DrawRect(new Rect(0, y, local.width, 1), color);
        }
        Vector2 offset = draft.GetCombinedOffset(up, down);
        Rect upRect = SpriteRect(up, upperOrigin + offset, upperScale, upperRenderer, screenCenter, pixels, out var upUV);
        Rect downRect = SpriteRect(down, lowerOrigin, lowerScale, lowerRenderer, screenCenter, pixels, out var downUV);
        PreviewUpperRect = new Rect(upRect.position + rect.position, upRect.size);
        if (upperRenderer.sortingOrder >= lowerRenderer.sortingOrder)
        { DrawSprite(down, downRect, downUV); DrawSprite(up, upRect, upUV); }
        else { DrawSprite(up, upRect, upUV); DrawSprite(down, downRect, downUV); }
        GUI.Label(new Rect(8, 6, local.width - 16, 45), $"上：{(up != null ? up.name : "空")}\n下：{(down != null ? down.name : "空")}", EditorStyles.whiteMiniLabel);
        if (editMuzzle) DrawMuzzle(local, screenCenter, pixels, upperOrigin + offset, rect.position);
        else HandleDrag(local, upRect, pixels);
        GUI.EndGroup();
    }

    void DrawMuzzle(Rect canvas, Vector2 origin, float pixels, Vector2 bodyPosition, Vector2 windowOrigin)
    {
        var sprite = CurrentUpper;
        if (sprite == null) return;
        bool exists = draft.TryGetMuzzle(sprite, muzzleWeapon, muzzleDirection, out var pixel);
        if (!exists) pixel = sprite.pivot;
        var local = GunnerBodyCalibration.MuzzleLocalPosition(sprite, pixel, upperRenderer.flipX, upperRenderer.flipY);
        var point = ToScreen(bodyPosition + Vector2.Scale(local, upperScale), origin, pixels);
        if (Event.current.type == EventType.Repaint)
            PreviewMuzzlePoint = GUIUtility.GUIToScreenPoint(point) - position.position;
        var color = exists ? Color.green : new Color(1, 0.65f, 0.15f);
        EditorGUI.DrawRect(new Rect(point.x - 9, point.y - 1, 19, 2), color);
        EditorGUI.DrawRect(new Rect(point.x - 1, point.y - 9, 2, 19), color);
        int id = GUIUtility.GetControlID("GunnerMuzzleDrag".GetHashCode(), FocusType.Passive);
        var ev = Event.current;
        if (CanEditMuzzle) EditorGUIUtility.AddCursorRect(canvas, MouseCursor.ArrowPlus);
        if (ev.type == EventType.MouseDown && ev.button == 0 && CanEditMuzzle && canvas.Contains(ev.mousePosition))
        {
            dragging = true; dragSprite = sprite;
            Undo.IncrementCurrentGroup(); dragUndoGroup = Undo.GetCurrentGroup(); GUIUtility.hotControl = id;
        }
        if (dragging && GUIUtility.hotControl == id && (ev.type == EventType.MouseDown || ev.type == EventType.MouseDrag))
        {
            var relative = (ev.mousePosition - origin) / pixels;
            var root = center + new Vector2(relative.x * (flipped ? -1 : 1), -relative.y);
            var delta = root - bodyPosition;
            var next = new Vector2(delta.x / upperScale.x * (upperRenderer.flipX ? -1 : 1),
                delta.y / upperScale.y * (upperRenderer.flipY ? -1 : 1)) * sprite.pixelsPerUnit + sprite.pivot;
            SetMuzzle(dragSprite, new Vector2(Mathf.Round(next.x), Mathf.Round(next.y)));
            ev.Use();
        }
        else if (ev.type == EventType.MouseUp && dragging) { EndDrag(); ev.Use(); }
    }

    Vector2 ToScreen(Vector2 point, Vector2 origin, float pixels)
    {
        Vector2 relative = point - center;
        return origin + new Vector2(relative.x * (flipped ? -pixels : pixels), -relative.y * pixels);
    }

    Rect SpriteRect(Sprite sprite, Vector2 position, Vector2 scale, SpriteRenderer renderer,
        Vector2 origin, float pixels, out Rect uv)
    {
        uv = new Rect();
        if (sprite == null) return new Rect();
        Texture2D texture = SourceTexture(sprite);
        if (texture == null) return new Rect();
        Vector2 size = sprite.rect.size / sprite.pixelsPerUnit;
        Vector2 pivot = sprite.pivot / sprite.pixelsPerUnit;
        Vector2 signedScale = Vector2.Scale(scale, new Vector2(renderer.flipX ? -1 : 1, renderer.flipY ? -1 : 1));
        Vector2 bottomLeft = position + Vector2.Scale(-pivot, signedScale);
        Vector2 topRight = position + Vector2.Scale(size - pivot, signedScale);
        Vector2 a = ToScreen(bottomLeft, origin, pixels), b = ToScreen(topRight, origin, pixels);
        Rect source = sprite.rect;
        uv = new Rect(source.x / texture.width, source.y / texture.height, source.width / texture.width, source.height / texture.height);
        if (a.x > b.x) { uv.x += uv.width; uv.width = -uv.width; }
        if (a.y < b.y) { uv.y += uv.height; uv.height = -uv.height; }
        return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
    }

    Texture2D SourceTexture(Sprite sprite)
    {
        if (!textures.TryGetValue(sprite, out var texture))
        {
            texture = AssetDatabase.LoadAssetAtPath<Texture2D>(AssetDatabase.GetAssetPath(sprite));
            textures[sprite] = texture;
        }
        return texture;
    }

    void DrawSprite(Sprite sprite, Rect rect, Rect uv)
    {
        if (sprite == null || Event.current.type != EventType.Repaint) return;
        Texture2D texture = SourceTexture(sprite);
        if (texture != null) GUI.DrawTextureWithTexCoords(rect, texture, uv, true);
    }

    void HandleDrag(Rect canvas, Rect upperRect, float pixels)
    {
        int id = GUIUtility.GetControlID("GunnerCalibrationDrag".GetHashCode(), FocusType.Passive);
        Event ev = Event.current;
        if (CanEdit) EditorGUIUtility.AddCursorRect(upperRect, MouseCursor.MoveArrow);
        if (ev.type == EventType.MouseDown && ev.button == 0 && CanEdit && upperRect.Contains(ev.mousePosition) && canvas.Contains(ev.mousePosition))
        {
            playing = false;
            dragging = true;
            dragStart = ev.mousePosition;
            dragSprite = ActiveSprite;
            dragOffset = draft.GetOffset(UpperMode, dragSprite);
            Undo.IncrementCurrentGroup();
            dragUndoGroup = Undo.GetCurrentGroup();
            GUIUtility.hotControl = id;
            ev.Use();
        }
        else if (ev.type == EventType.MouseDrag && dragging && GUIUtility.hotControl == id)
        {
            Vector2 delta = ev.mousePosition - dragStart;
            delta = new Vector2(delta.x * (flipped ? -1 : 1), -delta.y) / pixels;
            Vector2 unit = PixelSize(dragSprite, UpperMode);
            delta = new Vector2(Mathf.Round(delta.x / unit.x) * unit.x, Mathf.Round(delta.y / unit.y) * unit.y);
            SetOffset(dragSprite, dragOffset + delta);
            ev.Use();
        }
        else if (ev.type == EventType.MouseUp && dragging) { EndDrag(); ev.Use(); }
    }

    void EndDrag()
    {
        if (!dragging) return;
        Undo.CollapseUndoOperations(dragUndoGroup);
        dragging = false;
        GUIUtility.hotControl = 0;
    }

    public override void SaveChanges()
    {
        if (draft == null || bindingError != null || EditorApplication.isPlayingOrWillChangePlaymode) return;
        EndDrag();
        if (draft.ReferenceUpper == null || draft.ReferenceLower == null)
        {
            ShowNotification(new GUIContent("请先选择两个参考 Sprite"));
            return;
        }
        if (saved == null)
        {
            saved = CreateInstance<GunnerBodyCalibration>();
            saved.CopyFrom(draft);
            AssetDatabase.CreateAsset(saved, ProfilePath);
        }
        else
        {
            saved.CopyFrom(draft);
            EditorUtility.SetDirty(saved);
        }
        AssetDatabase.SaveAssetIfDirty(saved);
        var component = prefab.GetComponent<GunnerBodyAlignment>();
        if (component == null || component.calibration != saved)
        {
            ShowNotification(new GUIContent("配置已保存；请在 Player 的 GunnerBodyAlignment 中绑定此配置。"));
        }
        else ShowNotification(new GUIContent("已保存，Player 将使用此校准配置"));
        cleanJson = JsonUtility.ToJson(draft);
        base.SaveChanges();
    }

    public override void DiscardChanges() { LoadDraft(); base.DiscardChanges(); }
}

[CustomEditor(typeof(GunnerBodyCalibration))]
sealed class GunnerBodyCalibrationInspector : Editor
{
    public override void OnInspectorGUI()
    {
        EditorGUILayout.HelpBox("使用拼接窗口预览、批量校准和保存。偏移以角色本地空间存储。", MessageType.Info);
        if (GUILayout.Button("打开 Gunner 拼接校准")) GunnerBodyCalibrationWindow.Open();
        using (new EditorGUI.DisabledScope(true)) DrawDefaultInspector();
    }
}
