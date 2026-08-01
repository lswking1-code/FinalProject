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

    /// <summary>从存档恢复（死亡后 Restart）。</summary>
    public void OnRestartFromSave()
    {
        loadDataEvent?.RaiseEvent();
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
