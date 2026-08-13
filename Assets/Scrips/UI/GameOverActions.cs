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

    /// <summary>GAME OVER 后 Restart：重开本关并刷新数值。</summary>
    public void OnRestartFromSave()
    {
        var loader = FindFirstObjectByType<SceneLoader>();
        if (loader != null)
            loader.RestartCurrentLevel();
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
