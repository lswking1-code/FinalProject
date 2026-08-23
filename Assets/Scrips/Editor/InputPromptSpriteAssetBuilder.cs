#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore;

/// <summary>
/// 从 Pixel Keys / gdb-xbox-2 large / ExIcons 打包 TMP Sprite Asset，供引导字混排按键图标。
/// </summary>
public static class InputPromptSpriteAssetBuilder
{
    const string OutputFolder = "Assets/TextMesh Pro/Resources/Sprite Assets";
    const string KeyAtlasPath = OutputFolder + "/KeyIcons.png";
    const string KeyAssetPath = OutputFolder + "/KeyIcons.asset";
    const string GamepadAtlasPath = OutputFolder + "/GamepadIcons.png";
    const string GamepadAssetPath = OutputFolder + "/GamepadIcons.asset";
    const string PixelKeysFolder = "Assets/Arts/Icons/Pixel Keys x16/Tiles White";
    const string ExIconsFolder = "Assets/Arts/Icons/ExIcons/Sprites_Cropped";
    const string XboxSheetPath = "Assets/Arts/Icons/gdb-xbox-2.png";

    /// <summary>
    /// gdb-xbox-2 large 静态帧（Unity 纹理坐标，原点左下）。
    /// </summary>
    static readonly Dictionary<string, RectInt> XboxLargeCrops = new()
    {
        ["xbox_x"] = new RectInt(16, 592, 16, 16),
        ["xbox_a"] = new RectInt(16, 576, 16, 16),
        ["xbox_y"] = new RectInt(16, 560, 16, 16),
        ["xbox_b"] = new RectInt(16, 544, 16, 16),
        ["select"] = new RectInt(16, 528, 16, 16),
        ["start"] = new RectInt(16, 512, 16, 16),
        ["left_stick"] = new RectInt(216, 592, 16, 16),
        ["right_stick"] = new RectInt(280, 592, 16, 16),
        ["left_trigger"] = new RectInt(336, 576, 16, 16),
        ["right_trigger"] = new RectInt(336, 560, 16, 16),
        ["left_shoulder"] = new RectInt(336, 544, 16, 16),
        ["right_shoulder"] = new RectInt(336, 528, 16, 16),
        ["dpad_up"] = new RectInt(160, 576, 16, 16),
        ["dpad_down"] = new RectInt(176, 576, 16, 16),
        ["dpad_left"] = new RectInt(160, 560, 16, 16),
        ["dpad_right"] = new RectInt(176, 560, 16, 16),
    };

    static readonly string[] GamepadSourceNames =
    {
        "Controller_Face_Buttons_Xbox_A",
        "Controller_Face_Buttons_Xbox_B",
        "Controller_Face_Buttons_Xbox_X",
        "Controller_Face_Buttons_Xbox_Y",
        "Controller_Face_Buttons_Playstation_Cross",
        "Controller_Face_Buttons_Playstation_Circle",
        "Controller_Face_Buttons_Playstation_Square",
        "Controller_Face_Buttons_Playstation_Triangle",
        "Controller_Face_Buttons_Nintendo_A",
        "Controller_Face_Buttons_Nintendo_B",
        "Controller_Face_Buttons_Nintendo_X",
        "Controller_Face_Buttons_Nintendo_Y",
        "Controller_Face_Buttons_Blank_Up",
        "Controller_Face_Buttons_Blank_Down",
        "Controller_Face_Buttons_Blank_Left",
        "Controller_Face_Buttons_Blank_Right",
        "Controller_Buttons_Left_Shoulder_LB",
        "Controller_Buttons_Right_Shoulder_RB",
        "Controller_Buttons_Left_Shoulder_L1",
        "Controller_Buttons_Right_Shoulder_R1",
        "Controller_Buttons_Left_Trigger_LT",
        "Controller_Buttons_Right_Trigger_RT",
        "Controller_Buttons_Left_Trigger_L2",
        "Controller_Buttons_Right_Trigger_R2",
        "Controller_Stick_L_Center",
        "Controller_Stick_R_Center",
        "Controller_Buttons_Start_Next_Play",
        "Controller_Buttons_Back_Previous_Menu",
    };

    static readonly Dictionary<string, string> GamepadSpriteNames = new()
    {
        ["Controller_Face_Buttons_Xbox_A"] = "xbox_a",
        ["Controller_Face_Buttons_Xbox_B"] = "xbox_b",
        ["Controller_Face_Buttons_Xbox_X"] = "xbox_x",
        ["Controller_Face_Buttons_Xbox_Y"] = "xbox_y",
        ["Controller_Face_Buttons_Playstation_Cross"] = "ps_cross",
        ["Controller_Face_Buttons_Playstation_Circle"] = "ps_circle",
        ["Controller_Face_Buttons_Playstation_Square"] = "ps_square",
        ["Controller_Face_Buttons_Playstation_Triangle"] = "ps_triangle",
        ["Controller_Face_Buttons_Nintendo_A"] = "nintendo_a",
        ["Controller_Face_Buttons_Nintendo_B"] = "nintendo_b",
        ["Controller_Face_Buttons_Nintendo_X"] = "nintendo_x",
        ["Controller_Face_Buttons_Nintendo_Y"] = "nintendo_y",
        ["Controller_Face_Buttons_Blank_Up"] = "dpad_up",
        ["Controller_Face_Buttons_Blank_Down"] = "dpad_down",
        ["Controller_Face_Buttons_Blank_Left"] = "dpad_left",
        ["Controller_Face_Buttons_Blank_Right"] = "dpad_right",
        ["Controller_Buttons_Left_Shoulder_LB"] = "left_shoulder",
        ["Controller_Buttons_Right_Shoulder_RB"] = "right_shoulder",
        ["Controller_Buttons_Left_Shoulder_L1"] = "left_shoulder_ps",
        ["Controller_Buttons_Right_Shoulder_R1"] = "right_shoulder_ps",
        ["Controller_Buttons_Left_Trigger_LT"] = "left_trigger",
        ["Controller_Buttons_Right_Trigger_RT"] = "right_trigger",
        ["Controller_Buttons_Left_Trigger_L2"] = "left_trigger_ps",
        ["Controller_Buttons_Right_Trigger_R2"] = "right_trigger_ps",
        ["Controller_Stick_L_Center"] = "left_stick",
        ["Controller_Stick_R_Center"] = "right_stick",
        ["Controller_Buttons_Start_Next_Play"] = "start",
        ["Controller_Buttons_Back_Previous_Menu"] = "select",
    };

    [InitializeOnLoadMethod]
    static void BuildIfMissing()
    {
        EditorApplication.delayCall += () =>
        {
            if (AssetDatabase.LoadAssetAtPath<TMP_SpriteAsset>(KeyAssetPath) == null
                || AssetDatabase.LoadAssetAtPath<TMP_SpriteAsset>(GamepadAssetPath) == null)
                Build();
        };
    }

    [MenuItem("Lost Division/Build Input Prompt Sprite Assets")]
    public static void Build()
    {
        Directory.CreateDirectory(ToFullPath(OutputFolder));

        var keyAsset = BuildFromFolder(
            PixelKeysFolder,
            "pxkw_",
            KeyAtlasPath,
            KeyAssetPath,
            "KeyIcons",
            StripPxkwPrefix);

        var gamepadTextures = CollectGamepadTextures();
        var gamepadAsset = BuildFromTextures(
            gamepadTextures,
            GamepadAtlasPath,
            GamepadAssetPath,
            "GamepadIcons");

        if (keyAsset != null && gamepadAsset != null)
        {
            if (keyAsset.fallbackSpriteAssets == null)
                keyAsset.fallbackSpriteAssets = new List<TMP_SpriteAsset>();
            if (!keyAsset.fallbackSpriteAssets.Contains(gamepadAsset))
                keyAsset.fallbackSpriteAssets.Add(gamepadAsset);
            EditorUtility.SetDirty(keyAsset);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("Input Prompt Sprite Assets 已生成：KeyIcons / GamepadIcons。");
    }

    static string StripPxkwPrefix(string fileName)
    {
        const string prefix = "pxkw_";
        string name = fileName;
        if (name.StartsWith(prefix))
            name = name.Substring(prefix.Length);
        return name;
    }

    static List<(string name, Texture2D texture)> CollectGamepadTextures()
    {
        var list = new List<(string, Texture2D)>();
        Texture2D xboxSheet = null;
        string xboxFullPath = ToFullPath(XboxSheetPath);
        if (File.Exists(xboxFullPath))
            xboxSheet = LoadPng(xboxFullPath);
        else
            Debug.LogWarning($"缺少 Xbox 图集：{XboxSheetPath}");

        foreach (string source in GamepadSourceNames)
        {
            string spriteName = GamepadSpriteNames.TryGetValue(source, out string mapped)
                ? mapped
                : source;

            Texture2D texture = null;
            if (xboxSheet != null && XboxLargeCrops.TryGetValue(spriteName, out RectInt crop))
            {
                texture = CropFromSheet(xboxSheet, crop);
                if (texture == null)
                    Debug.LogWarning($"无法从 {XboxSheetPath} 裁切 {spriteName} {crop}");
            }

            if (texture == null)
            {
                string path = $"{ExIconsFolder}/{source}.png";
                if (!File.Exists(ToFullPath(path)))
                {
                    Debug.LogWarning($"缺少手柄图标：{path}");
                    continue;
                }

                texture = LoadPng(ToFullPath(path));
                if (texture == null)
                {
                    Debug.LogWarning($"无法读取图标：{path}");
                    continue;
                }
            }

            list.Add((spriteName, texture));
        }

        return list;
    }

    static Texture2D CropFromSheet(Texture2D sheet, RectInt crop)
    {
        if (sheet == null
            || crop.width <= 0
            || crop.height <= 0
            || crop.x < 0
            || crop.y < 0
            || crop.x + crop.width > sheet.width
            || crop.y + crop.height > sheet.height)
            return null;

        var texture = new Texture2D(crop.width, crop.height, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
        };
        texture.SetPixels(sheet.GetPixels(crop.x, crop.y, crop.width, crop.height));
        texture.Apply(false, false);
        return texture;
    }

    static TMP_SpriteAsset BuildFromFolder(
        string folder,
        string requiredPrefix,
        string atlasPath,
        string assetPath,
        string assetName,
        System.Func<string, string> nameSelector)
    {
        var entries = new List<(string name, string path)>();
        string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { folder });
        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            if (!path.EndsWith(".png"))
                continue;

            string file = Path.GetFileNameWithoutExtension(path);
            if (!string.IsNullOrEmpty(requiredPrefix) && !file.StartsWith(requiredPrefix))
                continue;

            entries.Add((nameSelector(file), path));
        }

        entries.Sort((a, b) => string.CompareOrdinal(a.name, b.name));

        var textures = new List<(string name, Texture2D texture)>(entries.Count);
        for (int i = 0; i < entries.Count; i++)
        {
            var loaded = LoadPng(ToFullPath(entries[i].path));
            if (loaded == null)
            {
                Debug.LogError($"无法读取图标：{entries[i].path}");
                return null;
            }

            textures.Add((entries[i].name, loaded));
        }

        return BuildFromTextures(textures, atlasPath, assetPath, assetName);
    }

    static TMP_SpriteAsset BuildFromTextures(
        List<(string name, Texture2D texture)> entries,
        string atlasPath,
        string assetPath,
        string assetName)
    {
        if (entries == null || entries.Count == 0)
        {
            Debug.LogError($"没有可用于 {assetName} 的图标。");
            return null;
        }

        var textures = new Texture2D[entries.Count];
        for (int i = 0; i < entries.Count; i++)
        {
            textures[i] = entries[i].texture;
            if (textures[i] == null)
            {
                Debug.LogError($"无法读取图标：{entries[i].name}");
                return null;
            }
        }

        var atlas = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        Rect[] rects = atlas.PackTextures(textures, 2, 2048, false);
        atlas.filterMode = FilterMode.Point;
        atlas.wrapMode = TextureWrapMode.Clamp;
        atlas.Apply(false, false);

        File.WriteAllBytes(ToFullPath(atlasPath), atlas.EncodeToPNG());
        AssetDatabase.ImportAsset(atlasPath, ImportAssetOptions.ForceUpdate);
        ApplyAtlasImportSettings(atlasPath);

        var atlasTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(atlasPath);
        if (atlasTexture == null)
        {
            Debug.LogError($"图集导入失败：{atlasPath}");
            return null;
        }

        var spriteAsset = AssetDatabase.LoadAssetAtPath<TMP_SpriteAsset>(assetPath);
        if (spriteAsset == null)
        {
            spriteAsset = ScriptableObject.CreateInstance<TMP_SpriteAsset>();
            AssetDatabase.CreateAsset(spriteAsset, assetPath);
        }
        else
        {
            foreach (var sub in AssetDatabase.LoadAllAssetsAtPath(assetPath))
            {
                if (sub != null && sub != spriteAsset && sub is Material)
                    Object.DestroyImmediate(sub, true);
            }
        }

        spriteAsset.name = assetName;
        spriteAsset.hashCode = TMP_TextUtilities.GetSimpleHashCode(assetName);
        var so = new SerializedObject(spriteAsset);
        so.FindProperty("m_Version").stringValue = "1.1.0";
        so.ApplyModifiedPropertiesWithoutUndo();
        spriteAsset.spriteSheet = atlasTexture;
        spriteAsset.spriteCharacterTable.Clear();
        spriteAsset.spriteGlyphTable.Clear();

        int atlasW = atlasTexture.width;
        int atlasH = atlasTexture.height;

        for (int i = 0; i < entries.Count; i++)
        {
            Rect uv = rects[i];
            int x = Mathf.RoundToInt(uv.x * atlasW);
            int y = Mathf.RoundToInt(uv.y * atlasH);
            int w = Mathf.Max(1, Mathf.RoundToInt(uv.width * atlasW));
            int h = Mathf.Max(1, Mathf.RoundToInt(uv.height * atlasH));

            var glyph = new TMP_SpriteGlyph
            {
                index = (uint)i,
                metrics = new GlyphMetrics(w, h, 0f, h * 0.85f, w),
                glyphRect = new GlyphRect(x, y, w, h),
                scale = 1f,
                atlasIndex = 0,
            };
            spriteAsset.spriteGlyphTable.Add(glyph);

            var character = new TMP_SpriteCharacter(0, glyph)
            {
                name = entries[i].name,
                scale = 1f,
                glyphIndex = (uint)i,
            };
            spriteAsset.spriteCharacterTable.Add(character);
        }

        Shader shader = Shader.Find("TextMeshPro/Sprite");
        if (shader == null)
            shader = Shader.Find("GUI/Text Shader");

        var material = new Material(shader)
        {
            name = assetName,
        };
        material.mainTexture = atlasTexture;
        AssetDatabase.AddObjectToAsset(material, spriteAsset);
        spriteAsset.material = material;
        spriteAsset.materialHashCode = TMP_TextUtilities.GetSimpleHashCode(material.name);
        spriteAsset.UpdateLookupTables();

        EditorUtility.SetDirty(spriteAsset);
        return spriteAsset;
    }

    static void ApplyAtlasImportSettings(string assetPath)
    {
        var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
        if (importer == null)
            return;

        importer.textureType = TextureImporterType.Default;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;
        importer.filterMode = FilterMode.Point;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.npotScale = TextureImporterNPOTScale.None;
        importer.sRGBTexture = true;
        importer.maxTextureSize = 2048;
        importer.SaveAndReimport();
    }

    static Texture2D LoadPng(string fullPath)
    {
        if (!File.Exists(fullPath))
            return null;

        byte[] data = File.ReadAllBytes(fullPath);
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        return texture.LoadImage(data) ? texture : null;
    }

    static string ToFullPath(string assetPath)
    {
        string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        return Path.GetFullPath(Path.Combine(projectRoot, assetPath.Replace('/', Path.DirectorySeparatorChar)));
    }
}
#endif
