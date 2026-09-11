using UnityEngine;

public class GameOverActions : MonoBehaviour
{
    VoidEventSO loadDataEvent;
    VoidEventSO backToMenuEvent;
    VoidEventSO newGameEvent;

    public void Configure(VoidEventSO loadEvent, VoidEventSO backToMenu, VoidEventSO newGame)
    {
        loadDataEvent = loadEvent;
        backToMenuEvent = backToMenu;
        newGameEvent = newGame;
    }

    /// <summary>GAME OVER Restart：清空进度，从 Stage1 起点重开。</summary>
    public void OnRestartFromSave()
    {
        var loader = FindFirstObjectByType<SceneLoader>();
        if (loader != null)
            loader.RestartCurrentLevel();
    }

    /// <summary>暂停 Restart：回到最新存档点；没有存档时再从 Stage1 重开。</summary>
    public void OnRestartFromCheckpoint()
    {
        if (HasLatestCheckpoint())
        {
            loadDataEvent?.RaiseEvent();
            return;
        }

        OnRestartFromSave();
    }

    static bool HasLatestCheckpoint()
    {
        var data = DataManager.instance;
        var loader = FindFirstObjectByType<SceneLoader>();
        if (data == null || loader == null || loader.playerTrans == null)
            return false;

        var def = loader.playerTrans.GetComponent<DataDefination>();
        return def != null && data.HasPlayerCheckpoint(def.ID);
    }

    /// <summary>返回主菜单。</summary>
    public void OnBackToMenu()
    {
        backToMenuEvent?.RaiseEvent();
    }

    /// <summary>通关后重开一局（新游戏）。</summary>
    public void OnReplay()
    {
        newGameEvent?.RaiseEvent();
    }
}
