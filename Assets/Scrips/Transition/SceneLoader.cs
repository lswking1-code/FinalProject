using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.InputSystem;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceProviders;
using UnityEngine.SceneManagement;

public class SceneLoader : MonoBehaviour, ISaveable
{
    [Serializable]
    public class PlayerCharacterBinding
    {
        public PlayerCharacterSO character;
        public Transform instanceTransform;
    }

    public Transform playerTrans;
    public Vector3 firstPosition;
    public Vector3 menuPosition;

    [Header("玩家选择")]
    public PlayerRegistrySO playerRegistry;
    public PlayerCharacterSO selectedCharacter;
    public PlayerCharacterBinding[] playerCharacterBindings = Array.Empty<PlayerCharacterBinding>();
    public event Action<PlayerCharacterSO> SelectedCharacterChanged;

    [Header("相机")]
    public CameraControl cameraControl;

    [Header("事件监听")]
    public SceneLoadEventSO loadEventSO;
    public VoidEventSO newGameEvent;
    public VoidEventSO backToMenuEvent;

    [Header("广播")]
    public VoidEventSO afterSceneLoadedEvent;
    public FadeEventSO fadeEvent;
    public SceneLoadEventSO unloadedSceneEvent;

    [Header("场景")]
    public GameSceneSO firstLoadScene;
    public GameSceneSO menuScene;

    [Header("开发模式")]
    public bool developMode;
    public GameSceneSO testScene;
    public Vector3 testPosition;
    public bool enableDevelopCharacterSwitch = true;
    [Tooltip("开启后按 M 键将当前玩家弹药填满（仅测试用）")]
    public bool enableFillAmmoCheat;

    private GameSceneSO currentLoadedScene;
    private GameSceneSO sceneToLoad;
    private Vector3 positionToGo;
    private bool fadeScreen;
    private bool isLoading;
    public float fadeDuration;

    Vector3 currentSceneEntryPosition;
    bool hasSceneEntry;
    bool pendingRecordEntry;
    bool pendingSaveAfterRestart;

    private void Awake()
    {
        EnsureSelectedCharacter();
        ApplyPlayerSelection();
    }

    // TODO: 完成 MainMenu 流程后调整此处逻辑
    private void Start()
    {
        if (developMode)
        {
            // 首次加载无旧场景可卸载，补发一次供 UIManage 打开 HUD
            unloadedSceneEvent.RaiseLoadRequestEvent(testScene, testPosition, true);
            loadEventSO.RaiseLoadRequestEvent(testScene, testPosition, true);
        }
        else
            loadEventSO.RaiseLoadRequestEvent(menuScene, menuPosition, true);
    }

    private void Update()
    {
        var keyboard = Keyboard.current;
        if (keyboard == null)
            return;

        if (enableFillAmmoCheat && keyboard.mKey.wasPressedThisFrame)
            TryFillPlayerAmmo();

        if (!developMode || !enableDevelopCharacterSwitch || playerRegistry == null)
            return;

        if (keyboard.digit1Key.wasPressedThisFrame)
            SelectCharacterByIndex(0);
        else if (keyboard.digit2Key.wasPressedThisFrame)
            SelectCharacterByIndex(1);
        else if (keyboard.digit3Key.wasPressedThisFrame)
            SelectCharacterByIndex(2);
    }

    void TryFillPlayerAmmo()
    {
        if (playerTrans == null || !playerTrans.gameObject.activeInHierarchy)
            return;

        var character = playerTrans.GetComponent<Character>();
        if (character == null)
            return;

        character.FillAllAmmo();
    }

    private void OnEnable()
    {
        loadEventSO.LoadRequestEvent += OnLoadRequestEvent;
        newGameEvent.OnEventRaised += NewGame;
        backToMenuEvent.OnEventRaised += OnBackToMenuEvent;

        ISaveable saveable = this;
        saveable.RegisterSaveData();
    }

    private void OnDisable()
    {
        loadEventSO.LoadRequestEvent -= OnLoadRequestEvent;
        newGameEvent.OnEventRaised -= NewGame;
        backToMenuEvent.OnEventRaised -= OnBackToMenuEvent;

        ISaveable saveable = this;
        saveable.UnregisterSaveData();
    }

    public void SelectCharacter(PlayerCharacterSO character)
    {
        if (character == null || character == selectedCharacter)
            return;

        bool wasVisible = playerTrans != null && playerTrans.gameObject.activeSelf;
        selectedCharacter = character;
        ApplyPlayerSelection();
        if (wasVisible && playerTrans != null)
            playerTrans.gameObject.SetActive(true);
    }

    public void SelectCharacterByIndex(int index)
    {
        if (playerRegistry == null)
            return;

        SelectCharacter(playerRegistry.GetByIndex(index));
    }

    private void OnBackToMenuEvent()
    {
        sceneToLoad = menuScene;
        loadEventSO.RaiseLoadRequestEvent(sceneToLoad, menuPosition, true);
    }

    private void NewGame()
    {
        ApplyPlayerSelection();
        sceneToLoad = firstLoadScene;
        loadEventSO.RaiseLoadRequestEvent(sceneToLoad, firstPosition, true);
    }

    /// <summary>
    /// GAME OVER Restart：清空进度、重置数值，从本关入口重开。
    /// </summary>
    public void RestartCurrentLevel()
    {
        if (isLoading)
            return;

        var ui = FindFirstObjectByType<UIManage>();
        ui?.CloseEndGamePanels();

        if (currentLoadedScene == null || currentLoadedScene.sceneType != SceneType.Loaction)
        {
            NewGame();
            return;
        }

        DataManager.instance?.ClearForNewGame();
        ResetPlayerForLevelRestart();

        Vector3 entry = hasSceneEntry ? currentSceneEntryPosition : firstPosition;
        pendingSaveAfterRestart = true;
        loadEventSO.RaiseLoadRequestEvent(currentLoadedScene, entry, true);
    }

    void ResetPlayerForLevelRestart()
    {
        if (playerTrans == null)
            return;

        playerTrans.GetComponent<PlayerDeath>()?.ResetForNewGame();
        playerTrans.GetComponent<PlayerMovement>()?.ResetMovementState();
        playerTrans.GetComponent<PlayerAbilities>()?.ResetForNewGame();
        playerTrans.GetComponent<PlayerWeaponController>()?.ResetToInitialWeapon();
        playerTrans.GetComponent<SpecialMagazine>()?.Clear();
    }

    void EnsureSelectedCharacter()
    {
        if (selectedCharacter != null)
            return;

        if (playerRegistry == null)
            return;

        if (playerRegistry.defaultCharacter != null)
            selectedCharacter = playerRegistry.defaultCharacter;
        else if (playerRegistry.characters.Count > 0)
            selectedCharacter = playerRegistry.characters[0];
    }

    void ApplyPlayerSelection()
    {
        if (playerRegistry == null)
        {
            Debug.LogWarning("SceneLoader: 未配置 PlayerRegistrySO。");
            return;
        }

        EnsureSelectedCharacter();
        ResolvePlayerInstances();

        if (selectedCharacter == null)
        {
            Debug.LogWarning("SceneLoader: 未找到有效角色配置。");
            return;
        }

        Transform selected = GetInstanceTransform(selectedCharacter);
        if (selected == null)
        {
            Debug.LogWarning($"SceneLoader: 角色 {selectedCharacter.displayName} 未配置 Persistent 实例引用。");
            SelectedCharacterChanged?.Invoke(selectedCharacter);
            return;
        }

        playerTrans = selected;

        foreach (var binding in playerCharacterBindings)
        {
            if (binding.instanceTransform == null || binding.instanceTransform == selected)
                continue;

            binding.instanceTransform.gameObject.SetActive(false);
        }

        BindCameraToPlayer();
        SelectedCharacterChanged?.Invoke(selectedCharacter);
    }

    void ResolvePlayerInstances()
    {
        if (playerRegistry == null)
            return;

        if (playerCharacterBindings == null || playerCharacterBindings.Length == 0)
            playerCharacterBindings = BuildBindingsFromRegistry();

        foreach (var binding in playerCharacterBindings)
        {
            if (binding.character == null)
                continue;

            binding.instanceTransform = ResolveSceneInstance(binding.instanceTransform, binding.character);
        }
    }

    PlayerCharacterBinding[] BuildBindingsFromRegistry()
    {
        var bindings = new List<PlayerCharacterBinding>();
        foreach (var character in playerRegistry.characters)
        {
            if (character == null)
                continue;

            bindings.Add(new PlayerCharacterBinding
            {
                character = character,
                instanceTransform = FindCharacterInScene(character)
            });
        }

        return bindings.ToArray();
    }

    Transform ResolveSceneInstance(Transform reference, PlayerCharacterSO character)
    {
        if (IsSceneInstance(reference))
            return reference;

        return FindCharacterInScene(character);
    }

    static bool IsSceneInstance(Transform transform)
    {
        return transform != null && transform.gameObject.scene.IsValid();
    }

    Transform FindCharacterInScene(PlayerCharacterSO character)
    {
        if (character == null)
            return null;

        string targetName = GetCharacterObjectName(character);
        foreach (var root in gameObject.scene.GetRootGameObjects())
        {
            if (root.name == targetName)
                return root.transform;

            var child = root.transform.Find(targetName);
            if (child != null)
                return child;
        }

        return null;
    }

    static string GetCharacterObjectName(PlayerCharacterSO character)
    {
        return string.IsNullOrEmpty(character.displayName) ? character.name : character.displayName;
    }

    Transform GetInstanceTransform(PlayerCharacterSO character)
    {
        foreach (var binding in playerCharacterBindings)
        {
            if (!IsSameCharacter(binding.character, character))
                continue;

            if (IsSceneInstance(binding.instanceTransform))
                return binding.instanceTransform;

            binding.instanceTransform = FindCharacterInScene(character);
            return binding.instanceTransform;
        }

        return FindCharacterInScene(character);
    }

    static bool IsSameCharacter(PlayerCharacterSO a, PlayerCharacterSO b)
    {
        if (a == null || b == null)
            return false;

        if (ReferenceEquals(a, b))
            return true;

        return a.name == b.name && GetCharacterObjectName(a) == GetCharacterObjectName(b);
    }

    void BindCameraToPlayer()
    {
        if (cameraControl == null || playerTrans == null)
            return;

        cameraControl.SetFollowTarget(playerTrans);
    }

    private void OnLoadRequestEvent(GameSceneSO locationToLoad, Vector3 posToGo, bool fadeScreen)
    {
        if (isLoading)
            return;

        isLoading = true;
        pendingRecordEntry = locationToLoad != null
            && locationToLoad.sceneType == SceneType.Loaction
            && currentLoadedScene != locationToLoad;

        sceneToLoad = locationToLoad;
        positionToGo = posToGo;
        this.fadeScreen = fadeScreen;
        if (currentLoadedScene != null)
        {
            StartCoroutine(UnLoadPreviousScene());
        }
        else
        {
            LoadNewScene();
        }
    }

    private IEnumerator UnLoadPreviousScene()
    {
        if (playerTrans != null)
        {
            playerTrans.position = positionToGo;
            cameraControl?.SnapCameraToFollowTarget();
        }

        if (fadeScreen)
        {
            fadeEvent.FadeIn(fadeDuration);
        }

        yield return new WaitForSeconds(fadeDuration);

        unloadedSceneEvent.RaiseLoadRequestEvent(sceneToLoad, positionToGo, true);

        // 子弹可能落在 Persistent，卸载关卡前统一清掉
        EnemySceneCleanup.ClearAll();

        yield return currentLoadedScene.sceneReference.UnLoadScene();
        if (playerTrans != null)
            playerTrans.gameObject.SetActive(false);

        LoadNewScene();
    }

    private void LoadNewScene()
    {
        var loadingOption = sceneToLoad.sceneReference.LoadSceneAsync(LoadSceneMode.Additive, true);
        loadingOption.Completed += OnLoadCompleted;
    }

    private void OnLoadCompleted(AsyncOperationHandle<SceneInstance> obj)
    {
        currentLoadedScene = sceneToLoad;

        ApplyPlayerSelection();

        if (playerTrans == null)
        {
            Debug.LogError("SceneLoader: 场景加载完成但 playerTrans 为空，无法显示玩家。");
            isLoading = false;
            pendingRecordEntry = false;
            pendingSaveAfterRestart = false;
            return;
        }

        playerTrans.position = positionToGo;

        playerTrans.gameObject.SetActive(currentLoadedScene.sceneType != SceneType.Menu);
        BindCameraToPlayer();

        if (fadeScreen)
        {
            fadeEvent.FadeOut(fadeDuration);
        }

        isLoading = false;

        if (currentLoadedScene.sceneType == SceneType.Loaction)
        {
            if (pendingRecordEntry)
            {
                currentSceneEntryPosition = positionToGo;
                hasSceneEntry = true;
                pendingRecordEntry = false;
            }

            afterSceneLoadedEvent.RaiseEvent();

            if (pendingSaveAfterRestart)
            {
                pendingSaveAfterRestart = false;
                DataManager.instance?.Save();
            }
        }
        else
        {
            pendingRecordEntry = false;
            pendingSaveAfterRestart = false;
        }
    }

    public DataDefination GetDataID()
    {
        return GetComponent<DataDefination>();
    }

    public void GetSaveData(Data data)
    {
        data.SaveGameScene(currentLoadedScene);
        data.selectedCharacterIndex = playerRegistry != null
            ? playerRegistry.IndexOf(selectedCharacter)
            : -1;
    }

    public void LoadSaveData(Data data)
    {
        if (playerRegistry != null && data.selectedCharacterIndex >= 0)
            selectedCharacter = playerRegistry.GetByIndex(data.selectedCharacterIndex);

        ApplyPlayerSelection();

        if (playerTrans == null)
            return;

        var playerID = playerTrans.GetComponent<DataDefination>().ID;
        if (data.characterPosDict.ContainsKey(playerID))
        {
            positionToGo = data.characterPosDict[playerID].ToVector3();
            sceneToLoad = data.GetSavedScene();

            // Character 可能已瞬移，也可能还没轮到；先放到存档点并 Snap。
            // 否则坠崖读档会先淡出 0.5s，镜头仍停在坑底，视差采到错误基线。
            playerTrans.position = positionToGo;
            cameraControl?.SnapCameraToFollowTarget();

            OnLoadRequestEvent(sceneToLoad, positionToGo, true);
        }
    }
}
