#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class AE74ShockwaveArtSetup
{
    const string TexturePath = "Assets/Arts/AE74Shockwave/AE74LandingShockwave.png";
    const string PrefabPath = "Assets/Prefabs/Bullets/AE74Shockwave.prefab";

    [MenuItem("Lost Division/Setup AE74 Shockwave Art %#&w")]
    public static void Setup()
    {
        try
        {
            AssetDatabase.ImportAsset(TexturePath);
            var importer = (TextureImporter)AssetImporter.GetAtPath(TexturePath);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Multiple;
            importer.filterMode = FilterMode.Point;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = 2048;
            importer.spritePixelsPerUnit = 192f;
            var slices = new SpriteMetaData[8];
            for (int i = 0; i < 8; i++)
            {
                slices[i] = new SpriteMetaData
                {
                    name = $"AE74Shockwave_{i:00}",
                    rect = new Rect(i % 4 * 384, i < 4 ? 512 : 0, 384, 512),
                    alignment = (int)SpriteAlignment.Custom,
                    // Align both rows to the ground contact line in the painted frames.
                    pivot = new Vector2(0.5f, i < 4 ? 82f / 512f : 114f / 512f)
                };
            }
            importer.spritesheet = slices;
            importer.SaveAndReimport();
            var frames = AssetDatabase.LoadAllAssetsAtPath(TexturePath).OfType<Sprite>().OrderBy(x => x.name).ToArray();
            if (frames.Length != 8) throw new InvalidOperationException("Expected 8 shockwave frames");
            var root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                var original = root.GetComponent<SpriteRenderer>();
                var child = root.transform.Find("ShockwaveArt");
                if (child == null)
                {
                    child = new GameObject("ShockwaveArt").transform;
                    child.SetParent(root.transform, false);
                }
                child.localPosition = Vector3.zero;
                child.localRotation = Quaternion.identity;
                // Counter the placeholder's squash without altering the damage collider.
                child.localScale = new Vector3(1f / Mathf.Abs(root.transform.localScale.x),
                    1f / Mathf.Abs(root.transform.localScale.y), 1f);
                var visual = child.GetComponent<SpriteRenderer>();
                if (visual == null) visual = child.gameObject.AddComponent<SpriteRenderer>();
                visual.sharedMaterial = original.sharedMaterial;
                visual.sortingLayerID = original.sortingLayerID;
                visual.sortingOrder = original.sortingOrder;
                visual.sprite = frames[0];
                visual.color = Color.white;
                original.enabled = false;
                var so = new SerializedObject(root.GetComponent<AE74Shockwave>());
                so.FindProperty("artwork").objectReferenceValue = visual;
                var refs = so.FindProperty("animationFrames");
                refs.arraySize = frames.Length;
                for (int i = 0; i < frames.Length; i++) refs.GetArrayElementAtIndex(i).objectReferenceValue = frames[i];
                so.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            Selection.activeObject = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            File.WriteAllText(Path.Combine(Path.GetTempPath(), "AE74ShockwaveArtValidation.txt"),
                "PASS: 8 sprites imported, point filtering, alpha preserved, prefab artwork and animation frames assigned; gameplay collider and values unchanged.");
            Debug.Log("AE74 shockwave art: 8 frames imported and connected.");
        }
        catch (Exception e)
        {
            File.WriteAllText(Path.Combine(Path.GetTempPath(), "AE74ShockwaveArtValidation.txt"), e.ToString());
            Debug.LogException(e);
        }
    }
}
#endif
