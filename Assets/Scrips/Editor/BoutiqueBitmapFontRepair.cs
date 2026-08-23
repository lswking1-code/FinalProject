#if UNITY_EDITOR
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 仅把 TTF 放回工程不会重烘焙图集。当前 BoutiqueBitmap SDF 图集已满且像素为空，
/// 必须清空后从源字体重新写入项目用字。
/// </summary>
public static class BoutiqueBitmapFontRepair
{
    const string FontAssetPath = "Assets/TextMesh Pro/Fonts/BoutiqueBitmap9x9_1.93 SDF.asset";
    const string TtfGuid = "7c5d524538f996a45815f23de32d8c80";
    const string TmpSettingsPath = "Assets/TextMesh Pro/Resources/TMP Settings.asset";
    const string PrefsKey = "LostDivision.BoutiqueBitmapFont.Repaired.v1";

    [InitializeOnLoadMethod]
    static void AutoRepair()
    {
        EditorApplication.delayCall += () =>
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return;
            if (EditorPrefs.GetBool(PrefsKey, false))
                return;
            if (Repair(logAlways: false))
                EditorPrefs.SetBool(PrefsKey, true);
        };
    }

    [MenuItem("Lost Division/Rebuild BoutiqueBitmap TMP Font")]
    static void MenuRebuild()
    {
        EditorPrefs.DeleteKey(PrefsKey);
        if (Repair(logAlways: true))
            EditorPrefs.SetBool(PrefsKey, true);
    }

    public static bool Repair(bool logAlways)
    {
        string ttfPath = AssetDatabase.GUIDToAssetPath(TtfGuid);
        if (string.IsNullOrEmpty(ttfPath))
            ttfPath = "Assets/TextMesh Pro/Fonts/BoutiqueBitmap9x9_1.93.ttf";

        var source = AssetDatabase.LoadAssetAtPath<Font>(ttfPath);
        var fontAsset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath);
        if (source == null)
        {
            if (logAlways)
                Debug.LogError("找不到 BoutiqueBitmap9x9_1.93.ttf。请放到 Assets/TextMesh Pro/Fonts/ 并保持原 meta。");
            return false;
        }

        if (fontAsset == null)
        {
            if (logAlways)
                Debug.LogError("找不到 TMP Font Asset：" + FontAssetPath);
            return false;
        }

        DisableClearDynamicDataOnBuild();

        var so = new SerializedObject(fontAsset);
        so.FindProperty("m_SourceFontFile").objectReferenceValue = source;
        so.FindProperty("m_SourceFontFileGUID").stringValue = TtfGuid;
        so.FindProperty("m_AtlasPopulationMode").intValue = (int)AtlasPopulationMode.Dynamic;
        so.FindProperty("m_IsMultiAtlasTexturesEnabled").boolValue = true;
        so.FindProperty("m_ClearDynamicDataOnBuild").boolValue = false;
        so.FindProperty("m_AtlasWidth").intValue = 2048;
        so.FindProperty("m_AtlasHeight").intValue = 2048;
        so.ApplyModifiedPropertiesWithoutUndo();

        fontAsset.isMultiAtlasTexturesEnabled = true;
        fontAsset.ClearFontAssetData(true);

        if (fontAsset.material != null)
        {
            fontAsset.material.SetFloat(ShaderUtilities.ID_TextureWidth, 2048);
            fontAsset.material.SetFloat(ShaderUtilities.ID_TextureHeight, 2048);
            EditorUtility.SetDirty(fontAsset.material);
        }

        string characters = CollectProjectCharacters();
        bool allAdded = fontAsset.TryAddCharacters(characters, out string missing);

        EditorUtility.SetDirty(fontAsset);
        AssetDatabase.SaveAssets();
        TMPro_EventManager.ON_FONT_PROPERTY_CHANGED(true, fontAsset);

        int baked = fontAsset.characterTable != null ? fontAsset.characterTable.Count : 0;
        if (!allAdded && !string.IsNullOrEmpty(missing))
        {
            Debug.LogWarning(
                $"BoutiqueBitmap 已重建 {baked} 个字符，但源 TTF 不含：{Truncate(missing, 80)}");
        }
        else if (logAlways || baked > 0)
        {
            Debug.Log($"BoutiqueBitmap TMP 字体已重建，写入 {baked} 个字符。请回到场景查看引导文字。");
        }

        return baked > 0;
    }

    static void DisableClearDynamicDataOnBuild()
    {
        var settings = AssetDatabase.LoadAssetAtPath<TMP_Settings>(TmpSettingsPath);
        if (settings == null)
            return;

        var so = new SerializedObject(settings);
        var prop = so.FindProperty("m_ClearDynamicDataOnBuild");
        if (prop == null || !prop.boolValue)
            return;

        prop.boolValue = false;
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(settings);
    }

    static string CollectProjectCharacters()
    {
        var set = new HashSet<char>();
        for (char c = (char)32; c <= 126; c++)
            set.Add(c);

        foreach (char c in "，。！？、：；（）【】《》…—·“”‘’　＋－＝")
            set.Add(c);

        string[] roots =
        {
            "Assets/Scenes",
            "Assets/Scrips",
            "Assets/Prefabs",
            "Assets/Resources"
        };

        foreach (string root in roots)
        {
            if (!Directory.Exists(root))
                continue;

            foreach (string path in Directory.GetFiles(root, "*.*", SearchOption.AllDirectories))
            {
                string ext = Path.GetExtension(path).ToLowerInvariant();
                if (ext != ".unity" && ext != ".prefab" && ext != ".cs" && ext != ".txt" && ext != ".json")
                    continue;

                string text;
                try
                {
                    text = File.ReadAllText(path);
                }
                catch
                {
                    continue;
                }

                text = DecodeUnityEscapes(text);
                foreach (char c in text)
                {
                    if (IsKeptCharacter(c))
                        set.Add(c);
                }
            }
        }

        var sb = new StringBuilder(set.Count);
        foreach (char c in set)
            sb.Append(c);
        return sb.ToString();
    }

    static bool IsKeptCharacter(char c)
    {
        if (c >= 0x4E00 && c <= 0x9FFF)
            return true;
        if (c >= 0x3000 && c <= 0x303F)
            return true;
        if (c >= 0xFF00 && c <= 0xFFEF)
            return true;
        return false;
    }

    static string DecodeUnityEscapes(string text)
    {
        var sb = new StringBuilder(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\\' && i + 5 < text.Length && (text[i + 1] == 'u' || text[i + 1] == 'U')
                && int.TryParse(text.Substring(i + 2, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int code))
            {
                sb.Append((char)code);
                i += 5;
                continue;
            }

            sb.Append(text[i]);
        }

        return sb.ToString();
    }

    static string Truncate(string value, int max)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= max)
            return value;
        return value.Substring(0, max) + "…";
    }
}

class BoutiqueBitmapFontTtfImportHook : AssetPostprocessor
{
    static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] movedTo, string[] movedFrom)
    {
        for (int i = 0; i < imported.Length; i++)
        {
            if (!imported[i].Replace('\\', '/').EndsWith("BoutiqueBitmap9x9_1.93.ttf"))
                continue;

            EditorPrefs.DeleteKey("LostDivision.BoutiqueBitmapFont.Repaired.v1");
            EditorApplication.delayCall += () =>
            {
                if (BoutiqueBitmapFontRepair.Repair(logAlways: true))
                    EditorPrefs.SetBool("LostDivision.BoutiqueBitmapFont.Repaired.v1", true);
            };
            return;
        }
    }
}
#endif
