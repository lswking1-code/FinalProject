#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Tilemaps;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Tile Palette 笔刷：在网格上生成 ElectrifiedPlatform，相邻格合并为一条碰撞，视觉每格一张图。
/// 不向 Tilemap 写入 Tile。
/// </summary>
[CustomGridBrush(true, false, false, "Electrified Platform Brush")]
public class ElectrifiedPlatformBrush : GridBrushBase
{
    const string DefaultPrefabPath = "Assets/Prefabs/ElectrifiedPlatform.prefab";

    [SerializeField] GameObject prefab;
    [SerializeField] bool defaultIsOn = true;
    [SerializeField] string rootName = "ElectrifiedPlatforms";

    public GameObject Prefab
    {
        get => prefab;
        set => prefab = value;
    }

    public bool DefaultIsOn
    {
        get => defaultIsOn;
        set => defaultIsOn = value;
    }

    public string RootName
    {
        get => string.IsNullOrWhiteSpace(rootName) ? "ElectrifiedPlatforms" : rootName;
        set => rootName = value;
    }

    public override void Paint(GridLayout gridLayout, GameObject brushTarget, Vector3Int position)
    {
        PaintCells(gridLayout, brushTarget, CollectCells(new BoundsInt(position, Vector3Int.one)));
    }

    public override void Erase(GridLayout gridLayout, GameObject brushTarget, Vector3Int position)
    {
        EraseCells(gridLayout, brushTarget, CollectCells(new BoundsInt(position, Vector3Int.one)));
    }

    public override void BoxFill(GridLayout gridLayout, GameObject brushTarget, BoundsInt position)
    {
        PaintCells(gridLayout, brushTarget, CollectCells(position));
    }

    public override void BoxErase(GridLayout gridLayout, GameObject brushTarget, BoundsInt position)
    {
        EraseCells(gridLayout, brushTarget, CollectCells(position));
    }

    public override void FloodFill(GridLayout gridLayout, GameObject brushTarget, Vector3Int position)
    {
        Debug.LogWarning("[ElectrifiedPlatformBrush] FloodFill 未实现。");
    }

    internal void CollectPreviewUnions(GridLayout gridLayout, GameObject brushTarget, BoundsInt position, List<BoundsInt> results)
    {
        results.Clear();
        if (gridLayout == null)
            return;

        gridLayout = ResolveCellLayout(gridLayout, brushTarget);
        var byRow = GroupByRow(CollectCells(position));
        foreach (var kv in byRow)
        {
            foreach (var run in EnumerateRuns(kv.Value))
            {
                int minX = run.x0;
                int maxEx = run.x1 + 1;
                var spans = FindTouchingSpans(kv.Key.y, kv.Key.z, run.x0, run.x1);
                for (int i = 0; i < spans.Count; i++)
                {
                    minX = Mathf.Min(minX, spans[i].MinX);
                    maxEx = Mathf.Max(maxEx, spans[i].MaxXExclusive);
                }

                results.Add(new BoundsInt(minX, kv.Key.y, kv.Key.z, maxEx - minX, 1, 1));
            }
        }
    }

    void PaintCells(GridLayout gridLayout, GameObject brushTarget, List<Vector3Int> cells)
    {
        if (!TryResolvePaintContext(gridLayout, brushTarget, cells, out GridLayout grid, out GameObject resolvedPrefab, out Transform parent))
            return;

        Undo.IncrementCurrentGroup();
        int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Paint Electrified Platform");

        AdoptExisting(grid);
        var byRow = GroupByRow(cells);
        foreach (var kv in byRow)
        {
            foreach (var run in EnumerateRuns(kv.Value))
                EnsureRun(grid, parent, resolvedPrefab, kv.Key.y, kv.Key.z, run.x0, run.x1);
        }

        Undo.CollapseUndoOperations(group);
    }

    void EraseCells(GridLayout gridLayout, GameObject brushTarget, List<Vector3Int> cells)
    {
        if (gridLayout == null || cells == null || cells.Count == 0)
            return;

        gridLayout = ResolveCellLayout(gridLayout, brushTarget);
        GameObject resolvedPrefab = ResolvePrefab();
        Undo.IncrementCurrentGroup();
        int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Erase Electrified Platform");

        AdoptExisting(gridLayout);

        var eraseSet = new HashSet<Vector3Int>(cells);
        var affected = new List<ElectrifiedPlatformGridSpan>();
        var all = FindSceneSpans();
        for (int i = 0; i < all.Count; i++)
        {
            var span = all[i];
            if (span == null)
                continue;

            bool hit = false;
            for (int x = span.MinX; x < span.MaxXExclusive; x++)
            {
                if (eraseSet.Contains(new Vector3Int(x, span.Y, span.Z)))
                {
                    hit = true;
                    break;
                }
            }

            if (hit)
                affected.Add(span);
        }

        var parent = ResolvePlatformsRoot(gridLayout, brushTarget);
        for (int i = 0; i < affected.Count; i++)
        {
            if (affected[i] != null)
                EraseFromSpan(gridLayout, parent, resolvedPrefab, affected[i], eraseSet);
        }

        Undo.CollapseUndoOperations(group);
    }

    bool TryResolvePaintContext(
        GridLayout gridLayout,
        GameObject brushTarget,
        List<Vector3Int> cells,
        out GridLayout grid,
        out GameObject resolvedPrefab,
        out Transform parent)
    {
        grid = gridLayout;
        resolvedPrefab = null;
        parent = null;

        if (grid == null || cells == null || cells.Count == 0)
            return false;

        grid = ResolveCellLayout(grid, brushTarget);

        resolvedPrefab = ResolvePrefab();
        if (resolvedPrefab == null || resolvedPrefab.GetComponent<ElectrifiedPlatform>() == null)
        {
            Debug.LogWarning("[ElectrifiedPlatformBrush] 未找到有效的 ElectrifiedPlatform Prefab。");
            return false;
        }

        parent = ResolvePlatformsRoot(grid, brushTarget);
        return true;
    }

    void EnsureRun(GridLayout grid, Transform parent, GameObject resolvedPrefab, int y, int z, int x0, int x1Inclusive)
    {
        var spans = FindTouchingSpans(y, z, x0, x1Inclusive);
        int minX = x0;
        int maxEx = x1Inclusive + 1;
        for (int i = 0; i < spans.Count; i++)
        {
            minX = Mathf.Min(minX, spans[i].MinX);
            maxEx = Mathf.Max(maxEx, spans[i].MaxXExclusive);
        }

        ElectrifiedPlatformGridSpan keep = null;
        for (int i = 0; i < spans.Count; i++)
        {
            if (keep == null || spans[i].MinX < keep.MinX)
                keep = spans[i];
        }

        var origin = new Vector3Int(minX, y, z);
        int width = maxEx - minX;

        if (keep == null)
        {
            CreateInstance(grid, parent, resolvedPrefab, origin, width, defaultIsOn);
            return;
        }

        for (int i = 0; i < spans.Count; i++)
        {
            if (spans[i] == null || spans[i] == keep)
                continue;

            var from = spans[i].GetComponent<ElectrifiedPlatform>();
            var to = keep.GetComponent<ElectrifiedPlatform>();
            ElectrifiedPlatformRefRetarget.Replace(from, to);
            Undo.DestroyObjectImmediate(spans[i].gameObject);
        }

        ApplySpan(grid, keep, origin, width);
    }

    void EraseFromSpan(
        GridLayout grid,
        Transform parent,
        GameObject resolvedPrefab,
        ElectrifiedPlatformGridSpan span,
        HashSet<Vector3Int> eraseSet)
    {
        var remaining = new List<int>();
        for (int x = span.MinX; x < span.MaxXExclusive; x++)
        {
            if (!eraseSet.Contains(new Vector3Int(x, span.Y, span.Z)))
                remaining.Add(x);
        }

        var platform = span.GetComponent<ElectrifiedPlatform>();
        bool powered = platform != null && platform.IsOn;
        int y = span.Y;
        int z = span.Z;

        if (remaining.Count == 0)
        {
            ElectrifiedPlatformRefRetarget.Replace(platform, null);
            Undo.DestroyObjectImmediate(span.gameObject);
            return;
        }

        bool first = true;
        foreach (var run in EnumerateRuns(remaining))
        {
            var origin = new Vector3Int(run.x0, y, z);
            int width = run.x1 - run.x0 + 1;
            if (first)
            {
                ApplySpan(grid, span, origin, width);
                first = false;
                continue;
            }

            if (resolvedPrefab == null)
            {
                Debug.LogWarning("[ElectrifiedPlatformBrush] 拆分平台时找不到 Prefab，已跳过右段。");
                continue;
            }

            CreateInstance(grid, parent, resolvedPrefab, origin, width, powered);
        }
    }

    ElectrifiedPlatformGridSpan CreateInstance(
        GridLayout grid,
        Transform parent,
        GameObject resolvedPrefab,
        Vector3Int origin,
        int width,
        bool powered)
    {
        GameObject instance = parent != null
            ? PrefabUtility.InstantiatePrefab(resolvedPrefab, parent) as GameObject
            : PrefabUtility.InstantiatePrefab(resolvedPrefab) as GameObject;

        if (instance == null)
        {
            Debug.LogError("[ElectrifiedPlatformBrush] InstantiatePrefab 失败。");
            return null;
        }

        Undo.RegisterCreatedObjectUndo(instance, "Paint Electrified Platform");

        var platform = instance.GetComponent<ElectrifiedPlatform>();
        if (platform != null)
            platform.ApplyEditorPaintDefaults(powered);

        var span = instance.GetComponent<ElectrifiedPlatformGridSpan>();
        if (span == null)
            span = Undo.AddComponent<ElectrifiedPlatformGridSpan>(instance);

        ApplySpan(grid, span, origin, width);
        return span;
    }

    static void ApplySpan(GridLayout grid, ElectrifiedPlatformGridSpan span, Vector3Int origin, int width)
    {
        if (span == null)
            return;

        Undo.RecordObject(span, "Resize Electrified Platform");
        Undo.RecordObject(span.transform, "Resize Electrified Platform");
        Undo.RecordObject(span.gameObject, "Resize Electrified Platform");

        span.SetSpan(origin, width);
        span.transform.position = SpanCenterWorld(grid, origin, width);
        span.transform.localRotation = Quaternion.identity;
        span.transform.localScale = ComputeCellScale(grid, span.transform.parent);
        span.gameObject.name = FormatName(origin, width);
        span.ApplyMergedLayout();

        EditorUtility.SetDirty(span);
        EditorUtility.SetDirty(span.gameObject);
        if (span.gameObject.scene.IsValid())
            EditorSceneManager.MarkSceneDirty(span.gameObject.scene);
    }

    static Vector3 ComputeCellScale(GridLayout grid, Transform parent)
    {
        float parentSx = parent != null ? Mathf.Abs(parent.lossyScale.x) : 1f;
        float parentSy = parent != null ? Mathf.Abs(parent.lossyScale.y) : 1f;
        if (parentSx < 0.0001f)
            parentSx = 1f;
        if (parentSy < 0.0001f)
            parentSy = 1f;

        return new Vector3(
            grid.cellSize.x / parentSx,
            grid.cellSize.y / parentSy,
            1f);
    }

    internal static GridLayout ResolveCellLayout(GridLayout gridLayout, GameObject brushTarget)
    {
        if (brushTarget != null)
        {
            var tilemap = brushTarget.GetComponent<Tilemap>();
            if (tilemap != null)
                return tilemap;

            var targetLayout = brushTarget.GetComponent<GridLayout>();
            if (targetLayout != null)
                return targetLayout;
        }

        return gridLayout;
    }

    static Vector3 CellCenterWorld(GridLayout grid, Vector3Int cell)
    {
        if (grid is Tilemap tilemap)
            return tilemap.GetCellCenterWorld(cell);

        if (grid is Grid sceneGrid)
            return sceneGrid.GetCellCenterWorld(cell);

        Vector3 interpolated = new Vector3(cell.x + 0.5f, cell.y + 0.5f, cell.z);
        return grid.LocalToWorld(grid.CellToLocalInterpolated(interpolated));
    }

    static Vector3 SpanCenterWorld(GridLayout grid, Vector3Int origin, int width)
    {
        Vector3 left = CellCenterWorld(grid, origin);
        Vector3 right = CellCenterWorld(grid, origin + new Vector3Int(Mathf.Max(0, width - 1), 0, 0));
        return (left + right) * 0.5f;
    }

    static string FormatName(Vector3Int origin, int width)
    {
        return $"ElectrifiedPlatform ({origin.x},{origin.y} x{width})";
    }

    GameObject ResolvePrefab()
    {
        if (prefab != null)
            return prefab;

        return AssetDatabase.LoadAssetAtPath<GameObject>(DefaultPrefabPath);
    }

    Transform ResolvePlatformsRoot(GridLayout gridLayout, GameObject brushTarget)
    {
        Transform rootParent = ResolveRootParent(gridLayout, brushTarget);
        if (rootParent == null)
            return null;

        Transform existing = rootParent.Find(RootName);
        if (existing != null)
            return existing;

        var go = new GameObject(RootName);
        Undo.RegisterCreatedObjectUndo(go, "Create ElectrifiedPlatforms");
        go.transform.SetParent(rootParent, false);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;
        return go.transform;
    }

    static Transform ResolveRootParent(GridLayout gridLayout, GameObject brushTarget)
    {
        Transform start = null;
        if (gridLayout != null)
            start = gridLayout.transform;
        else if (brushTarget != null)
            start = brushTarget.transform;

        if (start == null)
            return null;

        var grid = start.GetComponent<Grid>();
        if (grid == null)
            grid = start.GetComponentInParent<Grid>();
        if (grid != null)
            return grid.transform;

        if (start.GetComponent<CompositeCollider2D>() != null && start.parent != null)
            return start.parent;

        return start;
    }

    static void AdoptExisting(GridLayout grid)
    {
        if (grid == null)
            return;

        var platforms = Object.FindObjectsByType<ElectrifiedPlatform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < platforms.Length; i++)
        {
            var platform = platforms[i];
            if (platform == null || EditorUtility.IsPersistent(platform) || !platform.gameObject.scene.IsValid())
                continue;
            if (platform.GetComponent<ElectrifiedPlatformGridSpan>() != null)
                continue;
            if (!TryInferSpan(grid, platform, out Vector3Int origin, out int width))
                continue;

            var span = Undo.AddComponent<ElectrifiedPlatformGridSpan>(platform.gameObject);
            Undo.RecordObject(span, "Adopt Electrified Platform");
            span.SetSpan(origin, width);
            EditorUtility.SetDirty(span);
        }
    }

    static bool TryInferSpan(GridLayout grid, ElectrifiedPlatform platform, out Vector3Int origin, out int width)
    {
        origin = default;
        width = 0;

        var box = platform.GetComponent<BoxCollider2D>();
        if (box == null)
            return false;

        Bounds bounds = box.bounds;
        Vector3Int leftCell = grid.WorldToCell(new Vector3(bounds.min.x + 0.001f, bounds.center.y, bounds.center.z));
        Vector3Int rightCell = grid.WorldToCell(new Vector3(bounds.max.x - 0.001f, bounds.center.y, bounds.center.z));
        Vector3Int centerCell = grid.WorldToCell(bounds.center);
        leftCell.y = rightCell.y = centerCell.y;
        leftCell.z = rightCell.z = centerCell.z;

        origin = leftCell;
        width = rightCell.x - leftCell.x + 1;
        return width > 0;
    }

    static List<ElectrifiedPlatformGridSpan> FindTouchingSpans(int y, int z, int x0, int x1Inclusive)
    {
        var result = new List<ElectrifiedPlatformGridSpan>();
        var all = FindSceneSpans();
        for (int i = 0; i < all.Count; i++)
        {
            var span = all[i];
            if (span != null && span.TouchesOrOverlaps(y, z, x0, x1Inclusive))
                result.Add(span);
        }

        return result;
    }

    static List<ElectrifiedPlatformGridSpan> FindSceneSpans()
    {
        var result = new List<ElectrifiedPlatformGridSpan>();
        var all = Object.FindObjectsByType<ElectrifiedPlatformGridSpan>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < all.Length; i++)
        {
            var span = all[i];
            if (span == null || EditorUtility.IsPersistent(span) || !span.gameObject.scene.IsValid())
                continue;
            result.Add(span);
        }

        return result;
    }

    static List<Vector3Int> CollectCells(BoundsInt bounds)
    {
        var cells = new List<Vector3Int>();
        foreach (var cell in bounds.allPositionsWithin)
            cells.Add(cell);
        return cells;
    }

    static Dictionary<(int y, int z), List<int>> GroupByRow(List<Vector3Int> cells)
    {
        var byRow = new Dictionary<(int y, int z), List<int>>();
        for (int i = 0; i < cells.Count; i++)
        {
            var cell = cells[i];
            var key = (cell.y, cell.z);
            if (!byRow.TryGetValue(key, out var xs))
            {
                xs = new List<int>();
                byRow[key] = xs;
            }

            if (!xs.Contains(cell.x))
                xs.Add(cell.x);
        }

        foreach (var list in byRow.Values)
            list.Sort();

        return byRow;
    }

    static IEnumerable<(int x0, int x1)> EnumerateRuns(List<int> sortedXs)
    {
        if (sortedXs == null || sortedXs.Count == 0)
            yield break;

        int start = sortedXs[0];
        int prev = sortedXs[0];
        for (int i = 1; i < sortedXs.Count; i++)
        {
            int x = sortedXs[i];
            if (x > prev + 1)
            {
                yield return (start, prev);
                start = x;
            }

            prev = x;
        }

        yield return (start, prev);
    }
}

static class ElectrifiedPlatformRefRetarget
{
    public static void Replace(ElectrifiedPlatform from, ElectrifiedPlatform to)
    {
        if (from == null || from == to)
            return;

        ReplaceArray<ToggleSwitch>(from, to, "targets");
        ReplaceArray<PressurePlate>(from, to, "targets");
        ReplaceArray<ElevatorFloorHazard>(from, to, "segments");
    }

    static void ReplaceArray<T>(ElectrifiedPlatform from, ElectrifiedPlatform to, string propertyName)
        where T : Object
    {
        var owners = Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < owners.Length; i++)
        {
            var owner = owners[i];
            if (owner == null || EditorUtility.IsPersistent(owner))
                continue;

            var so = new SerializedObject(owner);
            var prop = so.FindProperty(propertyName);
            if (prop == null || !prop.isArray)
                continue;

            bool changed = false;
            for (int e = 0; e < prop.arraySize; e++)
            {
                if (prop.GetArrayElementAtIndex(e).objectReferenceValue == from)
                    changed = true;
            }

            if (!changed)
                continue;

            Undo.RecordObject(owner, "Retarget Electrified Platform");
            for (int e = 0; e < prop.arraySize; e++)
            {
                var element = prop.GetArrayElementAtIndex(e);
                if (element.objectReferenceValue == from)
                    element.objectReferenceValue = to;
            }

            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(owner);
        }
    }
}

[CustomEditor(typeof(ElectrifiedPlatformBrush))]
public class ElectrifiedPlatformBrushEditor : GridBrushEditorBase
{
    static readonly Color PaintColor = new Color(0.35f, 0.85f, 1f, 1f);
    static readonly Color EraseColor = new Color(1f, 0.4f, 0.35f, 1f);
    static readonly Color MergeColor = new Color(0.2f, 1f, 0.75f, 0.95f);
    static readonly List<BoundsInt> PreviewUnions = new List<BoundsInt>();

    public override string tooltip => "绘制通电平台：相邻格合并碰撞，每格一张图，不写入 Tilemap。";

    public override bool canChangeZPosition
    {
        get => false;
        set { }
    }

    public override GameObject[] validTargets
    {
        get
        {
            var layouts = Object.FindObjectsByType<GridLayout>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            var result = new List<GameObject>(layouts.Length);
            for (int i = 0; i < layouts.Length; i++)
            {
                var layout = layouts[i];
                if (layout != null && layout.gameObject.scene.isLoaded && layout.gameObject.activeInHierarchy)
                    result.Add(layout.gameObject);
            }

            return result.ToArray();
        }
    }

    public override void OnPaintInspectorGUI()
    {
        serializedObject.Update();
        EditorGUILayout.PropertyField(serializedObject.FindProperty("prefab"), new GUIContent("Prefab"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("defaultIsOn"), new GUIContent("默认通电"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("rootName"), new GUIContent("父节点名"));
        serializedObject.ApplyModifiedProperties();

        var brush = (ElectrifiedPlatformBrush)target;
        if (brush.Prefab == null)
        {
            var fallback = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/ElectrifiedPlatform.prefab");
            EditorGUILayout.HelpBox(
                fallback != null
                    ? "未指定 Prefab 时将使用 Assets/Prefabs/ElectrifiedPlatform.prefab。"
                    : "未找到 ElectrifiedPlatform Prefab，请在上方指定。",
                fallback != null ? MessageType.Info : MessageType.Warning);
        }

        EditorGUILayout.HelpBox("在网格上绘制通电平台。同一行相邻格子合并为一条碰撞，每格一张图（不拉伸）。不会写入 Tilemap。开关仍需手动接线。", MessageType.Info);
    }

    public override void OnPaintSceneGUI(
        GridLayout gridLayout,
        GameObject brushTarget,
        BoundsInt position,
        GridBrushBase.Tool tool,
        bool executing)
    {
        if (Event.current.type != EventType.Repaint || gridLayout == null)
            return;

        gridLayout = ElectrifiedPlatformBrush.ResolveCellLayout(gridLayout, brushTarget);

        Color color = PaintColor;
        if (tool == GridBrushBase.Tool.Erase)
            color = EraseColor;
        if (executing)
            color = Color.yellow;

        DrawBounds(gridLayout, position, color);

        var brush = target as ElectrifiedPlatformBrush;
        if (brush == null)
            return;
        if (tool != GridBrushBase.Tool.Paint && tool != GridBrushBase.Tool.Box)
            return;

        brush.CollectPreviewUnions(gridLayout, brushTarget, position, PreviewUnions);
        for (int i = 0; i < PreviewUnions.Count; i++)
        {
            if (PreviewUnions[i] == position)
                continue;
            DrawBounds(gridLayout, PreviewUnions[i], executing ? Color.yellow : MergeColor);
        }
    }

    static void DrawBounds(GridLayout grid, BoundsInt bounds, Color color)
    {
        Vector3 p0 = grid.CellToWorld(new Vector3Int(bounds.xMin, bounds.yMin, bounds.zMin));
        Vector3 p1 = grid.CellToWorld(new Vector3Int(bounds.xMax, bounds.yMin, bounds.zMin));
        Vector3 p2 = grid.CellToWorld(new Vector3Int(bounds.xMax, bounds.yMax, bounds.zMin));
        Vector3 p3 = grid.CellToWorld(new Vector3Int(bounds.xMin, bounds.yMax, bounds.zMin));

        Handles.color = color;
        Handles.DrawLine(p0, p1);
        Handles.DrawLine(p1, p2);
        Handles.DrawLine(p2, p3);
        Handles.DrawLine(p3, p0);
    }
}
#endif
