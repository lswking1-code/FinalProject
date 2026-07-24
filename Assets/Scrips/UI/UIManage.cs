using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
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

    [Header("组件")]
    public GameObject gameOverPannel;
    public GameObject gameClearPannel;
    public GameObject restartBtn;
    public GameObject replayBtn;
    public GameObject abilities;
    public GameObject collection;

    void Awake()
    {
        EnsureGameOverUI();
    }

    void EnsureGameOverUI()
    {
        if (gameOverPannel == null || gameOverPannel.transform.Find("RestartButton") != null)
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

        var actions = GetComponent<GameOverActions>();
        if (actions == null)
            actions = gameObject.AddComponent<GameOverActions>();
        actions.Configure(loadDataEvent);
        button.onClick.AddListener(actions.OnRestartFromSave);

        restartBtn = buttonGo;
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
        loadDataEvent.OnEventRaised += OnLoadDataEvent;
        GameOverEvent.OnEventRaised += OnGameOverEvent;
        backToMenuEvent.OnEventRaised += OnLoadDataEvent;
        GameClearEvent.OnEventRaised += OnGameClearEvent;
    }

   

    private void OnDisable()
    {
        healthEvent.OnEventRaised -= OnHealthEvent;
        unloadedSceneEvent.LoadRequestEvent -= OnUnLoadedSceneEvent;
        loadDataEvent.OnEventRaised -= OnLoadDataEvent;
        GameOverEvent.OnEventRaised -= OnGameOverEvent;
        backToMenuEvent.OnEventRaised -= OnLoadDataEvent;
        GameClearEvent.OnEventRaised -= OnGameClearEvent;
    }

    private void OnGameClearEvent()
    {
        gameClearPannel.SetActive(true);
        EventSystem.current.SetSelectedGameObject(replayBtn);
    }

    private void OnGameOverEvent()
    {
        //Debug.Log("dead");
        gameOverPannel.SetActive(true);
        EventSystem.current.SetSelectedGameObject(restartBtn);
    }
    private void OnLoadDataEvent()
    {
        //Debug.Log("close");
        gameOverPannel.SetActive(false);
        gameClearPannel.SetActive(false);
    }
    private void OnUnLoadedSceneEvent(GameSceneSO sceneToLoad, Vector3 arg1, bool arg2)
    {
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
