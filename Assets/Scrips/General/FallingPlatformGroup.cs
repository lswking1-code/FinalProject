using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// 将整段母体平台在编辑器中切分为多个独立掉落平台片。
/// 运行时不自动拆分；请在 Inspector 中 Bake。
/// </summary>
[DisallowMultipleComponent]
public class FallingPlatformGroup : MonoBehaviour
{
    public const string PiecesRootName = "Pieces";

    [Header("片 Prefab")]
    [Tooltip("单块掉落平台 Prefab，须含 FallingPlatform 与碰撞体/Sprite 尺寸")]
    [SerializeField] GameObject piecePrefab;

    [Header("Bake 选项")]
    [Tooltip("Bake 后隐藏母体 SpriteRenderer（仅本物体组件）")]
    [SerializeField] bool hideHostVisualOnBake = true;

    [Header("覆盖片参数（可选）")]
    [SerializeField] bool overrideFallSettings;
    [SerializeField] float fallDelay = 1f;
    [SerializeField] string destroyStateName = "Destroy";
    [SerializeField] float fallbackDestroyDelay = 0.5f;

    public GameObject PiecePrefab => piecePrefab;
    public bool HideHostVisualOnBake => hideHostVisualOnBake;
    public bool OverrideFallSettings => overrideFallSettings;

    public bool TryGetTotalWidth(out float width)
    {
        width = 0f;
        if (TryGetHostExtentWorld(out float sizeX, out _))
        {
            width = sizeX;
            return width > 0.0001f;
        }

        return false;
    }

    public bool TryGetPieceWidth(out float width)
    {
        width = 0f;
#if UNITY_EDITOR
        if (piecePrefab == null)
            return false;

        width = MeasurePrefabWorldWidth(piecePrefab);
        return width > 0.0001f;
#else
        return false;
#endif
    }

    public int GetPieceCount()
    {
        if (!TryGetTotalWidth(out float total) || !TryGetPieceWidth(out float piece) || piece <= 0.0001f)
            return 0;

        return Mathf.Max(1, Mathf.RoundToInt(total / piece));
    }

    public bool HasPieces()
    {
        Transform root = transform.Find(PiecesRootName);
        return root != null && root.childCount > 0;
    }

#if UNITY_EDITOR
    public void Bake()
    {
        if (piecePrefab == null)
        {
            Debug.LogWarning("[FallingPlatformGroup] 未指定 piecePrefab。", this);
            return;
        }

        if (piecePrefab.GetComponentInChildren<FallingPlatform>(true) == null)
        {
            Debug.LogWarning("[FallingPlatformGroup] piecePrefab 上找不到 FallingPlatform。", this);
            return;
        }

        if (!TryGetTotalWidth(out float totalWidth) || totalWidth <= 0.0001f)
        {
            Debug.LogWarning("[FallingPlatformGroup] 无法测量母体总宽（需要 BoxCollider2D / Collider2D / SpriteRenderer）。", this);
            return;
        }

        float pieceWidth = MeasurePrefabWorldWidth(piecePrefab);
        if (pieceWidth <= 0.0001f)
        {
            Debug.LogWarning("[FallingPlatformGroup] 无法测量片 Prefab 世界宽度。", this);
            return;
        }

        int count = Mathf.Max(1, Mathf.RoundToInt(totalWidth / pieceWidth));
        float segmentWidth = totalWidth / count;

        Undo.RegisterFullObjectHierarchyUndo(gameObject, "Bake Falling Platform Pieces");

        ClearPiecesInternal(restoreHost: false);

        Transform piecesRoot = GetOrCreatePiecesRoot();
        TryGetHostExtentWorld(out _, out Vector3 hostCenter);

        Vector3 right = transform.right;
        float half = totalWidth * 0.5f;

        for (int i = 0; i < count; i++)
        {
            float offset = -half + segmentWidth * (i + 0.5f);
            Vector3 worldPos = hostCenter + right * offset;

            GameObject instance = PrefabUtility.InstantiatePrefab(piecePrefab, piecesRoot) as GameObject;
            if (instance == null)
                instance = (GameObject)PrefabUtility.InstantiatePrefab(piecePrefab);

            if (instance == null)
            {
                Debug.LogError("[FallingPlatformGroup] InstantiatePrefab 失败。", this);
                continue;
            }

            Undo.RegisterCreatedObjectUndo(instance, "Bake Falling Platform Piece");
            instance.name = $"Piece_{i}";
            instance.transform.SetParent(piecesRoot, true);
            instance.transform.position = worldPos;
            instance.transform.rotation = transform.rotation;

            FitPieceWorldWidth(instance, segmentWidth);

            if (overrideFallSettings)
            {
                var platforms = instance.GetComponentsInChildren<FallingPlatform>(true);
                for (int p = 0; p < platforms.Length; p++)
                {
                    platforms[p].ApplyEditorSettings(fallDelay, destroyStateName, fallbackDestroyDelay);
                    EditorUtility.SetDirty(platforms[p]);
                }
            }

            EditorUtility.SetDirty(instance);
        }

        SetHostPresentationEnabled(enabled: false);
        EditorUtility.SetDirty(this);
    }

    public void ClearPieces()
    {
        Undo.RegisterFullObjectHierarchyUndo(gameObject, "Clear Falling Platform Pieces");
        ClearPiecesInternal(restoreHost: true);
        EditorUtility.SetDirty(this);
    }

    void ClearPiecesInternal(bool restoreHost)
    {
        Transform piecesRoot = transform.Find(PiecesRootName);
        if (piecesRoot != null)
        {
            for (int i = piecesRoot.childCount - 1; i >= 0; i--)
                Undo.DestroyObjectImmediate(piecesRoot.GetChild(i).gameObject);

            Undo.DestroyObjectImmediate(piecesRoot.gameObject);
        }

        if (restoreHost)
            SetHostPresentationEnabled(enabled: true);
    }

    Transform GetOrCreatePiecesRoot()
    {
        Transform existing = transform.Find(PiecesRootName);
        if (existing != null)
            return existing;

        var go = new GameObject(PiecesRootName);
        Undo.RegisterCreatedObjectUndo(go, "Create Pieces Root");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;
        return go.transform;
    }

    void SetHostPresentationEnabled(bool enabled)
    {
        var colliders = GetComponents<Collider2D>();
        for (int i = 0; i < colliders.Length; i++)
        {
            if (colliders[i] != null)
                colliders[i].enabled = enabled;
        }

        var effectors = GetComponents<PlatformEffector2D>();
        for (int i = 0; i < effectors.Length; i++)
        {
            if (effectors[i] != null)
                effectors[i].enabled = enabled;
        }

        // 恢复时打开母体渲染；禁用仅在 hideHostVisualOnBake 时
        if (!enabled && !hideHostVisualOnBake)
            return;

        var renderers = GetComponents<SpriteRenderer>();
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] != null)
                renderers[i].enabled = enabled;
        }
    }

    static float MeasurePrefabWorldWidth(GameObject prefab)
    {
        if (prefab == null)
            return 0f;

        string path = AssetDatabase.GetAssetPath(prefab);
        if (string.IsNullOrEmpty(path))
            return MeasureObjectWorldWidth(prefab);

        GameObject contents = PrefabUtility.LoadPrefabContents(path);
        try
        {
            return MeasureObjectWorldWidth(contents);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(contents);
        }
    }

    static float MeasureObjectWorldWidth(GameObject go)
    {
        if (go == null)
            return 0f;

        var box = go.GetComponentInChildren<BoxCollider2D>(true);
        if (box != null)
        {
            float w = box.size.x * Mathf.Abs(box.transform.lossyScale.x);
            if (w > 0.0001f)
                return w;
        }

        var col = go.GetComponentInChildren<Collider2D>(true);
        if (col != null)
        {
            float w = col.bounds.size.x;
            if (w > 0.0001f)
                return w;
        }

        var sr = go.GetComponentInChildren<SpriteRenderer>(true);
        if (sr != null && sr.sprite != null)
        {
            float w = sr.sprite.bounds.size.x * Mathf.Abs(sr.transform.lossyScale.x);
            if (w > 0.0001f)
                return w;
        }

        return 0f;
    }

    static void FitPieceWorldWidth(GameObject piece, float targetWorldWidth)
    {
        if (piece == null || targetWorldWidth <= 0.0001f)
            return;

        float current = MeasureObjectWorldWidth(piece);
        if (current <= 0.0001f)
            return;

        float factor = targetWorldWidth / current;
        Vector3 scale = piece.transform.localScale;
        scale.x *= factor;
        piece.transform.localScale = scale;
    }
#endif

    bool TryGetHostExtentWorld(out float sizeX, out Vector3 center)
    {
        sizeX = 0f;
        center = transform.position;

        var box = GetComponent<BoxCollider2D>();
        if (box != null)
        {
            // 沿 transform.right 的有效长度（支持旋转母体）
            sizeX = box.size.x * Mathf.Abs(transform.lossyScale.x);
            center = box.bounds.center;
            return sizeX > 0.0001f;
        }

        var col = GetComponent<Collider2D>();
        if (col != null)
        {
            sizeX = col.bounds.size.x;
            center = col.bounds.center;
            return sizeX > 0.0001f;
        }

        var sr = GetComponent<SpriteRenderer>();
        if (sr != null && sr.sprite != null)
        {
            sizeX = sr.sprite.bounds.size.x * Mathf.Abs(transform.lossyScale.x);
            center = sr.bounds.center;
            return sizeX > 0.0001f;
        }

        return false;
    }
}
