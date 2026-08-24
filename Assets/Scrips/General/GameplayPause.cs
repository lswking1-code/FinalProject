using UnityEngine;

/// <summary>
/// 玩家暂停：用 Time.timeScale 可逆冻结玩法。切场景前必须 Resume，否则淡入淡出会卡住。
/// </summary>
public static class GameplayPause
{
    public static bool IsPaused { get; private set; }

    public static void Pause()
    {
        IsPaused = true;
        Time.timeScale = 0f;
    }

    public static void Resume()
    {
        IsPaused = false;
        Time.timeScale = 1f;
    }
}
