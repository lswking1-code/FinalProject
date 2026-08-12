#if UNITY_EDITOR
using System.IO;
using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class FrameworkSetupEditor
{
    private const string DataSoRoot = "Assets/Data SO";
    private const string EventFolder = "Assets/Data SO/Event";
    private const string SceneSoFolder = "Assets/Data SO/Game Scenes";
    private const string PersistentScenePath = "Assets/Scenes/Persistent.unity";
    private const string InitScenePath = "Assets/Scenes/Init.unity";
    private const string MainMenuScenePath = "Assets/Scenes/MainMenu.unity";
    private const string Level1ScenePath = "Assets/Scenes/Level1.unity";

    [MenuItem("Lost Division/Setup Load Save Framework")]
    public static void SetupFramework()
    {
        EnsureTags();
        var events = CreateEventAssets();
        var scenes = CreateGameSceneAssets();
        CreatePersistentScene(events, scenes);
        RegisterAddressables();
        CreateInitScene();
        SetupMainMenuScene(events);
        SetupLevel1Scene();
        UpdateBuildSettings();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("Lost Division 加载/存档框架配置完成。请打开 Init 场景并运行测试。");
    }

    private static void EnsureTags()
    {
        AddTag("Player");
        AddTag("Bounds");
        AddTag("interactable");
        AddTag("Water");
    }

    private static void AddTag(string tag)
    {
        var tagManager = new SerializedObject(
            AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
        var tagsProp = tagManager.FindProperty("tags");
        for (int i = 0; i < tagsProp.arraySize; i++)
        {
            if (tagsProp.GetArrayElementAtIndex(i).stringValue == tag)
                return;
        }

        tagsProp.InsertArrayElementAtIndex(tagsProp.arraySize);
        tagsProp.GetArrayElementAtIndex(tagsProp.arraySize - 1).stringValue = tag;
        tagManager.ApplyModifiedProperties();
    }

    private static EventAssets CreateEventAssets()
    {
        Directory.CreateDirectory(EventFolder);
        return new EventAssets
        {
            sceneLoad = CreateAsset<VoidLike, SceneLoadEventSO>(EventFolder, "Scene Load Event SO"),
            sceneUnloaded = CreateAsset<VoidLike, SceneLoadEventSO>(EventFolder, "Scene UnLoaded Event SO"),
            fade = CreateAsset<VoidLike, FadeEventSO>(EventFolder, "Fade Event SO"),
            afterSceneLoaded = CreateAsset<VoidLike, VoidEventSO>(EventFolder, "After Scene Load Event SO"),
            newGame = CreateAsset<VoidLike, VoidEventSO>(EventFolder, "New Game Event SO"),
            backToMenu = CreateAsset<VoidLike, VoidEventSO>(EventFolder, "BackToMenuEvent SO"),
            saveData = CreateAsset<VoidLike, VoidEventSO>(EventFolder, "Save Game Data Event SO"),
            loadData = CreateAsset<VoidLike, VoidEventSO>(EventFolder, "Load Game Data Event SO"),
            gameOver = CreateAsset<VoidLike, VoidEventSO>(EventFolder, "GameOverEvent SO"),
            gameClear = CreateAsset<VoidLike, VoidEventSO>(EventFolder, "GameClearEvent SO"),
            health = CreateAsset<VoidLike, CharacterEventSO>(EventFolder, "CharacterEventSO"),
            cameraShake = CreateAsset<VoidLike, FloatEventSO>(EventFolder, "CameraShakeSO"),
            cameraHorizontalShake = CreateAsset<VoidLike, FloatEventSO>(EventFolder, "CameraHorizontalShakeSO"),
            cameraRecoilShake = CreateAsset<VoidLike, FloatEventSO>(EventFolder, "CameraRecoilShakeSO"),
        };
    }

    private static T CreateAsset<TMarker, T>(string folder, string name) where T : ScriptableObject
    {
        string path = $"{folder}/{name}.asset";
        var existing = AssetDatabase.LoadAssetAtPath<T>(path);
        if (existing != null)
            return existing;

        var asset = ScriptableObject.CreateInstance<T>();
        AssetDatabase.CreateAsset(asset, path);
        return asset;
    }

    private static GameSceneAssets CreateGameSceneAssets()
    {
        Directory.CreateDirectory(SceneSoFolder);
        return new GameSceneAssets
        {
            mainMenu = CreateGameSceneSo("MainMenu", MainMenuScenePath, SceneType.Menu),
            level1 = CreateGameSceneSo("Level1", Level1ScenePath, SceneType.Loaction),
        };
    }

    private static GameSceneSO CreateGameSceneSo(string name, string scenePath, SceneType sceneType)
    {
        string assetPath = $"{SceneSoFolder}/{name}.asset";
        var existing = AssetDatabase.LoadAssetAtPath<GameSceneSO>(assetPath);
        if (existing == null)
        {
            existing = ScriptableObject.CreateInstance<GameSceneSO>();
            AssetDatabase.CreateAsset(existing, assetPath);
        }

        existing.sceneType = sceneType;
        SetSceneReference(existing, scenePath);
        EditorUtility.SetDirty(existing);
        return existing;
    }

    private static void SetSceneReference(GameSceneSO gameScene, string scenePath)
    {
        string guid = AssetDatabase.AssetPathToGUID(scenePath);
        var serialized = new SerializedObject(gameScene);
        var reference = serialized.FindProperty("sceneReference");
        reference.FindPropertyRelative("m_AssetGUID").stringValue = guid;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void RegisterAddressables()
    {
        var settings = AddressableAssetSettingsDefaultObject.GetSettings(true);
        var group = settings.DefaultGroup;

        MarkAddressable(settings, group, PersistentScenePath, "Persistent");
        MarkAddressable(settings, group, MainMenuScenePath, "MainMenu");
        MarkAddressable(settings, group, Level1ScenePath, "Level1");
        settings.SetDirty(AddressableAssetSettings.ModificationEvent.GroupAdded, null, true);
    }

    private static void MarkAddressable(
        AddressableAssetSettings settings,
        AddressableAssetGroup group,
        string assetPath,
        string address)
    {
        string guid = AssetDatabase.AssetPathToGUID(assetPath);
        var entry = settings.CreateOrMoveEntry(guid, group);
        entry.SetAddress(address);
    }

    private static void CreatePersistentScene(EventAssets events, GameSceneAssets scenes)
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var sceneLoaderGo = new GameObject("SceneLoad Manager");
        var sceneLoader = sceneLoaderGo.AddComponent<SceneLoader>();
        EnsureDataDefinition(sceneLoaderGo);

        var dataManagerGo = new GameObject("DataManager");
        var dataManager = dataManagerGo.AddComponent<DataManager>();

        var fadeRoot = CreateFadeCanvas(events.fade);
        var player = CreatePlayer(events.newGame);
        var cameraRoot = CreateCameraRig(events, player.transform);
        var uiRoot = CreateGameUI(events);

        sceneLoader.playerTrans = player.transform;
        sceneLoader.firstPosition = new Vector3(0f, 0f, 0f);
        sceneLoader.menuPosition = Vector3.zero;
        sceneLoader.firstLoadScene = scenes.level1;
        sceneLoader.menuScene = scenes.mainMenu;
        sceneLoader.fadeDuration = 0.5f;
        sceneLoader.loadEventSO = events.sceneLoad;
        sceneLoader.newGameEvent = events.newGame;
        sceneLoader.backToMenuEvent = events.backToMenu;
        sceneLoader.afterSceneLoadedEvent = events.afterSceneLoaded;
        sceneLoader.fadeEvent = events.fade;
        sceneLoader.unloadedSceneEvent = events.sceneUnloaded;

        dataManager.saveDataEvent = events.saveData;
        dataManager.loadDataEvent = events.loadData;
        dataManager.newGameEvent = events.newGame;

        EditorSceneManager.SaveScene(scene, PersistentScenePath);
    }

    private static GameObject CreateFadeCanvas(FadeEventSO fadeEvent)
    {
        var root = new GameObject("Fade Canvas");
        var canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 999;
        root.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        root.AddComponent<GraphicRaycaster>();

        var imageGo = new GameObject("Fade Image");
        imageGo.transform.SetParent(root.transform, false);
        var rect = imageGo.AddComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        var image = imageGo.AddComponent<Image>();
        image.color = new Color(0f, 0f, 0f, 0f);
        image.raycastTarget = false;

        var fadeCanvas = root.AddComponent<FadeCanvas>();
        fadeCanvas.fadeEvent = fadeEvent;
        fadeCanvas.fadeImage = image;
        return root;
    }

    private static GameObject CreatePlayer(VoidEventSO newGameEvent)
    {
        var player = new GameObject("Player");
        player.tag = "Player";
        player.SetActive(false);

        EnsureDataDefinition(player);
        var character = player.AddComponent<Character>();
        character.newGameEvent = newGameEvent;
        character.maxHealth = 100f;
        character.currentHealth = 100f;
        character.maxPower = 100f;
        character.currentPower = 100f;
        character.maxAbilityPower = 100f;
        character.AbilityPower = 100f;

        return player;
    }

    private static GameObject CreateCameraRig(EventAssets events, Transform followTarget)
    {
        var mainCameraGo = new GameObject("Main Camera");
        mainCameraGo.tag = "MainCamera";
        var camera = mainCameraGo.AddComponent<Camera>();
        camera.orthographic = true;
        camera.orthographicSize = 5f;
        mainCameraGo.AddComponent<AudioListener>();
        mainCameraGo.AddComponent<CinemachineBrain>();

        var vcamGo = new GameObject("CM vcam1");
        vcamGo.transform.SetParent(mainCameraGo.transform.parent);
        var vcam = vcamGo.AddComponent<CinemachineCamera>();
        vcam.Follow = followTarget;
        vcam.Lens.OrthographicSize = 5f;

        var confiner = vcamGo.AddComponent<CinemachineConfiner2D>();
        var impulse = vcamGo.AddComponent<CinemachineImpulseSource>();
        var horizontalImpulse = vcamGo.AddComponent<CinemachineImpulseSource>();
        horizontalImpulse.DefaultVelocity = new Vector3(1f, 0f, 0f);
        var recoilImpulse = vcamGo.AddComponent<CinemachineImpulseSource>();
        recoilImpulse.ImpulseDefinition.ImpulseShape = CinemachineImpulseDefinition.ImpulseShapes.Recoil;
        var impulseListener = vcamGo.AddComponent<CinemachineImpulseListener>();
        impulseListener.Use2DDistance = true;
        var cameraControl = vcamGo.AddComponent<CameraControl>();
        cameraControl.afterSceneLoadEvent = events.afterSceneLoaded;
        cameraControl.cameraShakeEvent = events.cameraShake;
        cameraControl.impulseSource = impulse;
        cameraControl.cameraHorizontalShakeEvent = events.cameraHorizontalShake;
        cameraControl.horizontalImpulseSource = horizontalImpulse;
        cameraControl.cameraRecoilShakeEvent = events.cameraRecoilShake;
        cameraControl.recoilImpulseSource = recoilImpulse;

        var idleRecenter = vcamGo.AddComponent<CameraIdleRecenter>();
        idleRecenter.afterSceneLoadEvent = events.afterSceneLoaded;

        var airborneYLock = vcamGo.AddComponent<CameraAirborneYLock>();
        airborneYLock.afterSceneLoadEvent = events.afterSceneLoaded;

        return mainCameraGo;
    }

    private static GameObject CreateGameUI(EventAssets events)
    {
        var root = new GameObject("UI Canvas");
        var canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        root.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        root.AddComponent<GraphicRaycaster>();

        var hud = new GameObject("PlayerStatBar");
        hud.transform.SetParent(root.transform, false);
        var hudRect = hud.AddComponent<RectTransform>();
        hudRect.anchorMin = new Vector2(0f, 1f);
        hudRect.anchorMax = new Vector2(0f, 1f);
        hudRect.pivot = new Vector2(0f, 1f);
        hudRect.anchoredPosition = new Vector2(20f, -20f);
        hudRect.sizeDelta = new Vector2(200f, 30f);
        var hudImage = hud.AddComponent<Image>();
        hudImage.color = new Color(1f, 0.2f, 0.2f, 0.8f);
        hud.AddComponent<PlayerStatBar>();

        var gameOver = CreatePanel(root.transform, "GameOverPanel", false);
        var gameClear = CreatePanel(root.transform, "GameClearPanel", false);
        hud.SetActive(false);
        var abilities = CreatePanel(root.transform, "Abilities", false);
        var collection = CreatePanel(root.transform, "Collection", false);

        var uiManage = root.AddComponent<UIManage>();
        uiManage.playerStatBar = hud.GetComponent<PlayerStatBar>();
        uiManage.healthEvent = events.health;
        uiManage.unloadedSceneEvent = events.sceneUnloaded;
        uiManage.loadDataEvent = events.loadData;
        uiManage.GameOverEvent = events.gameOver;
        uiManage.backToMenuEvent = events.backToMenu;
        uiManage.GameClearEvent = events.gameClear;
        uiManage.gameOverPannel = gameOver;
        uiManage.gameClearPannel = gameClear;
        uiManage.abilities = abilities;
        uiManage.collection = collection;
        uiManage.restartBtn = gameOver;
        uiManage.replayBtn = gameClear;

        if (Object.FindObjectOfType<EventSystem>() == null)
        {
            var eventSystem = new GameObject("EventSystem");
            eventSystem.AddComponent<EventSystem>();
            eventSystem.AddComponent<StandaloneInputModule>();
        }

        return root;
    }

    private static GameObject CreatePanel(Transform parent, string name, bool active)
    {
        var panel = new GameObject(name);
        panel.transform.SetParent(parent, false);
        var rect = panel.AddComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        panel.SetActive(active);
        return panel;
    }

    private static void CreateInitScene()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var initGo = new GameObject("Initial Load");
        var initLoad = initGo.AddComponent<InitalLoad>();

        string persistentGuid = AssetDatabase.AssetPathToGUID(PersistentScenePath);
        var serialized = new SerializedObject(initLoad);
        var reference = serialized.FindProperty("persistentScene");
        reference.FindPropertyRelative("m_AssetGUID").stringValue = persistentGuid;
        serialized.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.SaveScene(scene, InitScenePath);
    }

    private static void SetupMainMenuScene(EventAssets events)
    {
        var scene = EditorSceneManager.OpenScene(MainMenuScenePath, OpenSceneMode.Single);

        foreach (var cam in Object.FindObjectsOfType<Camera>())
            cam.gameObject.SetActive(false);

        if (Object.FindObjectOfType<EventSystem>() == null)
        {
            var eventSystem = new GameObject("EventSystem");
            eventSystem.AddComponent<EventSystem>();
            eventSystem.AddComponent<StandaloneInputModule>();
        }

        var canvasGo = GameObject.Find("Menu Canvas") ?? new GameObject("Menu Canvas");
        if (canvasGo.GetComponent<Canvas>() == null)
        {
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasGo.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            canvasGo.AddComponent<GraphicRaycaster>();
        }

        var menu = canvasGo.GetComponent<Menu>() ?? canvasGo.AddComponent<Menu>();
        var menuActions = canvasGo.GetComponent<MenuActions>() ?? canvasGo.AddComponent<MenuActions>();
        menuActions.newGameEvent = events.newGame;
        menuActions.saveDataEvent = events.saveData;
        menuActions.loadDataEvent = events.loadData;

        var newGameButton = CreateMenuButton(canvasGo.transform, "New Game Button", "新游戏", new Vector2(0f, 40f));
        menu.newGameButton = newGameButton;
        BindButton(newGameButton, menuActions.OnNewGame);

        var exitButton = CreateMenuButton(canvasGo.transform, "Exit Button", "退出", new Vector2(0f, -40f));
        BindButton(exitButton, menuActions.OnExit);

        var saveBtn = CreateMenuButton(canvasGo.transform, "Save Button", "保存", new Vector2(160f, 40f));
        BindButton(saveBtn, menuActions.OnSave);

        var loadBtn = CreateMenuButton(canvasGo.transform, "Load Button", "读档", new Vector2(160f, -40f));
        BindButton(loadBtn, menuActions.OnLoad);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }

    private static void BindButton(GameObject buttonGo, UnityEngine.Events.UnityAction action)
    {
        var button = buttonGo.GetComponent<Button>();
        for (int i = button.onClick.GetPersistentEventCount() - 1; i >= 0; i--)
            UnityEventTools.RemovePersistentListener(button.onClick, i);
        UnityEventTools.AddPersistentListener(button.onClick, action);
    }

    private static GameObject CreateMenuButton(Transform parent, string name, string label, Vector2 position)
    {
        var existing = parent.Find(name);
        if (existing != null)
            return existing.gameObject;

        var buttonGo = new GameObject(name);
        buttonGo.transform.SetParent(parent, false);
        var rect = buttonGo.AddComponent<RectTransform>();
        rect.sizeDelta = new Vector2(160f, 40f);
        rect.anchoredPosition = position;

        var image = buttonGo.AddComponent<Image>();
        image.color = new Color(0.2f, 0.2f, 0.2f, 0.9f);
        buttonGo.AddComponent<Button>();

        var textGo = new GameObject("Text");
        textGo.transform.SetParent(buttonGo.transform, false);
        var textRect = textGo.AddComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;
        var text = textGo.AddComponent<Text>();
        text.text = label;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = Color.white;
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        return buttonGo;
    }

    private static void SetupLevel1Scene()
    {
        var scene = EditorSceneManager.OpenScene(Level1ScenePath, OpenSceneMode.Single);

        foreach (var cam in Object.FindObjectsOfType<Camera>())
            cam.gameObject.SetActive(false);

        var bounds = GameObject.Find("Bounds");
        if (bounds == null)
        {
            bounds = new GameObject("Bounds");
            bounds.tag = "Bounds";
            var box = bounds.AddComponent<BoxCollider2D>();
            box.size = new Vector2(20f, 12f);
            box.isTrigger = true;
        }

        var spawn = GameObject.Find("SpawnPoint");
        if (spawn == null)
        {
            spawn = new GameObject("SpawnPoint");
            spawn.transform.position = Vector3.zero;
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }

    private static void UpdateBuildSettings()
    {
        string initGuid = AssetDatabase.AssetPathToGUID(InitScenePath);
        var scenes = new[]
        {
            new EditorBuildSettingsScene(InitScenePath, true),
            new EditorBuildSettingsScene(PersistentScenePath, false),
            new EditorBuildSettingsScene(MainMenuScenePath, false),
            new EditorBuildSettingsScene(Level1ScenePath, false),
        };
        EditorBuildSettings.scenes = scenes;
    }

    private static void EnsureDataDefinition(GameObject target)
    {
        var dataDef = target.GetComponent<DataDefination>() ?? target.AddComponent<DataDefination>();
        dataDef.persistentType = PersistentType.ReadWrite;
        if (string.IsNullOrEmpty(dataDef.ID))
            dataDef.ID = System.Guid.NewGuid().ToString();
    }

    private struct VoidLike { }

    private class EventAssets
    {
        public SceneLoadEventSO sceneLoad;
        public SceneLoadEventSO sceneUnloaded;
        public FadeEventSO fade;
        public VoidEventSO afterSceneLoaded;
        public VoidEventSO newGame;
        public VoidEventSO backToMenu;
        public VoidEventSO saveData;
        public VoidEventSO loadData;
        public VoidEventSO gameOver;
        public VoidEventSO gameClear;
        public CharacterEventSO health;
        public FloatEventSO cameraShake;
        public FloatEventSO cameraHorizontalShake;
        public FloatEventSO cameraRecoilShake;
    }

    private class GameSceneAssets
    {
        public GameSceneSO mainMenu;
        public GameSceneSO level1;
    }
}
#endif
