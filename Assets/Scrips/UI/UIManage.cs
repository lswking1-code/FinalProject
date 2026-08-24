using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public class UIManage : MonoBehaviour
{
    public PlayerStatBar playerStatBar;
    public BulletUI bulletUI;
    public PullCooldownUI pullCooldownUI;
    [Header("事件监听")]
    public CharacterEventSO healthEvent;
    public SceneLoadEventSO unloadedSceneEvent;
    public VoidEventSO loadDataEvent;
    public VoidEventSO GameOverEvent;
    public VoidEventSO backToMenuEvent;
    public VoidEventSO GameClearEvent;
    public VoidEventSO newGameEvent;

    [Header("组件")]
    public GameObject gameOverPannel;
    public GameObject gameClearPannel;
    public GameObject gamePausePanel;
    public GameObject restartBtn;
    public GameObject replayBtn;
    public GameObject pauseRestartBtn;
    public GameObject abilities;
    public GameObject collection;

    GameOverActions endGameActions;
    InputSystem_Actions actions;
    SceneLoader sceneLoader;
    RunTimer runTimer;
    TMP_Text clearTimeText;

    void Awake()
    {
        sceneLoader = FindFirstObjectByType<SceneLoader>();
        runTimer = sceneLoader != null
            ? sceneLoader.GetComponent<RunTimer>()
            : FindFirstObjectByType<RunTimer>();
        EnsureGameOverUI();
        EnsureGameClearUI();
        CacheClearTimeText();
        WireEndGameButtons();
        WirePauseButtons();
    }

    void EnsureGameOverUI()
    {
        // 场景里已有设计好的 GameOverPanel（子物体常为 Restart，而非 RestartButton）时不要再生成一套
        if (gameOverPannel == null || gameOverPannel.transform.childCount > 0)
            return;

        var overlay = CreateUIObject("Overlay", gameOverPannel.transform);
        var overlayImage = overlay.AddComponent<Image>();
        overlayImage.color = new Color(0f, 0f, 0f, 0.75f);
        StretchFull(overlay.GetComponent<RectTransform>());

        var title = CreateUIObject("Title", gameOverPannel.transform);
        var titleRect = title.GetComponent<RectTransform>();
        titleRect.anchorMin = new Vector2(0.5f, 0.65f);
        titleRect.anchorMax = new Vector2(0.5f, 0.65f);
        titleRect.sizeDelta = new Vector2(400f, 80f);
        titleRect.anchoredPosition = Vector2.zero;
        var titleText = title.AddComponent<Text>();
        titleText.text = "游戏结束";
        titleText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        titleText.fontSize = 48;
        titleText.alignment = TextAnchor.MiddleCenter;
        titleText.color = Color.white;

        var buttonGo = CreateUIObject("RestartButton", gameOverPannel.transform);
        var buttonRect = buttonGo.GetComponent<RectTransform>();
        buttonRect.anchorMin = new Vector2(0.5f, 0.4f);
        buttonRect.anchorMax = new Vector2(0.5f, 0.4f);
        buttonRect.sizeDelta = new Vector2(220f, 56f);
        buttonRect.anchoredPosition = Vector2.zero;
        var buttonImage = buttonGo.AddComponent<Image>();
        buttonImage.color = new Color(0.85f, 0.2f, 0.2f, 1f);
        var button = buttonGo.AddComponent<Button>();
        button.targetGraphic = buttonImage;

        var labelGo = CreateUIObject("Text", buttonGo.transform);
        StretchFull(labelGo.GetComponent<RectTransform>());
        var label = labelGo.AddComponent<Text>();
        label.text = "重新开始";
        label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        label.fontSize = 28;
        label.alignment = TextAnchor.MiddleCenter;
        label.color = Color.white;

        restartBtn = buttonGo;
    }

    void EnsureGameClearUI()
    {
        // 场景里已有设计好的 GameClearPanel（子物体常为 Restart，而非 Replay）时不要再生成一套
        if (gameClearPannel == null || gameClearPannel.transform.childCount > 0)
            return;

        var overlay = CreateUIObject("Overlay", gameClearPannel.transform);
        var overlayImage = overlay.AddComponent<Image>();
        overlayImage.color = new Color(0f, 0f, 0f, 0.75f);
        StretchFull(overlay.GetComponent<RectTransform>());

        var title = CreateUIObject("Title", gameClearPannel.transform);
        var titleRect = title.GetComponent<RectTransform>();
        titleRect.anchorMin = new Vector2(0.5f, 0.65f);
        titleRect.anchorMax = new Vector2(0.5f, 0.65f);
        titleRect.sizeDelta = new Vector2(400f, 80f);
        titleRect.anchoredPosition = Vector2.zero;
        var titleText = title.AddComponent<Text>();
        titleText.text = "通关";
        titleText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        titleText.fontSize = 48;
        titleText.alignment = TextAnchor.MiddleCenter;
        titleText.color = Color.white;

        var buttonGo = CreateUIObject("ReplayButton", gameClearPannel.transform);
        var buttonRect = buttonGo.GetComponent<RectTransform>();
        buttonRect.anchorMin = new Vector2(0.5f, 0.4f);
        buttonRect.anchorMax = new Vector2(0.5f, 0.4f);
        buttonRect.sizeDelta = new Vector2(220f, 56f);
        buttonRect.anchoredPosition = Vector2.zero;
        var buttonImage = buttonGo.AddComponent<Image>();
        buttonImage.color = new Color(0.2f, 0.55f, 0.85f, 1f);
        var button = buttonGo.AddComponent<Button>();
        button.targetGraphic = buttonImage;

        var labelGo = CreateUIObject("Text", buttonGo.transform);
        StretchFull(labelGo.GetComponent<RectTransform>());
        var label = labelGo.AddComponent<Text>();
        label.text = "再玩一次";
        label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        label.fontSize = 28;
        label.alignment = TextAnchor.MiddleCenter;
        label.color = Color.white;

        replayBtn = buttonGo;
    }

    void WireEndGameButtons()
    {
        endGameActions = GetComponent<GameOverActions>();
        if (endGameActions == null)
            endGameActions = gameObject.AddComponent<GameOverActions>();
        endGameActions.Configure(loadDataEvent, backToMenuEvent, newGameEvent);

        if (gameOverPannel != null)
        {
            var restart = FindChildButton(gameOverPannel.transform, "Restart", "RestartButton");
            if (restart != null)
            {
                restartBtn = restart.gameObject;
                BindButton(restart, endGameActions.OnRestartFromSave);
            }

            var backToMenu = FindChildButton(gameOverPannel.transform, "Backtomenu", "BackToMenu", "Back");
            if (backToMenu != null)
                BindButton(backToMenu, endGameActions.OnBackToMenu);
        }

        if (gameClearPannel != null)
        {
            var replay = FindChildButton(gameClearPannel.transform, "Replay", "ReplayButton", "Restart");
            if (replay != null)
            {
                replayBtn = replay.gameObject;
                BindButton(replay, endGameActions.OnReplay);
            }

            var backToMenu = FindChildButton(gameClearPannel.transform, "Backtomenu", "BackToMenu", "Back");
            if (backToMenu != null)
                BindButton(backToMenu, endGameActions.OnBackToMenu);
        }
    }

    void WirePauseButtons()
    {
        if (endGameActions == null)
            return;

        if (gamePausePanel == null)
            return;

        var restart = FindChildButton(gamePausePanel.transform, "Restart", "RestartButton");
        if (restart != null)
        {
            pauseRestartBtn = restart.gameObject;
            BindButton(restart, endGameActions.OnRestartFromSave);
        }

        var backToMenu = FindChildButton(gamePausePanel.transform, "Backtomenu", "BackToMenu", "Back");
        if (backToMenu != null)
            BindButton(backToMenu, endGameActions.OnBackToMenu);
    }

    static Button FindChildButton(Transform root, params string[] names)
    {
        if (root == null)
            return null;

        for (int i = 0; i < names.Length; i++)
        {
            var t = root.Find(names[i]);
            if (t == null)
                continue;
            var button = t.GetComponent<Button>();
            if (button != null)
                return button;
        }

        return null;
    }

    static void BindButton(Button button, UnityEngine.Events.UnityAction action)
    {
        if (button == null || action == null)
            return;

        button.onClick.RemoveListener(action);
        button.onClick.AddListener(action);
    }

    static GameObject CreateUIObject(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go;
    }

    static void StretchFull(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private void OnEnable()
    {
        healthEvent.OnEventRaised += OnHealthEvent;
        unloadedSceneEvent.LoadRequestEvent += OnUnLoadedSceneEvent;
        loadDataEvent.OnEventRaised += OnCloseEndGamePanels;
        GameOverEvent.OnEventRaised += OnGameOverEvent;
        backToMenuEvent.OnEventRaised += OnCloseEndGamePanels;
        GameClearEvent.OnEventRaised += OnGameClearEvent;
        if (newGameEvent != null)
            newGameEvent.OnEventRaised += OnCloseEndGamePanels;

        actions ??= new InputSystem_Actions();
        actions.Player.Enable();
        actions.Player.Menu.performed += OnMenuPerformed;
    }

    private void OnDisable()
    {
        healthEvent.OnEventRaised -= OnHealthEvent;
        unloadedSceneEvent.LoadRequestEvent -= OnUnLoadedSceneEvent;
        loadDataEvent.OnEventRaised -= OnCloseEndGamePanels;
        GameOverEvent.OnEventRaised -= OnGameOverEvent;
        backToMenuEvent.OnEventRaised -= OnCloseEndGamePanels;
        GameClearEvent.OnEventRaised -= OnGameClearEvent;
        if (newGameEvent != null)
            newGameEvent.OnEventRaised -= OnCloseEndGamePanels;

        if (actions != null)
            actions.Player.Menu.performed -= OnMenuPerformed;
    }

    void OnDestroy()
    {
        if (actions == null)
            return;

        actions.Dispose();
        actions = null;
    }

    void OnMenuPerformed(InputAction.CallbackContext context)
    {
        if (!CanTogglePause())
            return;

        if (GameplayPause.IsPaused || IsPausePanelOpen())
            ClosePause();
        else
            OpenPause();
    }

    bool CanTogglePause()
    {
        if (IsEndGamePanelOpen() || GameplayHold.IsHeld)
            return false;

        if (sceneLoader == null)
            sceneLoader = FindFirstObjectByType<SceneLoader>();

        if (sceneLoader == null || sceneLoader.IsLoading)
            return false;

        if (GameplayPause.IsPaused || IsPausePanelOpen())
            return true;

        return sceneLoader.IsGameplayScene;
    }

    bool IsEndGamePanelOpen()
    {
        return (gameOverPannel != null && gameOverPannel.activeSelf)
            || (gameClearPannel != null && gameClearPannel.activeSelf);
    }

    bool IsPausePanelOpen()
    {
        return gamePausePanel != null && gamePausePanel.activeSelf;
    }

    void OpenPause()
    {
        GameplayPause.Pause();
        if (gamePausePanel != null)
            gamePausePanel.SetActive(true);

        if (pauseRestartBtn != null && EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(pauseRestartBtn);
    }

    void ClosePause()
    {
        GameplayPause.Resume();
        if (gamePausePanel != null)
            gamePausePanel.SetActive(false);
    }

    private void OnGameClearEvent()
    {
        ClosePause();
        runTimer?.Stop();
        ApplyClearTimeDisplay();
        if (gameClearPannel != null)
            gameClearPannel.SetActive(true);
        EventSystem.current.SetSelectedGameObject(replayBtn);
    }

    private void OnGameOverEvent()
    {
        ClosePause();
        runTimer?.Stop();
        gameOverPannel.SetActive(true);
        EventSystem.current.SetSelectedGameObject(restartBtn);
    }

    void CacheClearTimeText()
    {
        if (gameClearPannel == null)
            return;

        var timeTransform = gameClearPannel.transform.Find("Time");
        if (timeTransform == null)
            return;

        clearTimeText = timeTransform.GetComponent<TMP_Text>();
    }

    void ApplyClearTimeDisplay()
    {
        if (clearTimeText == null)
            CacheClearTimeText();
        if (clearTimeText == null)
            return;

        var timer = runTimer != null ? runTimer : RunTimer.Instance;
        if (timer == null)
            return;

        clearTimeText.text = timer.FormatElapsed();
        clearTimeText.color = timer.GetRankColor();
    }

    public void CloseEndGamePanels()
    {
        ClosePause();
        if (gameOverPannel != null)
            gameOverPannel.SetActive(false);
        if (gameClearPannel != null)
            gameClearPannel.SetActive(false);
        GameplayHold.Release();
    }

    private void OnCloseEndGamePanels() => CloseEndGamePanels();

    private void OnUnLoadedSceneEvent(GameSceneSO sceneToLoad, Vector3 arg1, bool arg2)
    {
        ClosePause();
        var isMenu = sceneToLoad.sceneType == SceneType.Menu;// 判断是否为菜单场景，用于控制 HUD 显示
        playerStatBar.gameObject.SetActive(!isMenu);
        abilities.SetActive(!isMenu);
        collection.SetActive(!isMenu);
        if (bulletUI != null)
            bulletUI.gameObject.SetActive(!isMenu);
        if (pullCooldownUI != null)
            pullCooldownUI.gameObject.SetActive(!isMenu);
    }

    private void OnHealthEvent(Character character)
    {
        var persebrage = character.currentHealth / character.maxHealth;
        playerStatBar.OnHealthChange(persebrage);

        playerStatBar.OnPowerChange(character);
        playerStatBar.OnAPChange(character);
        bulletUI?.OnCharacterChange(character);
    }

}
