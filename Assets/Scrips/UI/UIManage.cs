using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

public class UIManage : MonoBehaviour
{
    public PlayerStatBar playerStatBar;
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
    }

    private void OnHealthEvent(Character character)
    {
        var persebrage = character.currentHealth / character.maxHealth;
        playerStatBar.OnHealthChange(persebrage);

        playerStatBar.OnPowerChange(character);
        playerStatBar.OnAPChange(character);
    }

}
