#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Tilemaps;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Tile Palette 笔刷：在网格上生成竖直 LaserGate，同列相邻格合并碰撞，两端发射器、中间激光束。
/// 不向 Tilemap 写入 Tile。
/// </summary>
[CustomGridBrush(true, false, false, "Laser Gate Brush")]
public class LaserGateBrush : GridBrushBase
{
    const string DefaultPrefabPath = "Assets/Prefabs/LaserGate.prefab";

    [SerializeField] GameObject prefab;
    [SerializeField] bool defaultIsActive = true;
    [SerializeField] string rootName = "LaserGates";

    public GameObject Prefab
    {
        get => prefab;
        set => prefab = value;
    }

    public bool DefaultIsActive
    {
        get => defaultIsActive;
        set => defaultIsActive = value;
    }

    public string RootName
    {
        get => string.IsNullOrWhiteSpace(rootName) ? "LaserGates" : rootName;
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
        Debug.LogWarning("[LaserGateBrush] FloodFill 未实现。");
    }

    internal void CollectPreviewUnions(
        GridLayout gridLayout,
        GameObject brushTarget,
        BoundsInt position,
        List<BoundsInt> results)
    {
        results.Clear();
        if (gridLayout == null)
            return;

        gridLayout = ResolveCellLayout(gridLayout, brushTarget);
        var byCol = GroupByColumn(CollectCells(position));
        foreach (var kv in byCol)
        {
            foreach (var run in EnumerateRuns(kv.Value))
            {
                int minY = run.y0;
                int maxEx = run.y1 + 1;
                var spans = FindTouchingVerticalSpans(kv.Key.x, kv.Key.z, run.y0, run.y1);
                for (int i = 0; i < spans.Count; i++)
                {
                    minY = Mathf.Min(minY, spans[i].MinY);
                    maxEx = Mathf.Max(maxEx, spans[i].MaxYExclusive);
                }

                results.Add(new BoundsInt(kv.Key.x, minY, kv.Key.z, 1, maxEx - minY, 1));
            }
        }
    }

    void PaintCells(GridLayout gridLayout, GameObject brushTarget, List<Vector3Int> cells)
    {
        if (!TryResolvePaintContext(gridLayout, brushTarget, cells, out GridLayout grid, out GameObject resolvedPrefab, out Transform parent))
            return;

        Undo.IncrementCurrentGroup();
        int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Paint Laser Gate");

        AdoptExisting(grid);

        var byCol = GroupByColumn(cells);
        foreach (var kv in byCol)
        {
            foreach (var run in EnumerateRuns(kv.Value))
                EnsureVerticalRun(grid, parent, resolvedPrefab, kv.Key.x, kv.Key.z, run.y0, run.y1);
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
        Undo.SetCurrentGroupName("Erase Laser Gate");

        AdoptExisting(gridLayout);

        var eraseSet = new HashSet<Vector3Int>(cells);
        var affected = new List<LaserGateGridSpan>();
        var all = FindSceneSpans();
        for (int i = 0; i < all.Count; i++)
        {
            var span = all[i];
            if (span == null)
                continue;

            bool hit = false;
            for (int y = span.MinY; y < span.MaxYExclusive; y++)
            {
                if (eraseSet.Contains(new Vector3Int(span.MinX, y, span.Z)))
                {
                    hit = true;
                    break;
                }
            }

            if (hit)
                affected.Add(span);
        }

        var parent = ResolveGatesRoot(gridLayout, brushTarget);
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
        if (resolvedPrefab == null || resolvedPrefab.GetComponent<LaserGate>() == null)
        {
            Debug.LogWarning("[LaserGateBrush] 未找到有效的 LaserGate Prefab。");
            return false;
        }

        parent = ResolveGatesRoot(grid, brushTarget);
        return true;
    }

    void EnsureVerticalRun(GridLayout grid, Transform parent, GameObject resolvedPrefab, int x, int z, int y0, int y1Inclusive)
    {
        var spans = FindTouchingVerticalSpans(x, z, y0, y1Inclusive);
        int minY = y0;
        int maxEx = y1Inclusive + 1;
        for (int i = 0; i < spans.Count; i++)
        {
            minY = Mathf.Min(minY, spans[i].MinY);
            maxEx = Mathf.Max(maxEx, spans[i].MaxYExclusive);
        }

        LaserGateGridSpan keep = null;
        for (int i = 0; i < spans.Count; i++)
        {
            if (keep == null || spans[i].MinY < keep.MinY)
                keep = spans[i];
        }

        var origin = new Vector3Int(x, minY, z);
        int length = maxEx - minY;

        if (keep == null)
        {
            CreateInstance(grid, parent, resolvedPrefab, origin, length, defaultIsActive);
            return;
        }

        for (int i = 0; i < spans.Count; i++)
        {
            if (spans[i] == null || spans[i] == keep)
                continue;

            var from = spans[i].GetComponent<LaserGate>();
            var to = keep.GetComponent<LaserGate>();
            LaserGateRefRetarget.Replace(from, to);
            Undo.DestroyObjectImmediate(spans[i].gameObject);
        }

        ApplySpan(grid, keep, origin, length);
    }

    void EraseFromSpan(
        GridLayout grid,
        Transform parent,
        GameObject resolvedPrefab,
        LaserGateGridSpan span,
        HashSet<Vector3Int> eraseSet)
    {
        var remaining = new List<int>();
        for (int y = span.MinY; y < span.MaxYExclusive; y++)
        {
            if (!eraseSet.Contains(new Vector3Int(span.MinX, y, span.Z)))
                remaining.Add(y);
        }

        var gate = span.GetComponent<LaserGate>();
        bool active = gate != null && gate.IsActive;
        int x = span.MinX;
        int z = span.Z;

        if (remaining.Count == 0)
        {
            LaserGateRefRetarget.Replace(gate, null);
            Undo.DestroyObjectImmediate(span.gameObject);
            return;
        }

        bool first = true;
        foreach (var run in EnumerateRuns(remaining))
        {
            var origin = new Vector3Int(x, run.y0, z);
            int length = run.y1 - run.y0 + 1;

            if (first)
            {
                ApplySpan(grid, span, origin, length);
                first = false;
                continue;
            }

            if (resolvedPrefab == null)
            {
                Debug.LogWarning("[LaserGateBrush] 拆分激光门时找不到 Prefab，已跳过剩余段。");
                continue;
            }

            CreateInstance(grid, parent, resolvedPrefab, origin, length, active);
        }
    }

    LaserGateGridSpan CreateInstance(
        GridLayout grid,
        Transform parent,
        GameObject resolvedPrefab,
        Vector3Int origin,
        int length,
        bool active)
    {
        GameObject instance = parent != null
            ? PrefabUtility.InstantiatePrefab(resolvedPrefab, parent) as GameObject
            : PrefabUtility.InstantiatePrefab(resolvedPrefab) as GameObject;

        if (instance == null)
        {
            Debug.LogError("[LaserGateBrush] InstantiatePrefab 失败。");
            return null;
        }

        Undo.RegisterCreatedObjectUndo(instance, "Paint Laser Gate");

        var gate = instance.GetComponent<LaserGate>();
        if (gate != null)
            gate.ApplyEditorPaintDefaults(active);

        var span = instance.GetComponent<LaserGateGridSpan>();
        if (span == null)
            span = Undo.AddComponent<LaserGateGridSpan>(instance);

        ApplySpan(grid, span, origin, length);
        return span;
    }

    static void ApplySpan(GridLayout grid, LaserGateGridSpan span, Vector3Int origin, int length)
    {
        if (span == null)
            return;

        Undo.RecordObject(span, "Resize Laser Gate");
        Undo.RecordObject(span.transform, "Resize Laser Gate");
        Undo.RecordObject(span.gameObject, "Resize Laser Gate");

        span.SetSpan(origin, length, LaserGateGridSpan.SpanOrientation.Vertical);
        span.transform.position = SpanCenterWorld(grid, origin, length);
        span.transform.localRotation = Quaternion.identity;
        span.transform.localScale = ComputeCellScale(grid, span.transform.parent);
        span.gameObject.name = FormatName(origin, length);
        span.ApplyMergedLayout();

        var gate = span.GetComponent<LaserGate>();
        gate?.NotifyVisualLayoutChanged();

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

    static Vector3 SpanCenterWorld(GridLayout grid, Vector3Int origin, int length)
    {
        Vector3 bottom = CellCenterWorld(grid, origin);
        Vector3 top = CellCenterWorld(grid, origin + new Vector3Int(0, Mathf.Max(0, length - 1), 0));
        return (bottom + top) * 0.5f;
    }

    static string FormatName(Vector3Int origin, int length)
    {
        return $"LaserGate ({origin.x},{origin.y} x{length} V)";
    }

    GameObject ResolvePrefab()
    {
        if (prefab != null)
            return prefab;

        return AssetDatabase.LoadAssetAtPath<GameObject>(DefaultPrefabPath);
    }

    Transform ResolveGatesRoot(GridLayout gridLayout, GameObject brushTarget)
    {
        Transform rootParent = ResolveRootParent(gridLayout, brushTarget);
        if (rootParent == null)
            return null;

        Transform existing = rootParent.Find(RootName);
        if (existing != null)
            return existing;

        var go = new GameObject(RootName);
        Undo.RegisterCreatedObjectUndo(go, "Create LaserGates");
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

        var gates = Object.FindObjectsByType<LaserGate>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < gates.Length; i++)
        {
            var gate = gates[i];
            if (gate == null || EditorUtility.IsPersistent(gate) || !gate.gameObject.scene.IsValid())
                continue;
            if (gate.GetComponent<LaserGateGridSpan>() != null)
                continue;
            if (!TryInferVerticalSpan(grid, gate, out Vector3Int origin, out int length))
                continue;

            var span = Undo.AddComponent<LaserGateGridSpan>(gate.gameObject);
            Undo.RecordObject(span, "Adopt Laser Gate");
            span.SetSpan(origin, length, LaserGateGridSpan.SpanOrientation.Vertical);
            span.ApplyMergedLayout();
            EditorUtility.SetDirty(span);
        }
    }

    static bool TryInferVerticalSpan(GridLayout grid, LaserGate gate, out Vector3Int origin, out int length)
    {
        origin = default;
        length = 0;

        var box = gate.GetComponent<BoxCollider2D>();
        if (box == null)
            return false;

        Bounds bounds = box.bounds;
        Vector3Int minCell = grid.WorldToCell(new Vector3(bounds.min.x + 0.001f, bounds.min.y + 0.001f, bounds.center.z));
        Vector3Int maxCell = grid.WorldToCell(new Vector3(bounds.max.x - 0.001f, bounds.max.y - 0.001f, bounds.center.z));
        Vector3Int centerCell = grid.WorldToCell(bounds.center);
        minCell.z = maxCell.z = centerCell.z;

        origin = new Vector3Int(centerCell.x, minCell.y, centerCell.z);
        length = maxCell.y - minCell.y + 1;
        return length > 0;
    }

    static List<LaserGateGridSpan> FindTouchingVerticalSpans(int x, int z, int y0, int y1Inclusive)
    {
        var result = new List<LaserGateGridSpan>();
        var all = FindSceneSpans();
        for (int i = 0; i < all.Count; i++)
        {
            var span = all[i];
            if (span != null && span.TouchesOrOverlapsVertical(x, z, y0, y1Inclusive))
                result.Add(span);
        }

        return result;
    }

    static List<LaserGateGridSpan> FindSceneSpans()
    {
        var result = new List<LaserGateGridSpan>();
        var all = Object.FindObjectsByType<LaserGateGridSpan>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < all.Count; i++)
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

    static Dictionary<(int x, int z), List<int>> GroupByColumn(List<Vector3Int> cells)
    {
        var byCol = new Dictionary<(int x, int z), List<int>>();
        for (int i = 0; i < cells.Count; i++)
        {
            var cell = cells[i];
            var key = (cell.x, cell.z);
            if (!byCol.TryGetValue(key, out var ys))
            {
                ys = new List<int>();
                byCol[key] = ys;
            }

            if (!ys.Contains(cell.y))
                ys.Add(cell.y);
        }

        foreach (var list in byCol.Values)
            list.Sort();

        return byCol;
    }

    static IEnumerable<(int y0, int y1)> EnumerateRuns(List<int> sortedYs)
    {
        if (sortedYs == null || sortedYs.Count == 0)
            yield break;

        int start = sortedYs[0];
        int prev = sortedYs[0];
        for (int i = 1; i < sortedYs.Count; i++)
        {
            int y = sortedYs[i];
            if (y > prev + 1)
            {
                yield return (start, prev);
                start = y;
            }

            prev = y;
        }

        yield return (start, prev);
    }
}

static class LaserGateRefRetarget
{
    public static void Replace(LaserGate from, LaserGate to)
    {
        if (from == null || from == to)
            return;

        ReplaceArray<ToggleSwitch>(from, to, "laserGateTargets");
        ReplaceArray<PressurePlate>(from, to, "laserGateTargets");
    }

    static void ReplaceArray<T>(LaserGate from, LaserGate to, string propertyName)
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

            Undo.RecordObject(owner, "Retarget Laser Gate");
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

[CustomEditor(typeof(LaserGateBrush))]
public class LaserGateBrushEditor : GridBrushEditorBase
{
    static readonly Color PaintColor = new Color(1f, 0.35f, 0.35f, 1f);
    static readonly Color EraseColor = new Color(1f, 0.4f, 0.35f, 1f);
    static readonly Color MergeColor = new Color(0.35f, 0.85f, 1f, 0.95f);
    static readonly List<BoundsInt> PreviewUnions = new List<BoundsInt>();

    public override string tooltip => "绘制竖直激光门：同列相邻格合并碰撞，两端发射器、中间激光束，不写入 Tilemap。";

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
        EditorGUILayout.PropertyField(serializedObject.FindProperty("defaultIsActive"), new GUIContent("默认激活"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("rootName"), new GUIContent("父节点名"));
        serializedObject.ApplyModifiedProperties();

        var brush = (LaserGateBrush)target;
        if (brush.Prefab == null)
        {
            var fallback = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/LaserGate.prefab");
            EditorGUILayout.HelpBox(
                fallback != null
                    ? "未指定 Prefab 时将使用 Assets/Prefabs/LaserGate.prefab。"
                    : "未找到 LaserGate Prefab，请在上方指定。",
                fallback != null ? MessageType.Info : MessageType.Warning);
        }

        EditorGUILayout.HelpBox(
            "仅支持竖直激光门。同列连续格自动合并；1 格为激光束，2 格为两端发射器，3+ 格为发射器+束+发射器。不会写入 Tilemap。",
            MessageType.Info);
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

        gridLayout = LaserGateBrush.ResolveCellLayout(gridLayout, brushTarget);

        Color color = PaintColor;
        if (tool == GridBrushBase.Tool.Erase)
            color = EraseColor;
        if (executing)
            color = Color.yellow;

        DrawBounds(gridLayout, position, color);

        var brush = target as LaserGateBrush;
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
