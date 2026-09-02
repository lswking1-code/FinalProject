using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// 激光门在网格上占用的区间，供 Tile Palette 笔刷合并/拆分。
/// 多格时碰撞合并为一条，视觉按格分段：两端发射器、中间激光束。
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
[RequireComponent(typeof(LaserGate))]
public class LaserGateGridSpan : MonoBehaviour
{
    public const string TilesRootName = "Tiles";

    public enum SpanOrientation
    {
        Horizontal,
        Vertical,
    }

    enum SegmentRole
    {
        Beam,
        LeftOrBottomEmitter,
        RightOrTopEmitter,
    }

    [SerializeField] Vector3Int origin;
    [SerializeField] int length = 1;
    [SerializeField] SpanOrientation orientation = SpanOrientation.Vertical;

    [Header("分段精灵")]
    [SerializeField] Sprite beamSprite;
    [Tooltip("未指定左右/上下发射器时使用的通用发射器")]
    [SerializeField] Sprite emitterSprite;
    [SerializeField] Sprite leftEmitterSprite;
    [SerializeField] Sprite rightEmitterSprite;
    [SerializeField] Sprite bottomEmitterSprite;
    [SerializeField] Sprite topEmitterSprite;

    [Header("视觉对齐")]
    [Tooltip("将每格精灵缩放到 1x1 本地单位（与 Grid 单格对齐）")]
    [SerializeField] bool fitSpritesToCell = true;
    [SerializeField] Vector2 visualScaleMultiplier = Vector2.one;
    [Tooltip("根据 Sprite Pivot 偏移，使图案对齐格心")]
    [SerializeField] bool compensatePivot = true;
    [Tooltip("勾选时中间束跟随根物体 Animator 帧；否则使用 Beam Sprite 静态图")]
    [SerializeField] bool syncBeamFromHostAnimator;

    bool beamsVisible = true;

    public Vector3Int Origin => origin;
    public int Length => length;
    public SpanOrientation Orientation => orientation;

    public int MinX => origin.x;
    public int MaxXExclusive => orientation == SpanOrientation.Horizontal ? origin.x + length : origin.x + 1;
    public int MinY => origin.y;
    public int MaxYExclusive => orientation == SpanOrientation.Vertical ? origin.y + length : origin.y + 1;
    public int Z => origin.z;

    public void SetSpan(Vector3Int newOrigin, int newLength, SpanOrientation newOrientation)
    {
        origin = newOrigin;
        length = Mathf.Max(1, newLength);
        orientation = newOrientation;
    }

    public bool Contains(Vector3Int cell)
    {
        if (cell.z != origin.z)
            return false;

        if (orientation == SpanOrientation.Horizontal)
            return cell.y == origin.y && cell.x >= origin.x && cell.x < origin.x + length;

        return cell.x == origin.x && cell.y >= origin.y && cell.y < origin.y + length;
    }

    public bool TouchesLeft(Vector3Int cell)
    {
        return orientation == SpanOrientation.Horizontal
            && cell.y == origin.y
            && cell.z == origin.z
            && cell.x == origin.x - 1;
    }

    public bool TouchesRight(Vector3Int cell)
    {
        return orientation == SpanOrientation.Horizontal
            && cell.y == origin.y
            && cell.z == origin.z
            && cell.x == origin.x + length;
    }

    public bool TouchesBottom(Vector3Int cell)
    {
        return orientation == SpanOrientation.Vertical
            && cell.x == origin.x
            && cell.z == origin.z
            && cell.y == origin.y - 1;
    }

    public bool TouchesTop(Vector3Int cell)
    {
        return orientation == SpanOrientation.Vertical
            && cell.x == origin.x
            && cell.z == origin.z
            && cell.y == origin.y + length;
    }

    public bool TouchesOrOverlaps(int y, int z, int x0, int x1Inclusive)
    {
        if (orientation != SpanOrientation.Horizontal || origin.y != y || origin.z != z)
            return false;

        return x0 <= MaxXExclusive && x1Inclusive + 1 >= MinX;
    }

    public bool TouchesOrOverlapsVertical(int x, int z, int y0, int y1Inclusive)
    {
        if (orientation != SpanOrientation.Vertical || origin.x != x || origin.z != z)
            return false;

        return y0 <= MaxYExclusive && y1Inclusive + 1 >= MinY;
    }

    public void ApplyMergedLayout()
    {
        var box = GetComponent<BoxCollider2D>();
        if (box != null)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
                Undo.RecordObject(box, "Resize Laser Gate");
#endif
            box.size = orientation == SpanOrientation.Horizontal
                ? new Vector2(length, 1f)
                : new Vector2(1f, length);
            box.offset = Vector2.zero;
#if UNITY_EDITOR
            if (!Application.isPlaying)
                EditorUtility.SetDirty(box);
#endif
        }

        RebuildVisualTiles();
        RefreshVisualState(GetComponent<LaserGate>()?.IsActive ?? true);
    }

    public void RebuildVisualTiles()
    {
        var host = GetComponent<SpriteRenderer>();
        if (beamSprite != null && host != null)
            host.sprite = beamSprite;

        if (host != null)
            host.enabled = false;

        Transform tilesRoot = transform.Find(TilesRootName);
        tilesRoot = GetOrCreateTilesRoot();

        int existing = tilesRoot.childCount;
        for (int i = existing - 1; i >= length; i--)
            DestroyTile(tilesRoot.GetChild(i).gameObject);

        float start = -(length - 1) * 0.5f;
        for (int i = 0; i < length; i++)
        {
            Transform tile = i < tilesRoot.childCount ? tilesRoot.GetChild(i) : null;
            if (tile == null)
                tile = CreateTile(tilesRoot, i).transform;

            tile.name = $"Tile_{i}";
            Vector3 baseLocalPos = orientation == SpanOrientation.Horizontal
                ? new Vector3(start + i, 0f, 0f)
                : new Vector3(0f, start + i, 0f);

            var sr = tile.GetComponent<SpriteRenderer>();
            if (sr == null)
                continue;

            SegmentRole role = RoleForIndex(i, length);
            ApplySegmentVisual(sr, host, role);
            ApplyTileLayout(tile, sr.sprite, baseLocalPos);
        }
    }

    public void RefreshVisualState(bool active)
    {
        beamsVisible = active;

        Transform tilesRoot = transform.Find(TilesRootName);
        if (tilesRoot == null)
            return;

        for (int i = 0; i < tilesRoot.childCount; i++)
        {
            var sr = tilesRoot.GetChild(i).GetComponent<SpriteRenderer>();
            if (sr == null)
                continue;

            SegmentRole role = RoleForIndex(i, length);
            sr.enabled = role != SegmentRole.Beam || active;
        }
    }

    void LateUpdate()
    {
        if (!beamsVisible || !syncBeamFromHostAnimator)
            return;

        var host = GetComponent<SpriteRenderer>();
        Transform tilesRoot = transform.Find(TilesRootName);
        if (host == null || tilesRoot == null || host.sprite == null)
            return;

        for (int i = 0; i < tilesRoot.childCount; i++)
        {
            if (RoleForIndex(i, length) != SegmentRole.Beam)
                continue;

            var sr = tilesRoot.GetChild(i).GetComponent<SpriteRenderer>();
            if (sr == null || !sr.enabled)
                continue;

            sr.sprite = host.sprite;
            sr.color = host.color;
        }
    }

    static SegmentRole RoleForIndex(int index, int spanLength)
    {
        if (spanLength == 1)
            return SegmentRole.Beam;
        if (spanLength == 2)
            return index == 0 ? SegmentRole.LeftOrBottomEmitter : SegmentRole.RightOrTopEmitter;
        if (index == 0)
            return SegmentRole.LeftOrBottomEmitter;
        if (index == spanLength - 1)
            return SegmentRole.RightOrTopEmitter;
        return SegmentRole.Beam;
    }

    Sprite ResolveEmitterSprite(SegmentRole role)
    {
        if (orientation == SpanOrientation.Horizontal)
        {
            if (role == SegmentRole.LeftOrBottomEmitter)
            {
                if (leftEmitterSprite != null)
                    return leftEmitterSprite;
            }
            else if (role == SegmentRole.RightOrTopEmitter)
            {
                if (rightEmitterSprite != null)
                    return rightEmitterSprite;
            }
        }
        else
        {
            if (role == SegmentRole.LeftOrBottomEmitter)
            {
                if (bottomEmitterSprite != null)
                    return bottomEmitterSprite;
            }
            else if (role == SegmentRole.RightOrTopEmitter)
            {
                if (topEmitterSprite != null)
                    return topEmitterSprite;
            }
        }

        return emitterSprite;
    }

    void ApplySegmentVisual(SpriteRenderer sr, SpriteRenderer host, SegmentRole role)
    {
        if (host != null)
            CopyRendererSettings(host, sr);

        switch (role)
        {
            case SegmentRole.Beam:
                sr.sprite = beamSprite != null ? beamSprite : host != null ? host.sprite : null;
                sr.flipX = false;
                sr.flipY = false;
                sr.enabled = beamsVisible;
                break;
            case SegmentRole.LeftOrBottomEmitter:
            case SegmentRole.RightOrTopEmitter:
                sr.sprite = ResolveEmitterSprite(role);
                sr.flipX = false;
                sr.flipY = false;
                sr.enabled = true;
                break;
        }
    }

    void ApplyTileLayout(Transform tile, Sprite sprite, Vector3 baseLocalPos)
    {
        tile.localRotation = Quaternion.identity;

        if (sprite == null)
        {
            tile.localScale = Vector3.one;
            tile.localPosition = baseLocalPos;
            return;
        }

        Vector2 spriteUnitSize = new Vector2(
            sprite.rect.width / sprite.pixelsPerUnit,
            sprite.rect.height / sprite.pixelsPerUnit);

        if (fitSpritesToCell)
        {
            tile.localScale = new Vector3(
                visualScaleMultiplier.x / Mathf.Max(0.0001f, spriteUnitSize.x),
                visualScaleMultiplier.y / Mathf.Max(0.0001f, spriteUnitSize.y),
                1f);
        }
        else
        {
            tile.localScale = Vector3.one;
        }

        Vector3 pivotOffset = Vector3.zero;
        if (compensatePivot)
        {
            Vector2 scaledSize = new Vector2(
                spriteUnitSize.x * tile.localScale.x,
                spriteUnitSize.y * tile.localScale.y);
            Vector2 normPivot = new Vector2(
                sprite.pivot.x / Mathf.Max(1f, sprite.rect.width),
                sprite.pivot.y / Mathf.Max(1f, sprite.rect.height));
            pivotOffset = new Vector3(
                (0.5f - normPivot.x) * scaledSize.x,
                (0.5f - normPivot.y) * scaledSize.y,
                0f);
        }

        tile.localPosition = baseLocalPos + pivotOffset;
    }

    Transform GetOrCreateTilesRoot()
    {
        Transform existing = transform.Find(TilesRootName);
        if (existing != null)
            return existing;

        var go = new GameObject(TilesRootName);
        go.layer = gameObject.layer;
#if UNITY_EDITOR
        if (!Application.isPlaying)
            Undo.RegisterCreatedObjectUndo(go, "Create Laser Gate Tiles");
#endif
        go.transform.SetParent(transform, false);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;
        return go.transform;
    }

    GameObject CreateTile(Transform parent, int index)
    {
        var go = new GameObject($"Tile_{index}");
        go.layer = gameObject.layer;
        go.AddComponent<SpriteRenderer>();
#if UNITY_EDITOR
        if (!Application.isPlaying)
            Undo.RegisterCreatedObjectUndo(go, "Create Laser Gate Tile");
#endif
        go.transform.SetParent(parent, false);
        return go;
    }

    void DestroyTilesRoot(Transform tilesRoot)
    {
        if (tilesRoot == null)
            return;

#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            Undo.DestroyObjectImmediate(tilesRoot.gameObject);
            return;
        }
#endif
        DestroyImmediate(tilesRoot.gameObject);
    }

    void DestroyTile(GameObject tile)
    {
        if (tile == null)
            return;

#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            Undo.DestroyObjectImmediate(tile);
            return;
        }
#endif
        DestroyImmediate(tile);
    }

    static void CopyRendererSettings(SpriteRenderer src, SpriteRenderer dst)
    {
        dst.sharedMaterial = src.sharedMaterial;
        dst.sortingLayerID = src.sortingLayerID;
        dst.sortingOrder = src.sortingOrder;
        dst.drawMode = SpriteDrawMode.Simple;
        dst.maskInteraction = src.maskInteraction;
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        if (!Application.isPlaying && isActiveAndEnabled)
        {
            RebuildVisualTiles();
            RefreshVisualState(GetComponent<LaserGate>()?.IsActive ?? true);
        }
    }
#endif
}
