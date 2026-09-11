using UnityEngine;

/// <summary>
/// 玩家暂停：用 Time.timeScale 可逆冻结玩法。切场景前必须 Resume，否则淡入淡出会卡住。
/// 解除暂停后短时间继续挡住玩法输入，避免菜单确认/Start 同帧漏成跳跃等动作。
/// </summary>
public static class GameplayPause
{
    const float ResumeInputSuppress = 0.15f;

    static float suppressGameplayInputUntil = float.NegativeInfinity;

    public static bool IsPaused { get; private set; }

    public static bool SuppressesGameplayInput =>
        IsPaused || Time.unscaledTime < suppressGameplayInputUntil;

    public static void Pause()
    {
        IsPaused = true;
        Time.timeScale = 0f;
    }

    public static void Resume()
    {
        bool wasPaused = IsPaused;
        IsPaused = false;
        Time.timeScale = 1f;
        if (wasPaused)
            suppressGameplayInputUntil = Time.unscaledTime + ResumeInputSuppress;
    }
}
