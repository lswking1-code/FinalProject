using TMPro;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Unity 6 Inspector 重绘 FontAsset 时，会把 TMP 动态图集（HideAndDontSave）当持久化对象保存，
/// 触发 kDontSaveInEditor 断言。选中/播放时清掉 DontSaveInEditor，只保留运行时 HideFlags。
/// </summary>
[InitializeOnLoad]
static class TmpAtlasDontSaveInspectorFix
{
    const HideFlags PersistMask = HideFlags.DontSaveInEditor | HideFlags.HideAndDontSave;
    static double lastStripTime;

    static TmpAtlasDontSaveInspectorFix()
    {
        Selection.selectionChanged += StripSelected;
        EditorApplication.playModeStateChanged += _ => StripLoaded();
        EditorApplication.update += OnEditorUpdate;
        EditorApplication.delayCall += StripLoaded;
    }

    static void OnEditorUpdate()
    {
        if (!EditorApplication.isPlaying)
            return;
        if (EditorApplication.timeSinceStartup - lastStripTime < 0.25)
            return;
        lastStripTime = EditorApplication.timeSinceStartup;
        StripLoaded();
    }

    static void StripSelected()
    {
        Object[] selected = Selection.objects;
        for (int i = 0; i < selected.Length; i++)
        {
            if (selected[i] is TMP_FontAsset font)
                StripFont(font);
        }
    }

    static void StripLoaded()
    {
        TMP_FontAsset[] fonts = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
        for (int i = 0; i < fonts.Length; i++)
            StripFont(fonts[i]);
    }

    static void StripFont(TMP_FontAsset font)
    {
        if (font == null)
            return;

        Texture2D[] textures = font.atlasTextures;
        if (textures != null)
        {
            for (int i = 0; i < textures.Length; i++)
                ClearPersistFlag(textures[i]);
        }

        ClearPersistFlag(font.material);
        if (font.material != null)
            ClearPersistFlag(font.material.mainTexture);
    }

    static void ClearPersistFlag(Object obj)
    {
        if (obj == null)
            return;
        if ((obj.hideFlags & PersistMask) == 0)
            return;

        obj.hideFlags &= ~HideFlags.DontSaveInEditor;
        obj.hideFlags &= ~HideFlags.HideAndDontSave;
    }
}
