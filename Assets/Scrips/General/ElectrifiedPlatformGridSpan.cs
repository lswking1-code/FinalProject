using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// 通电平台在网格上占用的横向区间，供 Tile Palette 笔刷合并/拆分使用。
/// 多格时碰撞合并为一条，视觉按格复制，不拉伸。
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
[RequireComponent(typeof(ElectrifiedPlatform))]
public class ElectrifiedPlatformGridSpan : MonoBehaviour
{
    public const string TilesRootName = "Tiles";

    [SerializeField] Vector3Int origin;
    [SerializeField] int width = 1;

    public Vector3Int Origin => origin;
    public int Width => width;
    public int MinX => origin.x;
    public int MaxXExclusive => origin.x + width;
    public int Y => origin.y;
    public int Z => origin.z;

    public void SetSpan(Vector3Int newOrigin, int newWidth)
    {
        origin = newOrigin;
        width = Mathf.Max(1, newWidth);
    }

    public bool Contains(Vector3Int cell)
    {
        return cell.y == origin.y
            && cell.z == origin.z
            && cell.x >= origin.x
            && cell.x < origin.x + width;
    }

    public bool TouchesLeft(Vector3Int cell)
    {
        return cell.y == origin.y && cell.z == origin.z && cell.x == origin.x - 1;
    }

    public bool TouchesRight(Vector3Int cell)
    {
        return cell.y == origin.y && cell.z == origin.z && cell.x == origin.x + width;
    }

    public bool TouchesOrOverlaps(int y, int z, int x0, int x1Inclusive)
    {
        if (origin.y != y || origin.z != z)
            return false;

        return x0 <= MaxXExclusive && x1Inclusive + 1 >= MinX;
    }

    public void ApplyMergedLayout()
    {
        var box = GetComponent<BoxCollider2D>();
        if (box != null)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
                Undo.RecordObject(box, "Resize Electrified Platform");
#endif
            box.size = new Vector2(width, 1f);
            box.offset = Vector2.zero;
#if UNITY_EDITOR
            if (!Application.isPlaying)
                EditorUtility.SetDirty(box);
#endif
        }

        RebuildVisualTiles();
    }

    public void RebuildVisualTiles()
    {
        var host = GetComponent<SpriteRenderer>();
        Transform tilesRoot = transform.Find(TilesRootName);

        if (width <= 1)
        {
            if (host != null)
                host.enabled = true;
            DestroyTilesRoot(tilesRoot);
            return;
        }

        if (host != null)
            host.enabled = false;

        tilesRoot = GetOrCreateTilesRoot();
        int existing = tilesRoot.childCount;
        for (int i = existing - 1; i >= width; i--)
            DestroyTile(tilesRoot.GetChild(i).gameObject);

        float startX = -(width - 1) * 0.5f;
        for (int i = 0; i < width; i++)
        {
            Transform tile = i < tilesRoot.childCount ? tilesRoot.GetChild(i) : null;
            if (tile == null)
                tile = CreateTile(tilesRoot, i).transform;

            tile.name = $"Tile_{i}";
            tile.localPosition = new Vector3(startX + i, 0f, 0f);
            tile.localRotation = Quaternion.identity;
            tile.localScale = Vector3.one;

            var sr = tile.GetComponent<SpriteRenderer>();
            if (sr != null && host != null)
                CopyRenderer(host, sr);
        }
    }

    void LateUpdate()
    {
        if (width <= 1)
            return;

        var host = GetComponent<SpriteRenderer>();
        Transform tilesRoot = transform.Find(TilesRootName);
        if (host == null || tilesRoot == null)
            return;

        for (int i = 0; i < tilesRoot.childCount; i++)
        {
            var sr = tilesRoot.GetChild(i).GetComponent<SpriteRenderer>();
            if (sr != null)
            {
                sr.sprite = host.sprite;
                sr.color = host.color;
            }
        }
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
            Undo.RegisterCreatedObjectUndo(go, "Create Platform Tiles");
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
            Undo.RegisterCreatedObjectUndo(go, "Create Platform Tile");
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

    static void CopyRenderer(SpriteRenderer src, SpriteRenderer dst)
    {
        dst.sprite = src.sprite;
        dst.color = src.color;
        dst.sharedMaterial = src.sharedMaterial;
        dst.sortingLayerID = src.sortingLayerID;
        dst.sortingOrder = src.sortingOrder;
        dst.drawMode = SpriteDrawMode.Simple;
        dst.flipX = src.flipX;
        dst.flipY = src.flipY;
        dst.maskInteraction = src.maskInteraction;
    }
}
