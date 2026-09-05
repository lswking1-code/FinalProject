#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Tilemaps;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// 生成激光门底座装饰 Tile / Tile Palette，可用默认笔刷单独刷进关卡。
/// </summary>
public static class LaserGateBaseTileSetup
{
    const string Folder = "Assets/Tilemaps/LaserGate";
    const string TopTilePath = Folder + "/LaserGateBase_Top.asset";
    const string BottomTilePath = Folder + "/LaserGateBase_Bottom.asset";
    const string PalettePath = Folder + "/LaserGateBasePalette.prefab";
    const string SourceSpritePath = "Assets/Arts/Tilemap/Traps/Individual_PNGs/laser_activate/tile000.png";

    [MenuItem("Lost Division/Create Laser Gate Base Tile Palette")]
    public static void CreateLaserGateBaseTilePalette()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            return;

        EnsureFolder();
        EnsureSourceSpriteImport();

        Tile top = EnsureTile(TopTilePath, "LaserGateBase_Top", "tile000_0");
        Tile bottom = EnsureTile(BottomTilePath, "LaserGateBase_Bottom", "tile000_1");
        if (top == null || bottom == null)
        {
            Debug.LogError("[LaserGateBaseTileSetup] 无法创建底座 Tile，请确认 tile000.png 已正确切片。");
            return;
        }

        CreateOrUpdatePalette(top, bottom);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Selection.activeObject = AssetDatabase.LoadAssetAtPath<Object>(PalettePath);
        EditorGUIUtility.PingObject(Selection.activeObject);
        Debug.Log("[LaserGateBaseTileSetup] 已生成底座 Tile Palette：Assets/Tilemaps/LaserGate/LaserGateBasePalette.prefab\n" +
                  "在 Tile Palette 窗口打开该 Palette，用默认笔刷刷到装饰 Tilemap（Collider=None）。");
    }

    static void EnsureFolder()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Tilemaps"))
            AssetDatabase.CreateFolder("Assets", "Tilemaps");
        if (!AssetDatabase.IsValidFolder(Folder))
            AssetDatabase.CreateFolder("Assets/Tilemaps", "LaserGate");
    }

    static void EnsureSourceSpriteImport()
    {
        var importer = AssetImporter.GetAtPath(SourceSpritePath) as TextureImporter;
        if (importer == null)
            return;

        bool dirty = false;
        if (importer.spritePixelsPerUnit != 32f)
        {
            importer.spritePixelsPerUnit = 32f;
            dirty = true;
        }

        if (importer.filterMode != FilterMode.Point)
        {
            importer.filterMode = FilterMode.Point;
            dirty = true;
        }

        if (dirty)
            importer.SaveAndReimport();
    }

    static Tile EnsureTile(string path, string name, string spriteName)
    {
        var sprites = AssetDatabase.LoadAllAssetsAtPath(SourceSpritePath);
        Sprite sprite = null;
        for (int i = 0; i < sprites.Length; i++)
        {
            if (sprites[i] is Sprite s && s.name == spriteName)
            {
                sprite = s;
                break;
            }
        }

        if (sprite == null)
            return null;

        Tile tile = AssetDatabase.LoadAssetAtPath<Tile>(path);
        if (tile == null)
        {
            tile = ScriptableObject.CreateInstance<Tile>();
            tile.name = name;
            AssetDatabase.CreateAsset(tile, path);
        }

        tile.sprite = sprite;
        tile.colliderType = Tile.ColliderType.None;
        tile.color = Color.white;
        EditorUtility.SetDirty(tile);
        return tile;
    }

    static void CreateOrUpdatePalette(Tile top, Tile bottom)
    {
        GameObject paletteRoot = AssetDatabase.LoadAssetAtPath<GameObject>(PalettePath);
        if (paletteRoot == null)
        {
            GameObject created = GridPaletteUtility.CreateNewPalette(
                Folder,
                "LaserGateBasePalette",
                GridLayout.CellLayout.Rectangle,
                GridPalette.CellSizing.Automatic,
                Vector3.one,
                GridLayout.CellSwizzle.XYZ);

            if (created == null)
            {
                Debug.LogError("[LaserGateBaseTileSetup] CreateNewPalette 失败。");
                return;
            }

            // Unity 可能把 prefab 建在子文件夹；统一挪到目标路径。
            string createdPath = AssetDatabase.GetAssetPath(created);
            if (createdPath != PalettePath)
            {
                AssetDatabase.DeleteAsset(PalettePath);
                AssetDatabase.MoveAsset(createdPath, PalettePath);
            }

            paletteRoot = AssetDatabase.LoadAssetAtPath<GameObject>(PalettePath);
        }

        if (paletteRoot == null)
            return;

        string assetPath = AssetDatabase.GetAssetPath(paletteRoot);
        var contents = PrefabUtility.LoadPrefabContents(assetPath);
        try
        {
            var tilemap = contents.GetComponentInChildren<Tilemap>(true);
            if (tilemap == null)
            {
                Debug.LogError("[LaserGateBaseTileSetup] Palette 中找不到 Tilemap。");
                return;
            }

            tilemap.ClearAllTiles();
            tilemap.SetTile(new Vector3Int(0, 0, 0), bottom);
            tilemap.SetTile(new Vector3Int(1, 0, 0), top);
            PrefabUtility.SaveAsPrefabAsset(contents, assetPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(contents);
        }
    }
}
#endif
