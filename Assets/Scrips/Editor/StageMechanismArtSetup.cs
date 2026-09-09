#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Installs and verifies the Stage1/Stage2 mechanism sprite set.</summary>
public static class StageMechanismArtSetup
{
    const string ArtDirectory = "Assets/Arts/StageMechanisms/";
    const string ArtChild = "MechanismArt";
    // 32px source sprites displayed at 1.5 world units, without scaling colliders.
    const float ArtPixelsPerUnit = 32f / 1.5f;
    const float DoorLampWorldSize = (32f / ArtPixelsPerUnit) * 0.75f;
    static string ReportDirectory => Path.GetFullPath(Path.Combine(Application.dataPath, "../Temp/StageMechanismArt"));
    static readonly string[] ChargeStates = { "Dormant", "Full", "Half", "Low" };
    static readonly string[] CoreStates = { "Intact", "Cracked", "Critical", "Broken" };
    static readonly string[] SwitchStates = { "Off", "On" };

    [MenuItem("Lost Division/Art/Preview Stage2 Mechanism Art")]
    public static void PreviewStage2()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        const string path = "Assets/Scenes/Stage2.unity";
        Scene scene = SceneManager.GetSceneByPath(path);
        if (!scene.IsValid() || !scene.isLoaded) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
        MechanismVisual target = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<MechanismVisual>(true)).First(v => v.name == "EnergyNode1");
        Selection.activeGameObject = target.Artwork.gameObject;
        SceneView view = SceneView.lastActiveSceneView;
        if (view != null)
        {
            view.in2DMode = true;
            view.drawGizmos = false;
            view.LookAt(target.transform.position, Quaternion.identity, 3f, true, true);
            view.Repaint();
        }
    }

    [MenuItem("Lost Division/Art/Apply Stage1 and Stage2 Mechanism Art")]
    public static void Apply()
    {
        if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;
        SceneSetup[] previous = EditorSceneManager.GetSceneManagerSetup();
        try { ApplyInternal(); }
        finally
        {
            if (!Application.isBatchMode && previous.Length > 0)
                EditorSceneManager.RestoreSceneManagerSetup(previous);
        }
    }

    public static void ApplyAndVerifyBatch()
    {
        try
        {
            ApplyInternal();
            ValidateTransitions();
            File.WriteAllText(Path.Combine(ReportDirectory, "mechanism-unity-validation.txt"), "PASS: imports, 18 scene mechanisms, pixel scale and visual state selection (including held charge and damage flash).\n");
            Debug.Log("MECHANISM_ART_VALIDATION_PASS");
            EditorApplication.Exit(0);
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            EditorApplication.Exit(1);
        }
    }

    static void ApplyInternal()
    {
        Directory.CreateDirectory(ReportDirectory);
        foreach (string stage in new[] { "Stage1", "Stage2" })
        {
            Scene open = SceneManager.GetSceneByPath("Assets/Scenes/" + stage + ".unity");
            Require(!open.IsValid() || !open.isDirty, stage + " has unsaved user edits; art setup did not overwrite them.");
        }
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        foreach (string file in Directory.GetFiles(ArtDirectory, "*.png"))
        {
            string path = file.Replace('\\', '/');
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            bool isDoorLamp = Path.GetFileNameWithoutExtension(path).StartsWith("ChargeDoorLamp_", StringComparison.Ordinal);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            // Door lamps are three quarters of the mechanism body's width.
            importer.spritePixelsPerUnit = isDoorLamp ? 1254f / DoorLampWorldSize : ArtPixelsPerUnit;
            importer.filterMode = FilterMode.Point;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.maxTextureSize = isDoorLamp ? 2048 : 256;
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteMeshType = SpriteMeshType.FullRect;
            settings.spriteAlignment = (int)SpriteAlignment.Center;
            importer.SetTextureSettings(settings);
            importer.SaveAndReimport();
        }

        foreach (string name in new[] { "EnergyNode", "TimedChargeNode", "ToggleSwitch" })
        {
            string path = "Assets/Prefabs/" + name + ".prefab";
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                Configure(root, name);
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        var report = new List<string>();
        int total = 0;
        foreach (string stage in new[] { "Stage1", "Stage2" })
        {
            string scenePath = "Assets/Scenes/" + stage + ".unity";
            Scene scene = SceneManager.GetSceneByPath(scenePath);
            bool wasLoaded = scene.IsValid() && scene.isLoaded;
            if (!wasLoaded) scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
            int count = 0;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (EnergyNode node in root.GetComponentsInChildren<EnergyNode>(true)) { Configure(node.gameObject, "EnergyNode"); count++; }
                foreach (TimedChargeNode node in root.GetComponentsInChildren<TimedChargeNode>(true)) { Configure(node.gameObject, "TimedChargeNode"); count++; }
                foreach (ToggleSwitch node in root.GetComponentsInChildren<ToggleSwitch>(true)) { Configure(node.gameObject, "ToggleSwitch"); count++; }
                foreach (BreakableProp node in root.GetComponentsInChildren<BreakableProp>(true))
                {
                    if (!new SerializedObject(node).FindProperty("isCore").boolValue) continue;
                    Configure(node.gameObject, "DestructionNode"); count++;
                }
            }
            Require(count == (stage == "Stage1" ? 3 : 15), stage + " unexpected mechanism count: " + count);
            foreach (MechanismVisual visual in scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<MechanismVisual>(true)))
            {
                ValidateVisual(visual);
                report.Add(stage + " / " + HierarchyPath(visual.transform) + " / " + visual.Artwork.sprite.name + " / 1.5x artwork / world scale " + visual.Artwork.transform.lossyScale);
            }
            EditorSceneManager.MarkSceneDirty(scene);
            Require(EditorSceneManager.SaveScene(scene), "Failed to save " + stage);
            if (!wasLoaded) EditorSceneManager.CloseScene(scene, true);
            total += count;
        }
        Require(total == 18, "Expected 18 mechanisms");
        AssetDatabase.SaveAssets();
        File.WriteAllLines(Path.Combine(ReportDirectory, "mechanism-scene-inventory.txt"), report);
    }

    static string HierarchyPath(Transform t) => t.parent == null ? t.name : HierarchyPath(t.parent) + "/" + t.name;

    static void Configure(GameObject root, string kind)
    {
        Transform child = root.transform.Find(ArtChild);
        if (child == null)
        {
            child = new GameObject(ArtChild).transform;
            child.SetParent(root.transform, false);
        }
        child.gameObject.layer = root.layer;
        child.localPosition = kind == "ToggleSwitch" ? new Vector3(0, 0.25f, 0) : Vector3.zero;
        child.localRotation = Quaternion.identity;
        Vector3 scale = root.transform.lossyScale;
        child.localScale = new Vector3(1f / Mathf.Max(Mathf.Abs(scale.x), 0.0001f), 1f / Mathf.Max(Mathf.Abs(scale.y), 0.0001f), 1);
        SpriteRenderer art = child.GetComponent<SpriteRenderer>();
        if (art == null) art = child.gameObject.AddComponent<SpriteRenderer>();
        // Reuse the scene's established sprite material, preserving its lighting model.
        foreach (SpriteRenderer old in root.GetComponentsInChildren<SpriteRenderer>(true))
        {
            if (old == art) continue;
            if (old.sharedMaterial != null) art.sharedMaterial = old.sharedMaterial;
            old.enabled = false;
            EditorUtility.SetDirty(old);
            if (PrefabUtility.IsPartOfPrefabInstance(old)) PrefabUtility.RecordPrefabInstancePropertyModifications(old);
        }
        art.color = Color.white;
        art.enabled = true;
        art.sortingLayerName = "Ground";
        art.sortingOrder = 10;
        string[] states = kind == "DestructionNode" ? CoreStates : kind == "ToggleSwitch" ? SwitchStates : ChargeStates;
        Sprite[] sprites = states.Select(state => AssetDatabase.LoadAssetAtPath<Sprite>(ArtDirectory + kind + "_" + state + ".png")).ToArray();
        Require(sprites.All(s => s != null), "Missing sprites: " + kind);
        MechanismVisual visual = root.GetComponent<MechanismVisual>();
        if (visual == null) visual = root.AddComponent<MechanismVisual>();
        visual.Configure(art, sprites);
        EditorUtility.SetDirty(art);
        EditorUtility.SetDirty(visual);
        if (PrefabUtility.IsPartOfPrefabInstance(art)) PrefabUtility.RecordPrefabInstancePropertyModifications(art);
        if (PrefabUtility.IsPartOfPrefabInstance(visual)) PrefabUtility.RecordPrefabInstancePropertyModifications(visual);
        if (PrefabUtility.IsPartOfPrefabInstance(child)) PrefabUtility.RecordPrefabInstancePropertyModifications(child);
    }

    static void ValidateVisual(MechanismVisual visual)
    {
        Require(visual.Artwork != null && visual.Artwork.enabled, visual.name + " missing artwork");
        Require(visual.StateSprites.All(s => s != null && Mathf.Approximately(s.pixelsPerUnit, ArtPixelsPerUnit)), visual.name + " wrong PPU");
        Vector3 scale = visual.Artwork.transform.lossyScale;
        Require(Mathf.Approximately(Mathf.Abs(scale.x), 1) && Mathf.Approximately(Mathf.Abs(scale.y), 1), visual.name + " stretched pixels");
        Require(visual.GetComponentsInChildren<SpriteRenderer>(true).Count(r => r.enabled) == 1, visual.name + " visible placeholder");
    }

    static void SetField(object target, string name, object value)
    {
        FieldInfo field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        Require(field != null, "Missing state field " + name);
        field.SetValue(target, value);
    }

    static void ValidateTransitions()
    {
        foreach (string kind in new[] { "EnergyNode", "TimedChargeNode", "ToggleSwitch" })
        {
            GameObject root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/" + kind + ".prefab"));
            try
            {
                MechanismVisual visual = root.GetComponent<MechanismVisual>();
                Action<int> check = expected => { visual.RefreshVisual(); Require(visual.Artwork.sprite == visual.StateSprites[expected], kind + " incorrect state " + expected); };
                check(0);
                if (kind == "ToggleSwitch")
                {
                    ToggleSwitch node = root.GetComponent<ToggleSwitch>();
                    node.SetOn(true); check(1); node.SetOn(false); check(0);
                }
                else if (kind == "EnergyNode")
                {
                    EnergyNode node = root.GetComponent<EnergyNode>();
                    node.Charge(); check(1);
                    SetField(node, "remain", 5f); check(2);
                    SetField(node, "remain", 2f); check(3);
                    node.HoldCharged(); check(1);
                    SetField(node, "held", false); SetField(node, "isCharged", false); check(0);
                }
                else
                {
                    TimedChargeNode node = root.GetComponent<TimedChargeNode>();
                    SetField(node, "isActive", true); SetField(node, "remain", 10f); check(1);
                    SetField(node, "remain", 5f); check(2);
                    SetField(node, "remain", 2f); check(3);
                    SetField(node, "isActive", false); check(0);
                }
            }
            finally { Object.DestroyImmediate(root); }
        }
        GameObject coreRoot = new GameObject("CoreArtValidation");
        try
        {
            BreakableProp core = coreRoot.AddComponent<BreakableProp>();
            SetField(core, "hitsToBreak", 20);
            Configure(coreRoot, "DestructionNode");
            var visual = coreRoot.GetComponent<MechanismVisual>();
            foreach (int hits in new[] { 0, 1, 10, 19 })
            {
                SetField(core, "currentHits", hits); visual.RefreshVisual();
                int expected = hits == 0 ? 0 : hits < 10 ? 1 : 2;
                Require(visual.Artwork.sprite == visual.StateSprites[expected], "Core damage progression");
            }
            SetField(core, "isBroken", true); visual.RefreshVisual();
            Require(visual.Artwork.sprite == visual.StateSprites[3], "Core broken state");
            visual.Artwork.color = Color.red; visual.RefreshVisual();
            Require(visual.Artwork.color == Color.red, "Damage flash was overwritten");
        }
        finally { Object.DestroyImmediate(coreRoot); }
    }

    static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
#endif
