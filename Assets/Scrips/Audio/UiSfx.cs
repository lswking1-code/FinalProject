using FMODUnity;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// UI 点击音效：所有 Button.onClick（含键盘/手柄 Invoke）播 button_press_01。
/// </summary>
public static class UiSfx
{
    public static readonly EventReference FallbackButtonPress01 = new EventReference
    {
        Guid = FMOD.GUID.Parse("{d54cf69e-bec2-4758-bd9b-26893e54bc2f}"),
        Path = "event:/UI/button_press_01",
    };

    static readonly UnityAction PlayClick = PlayButtonPress;

    public static void PlayButtonPress()
        => FmodAudio.Play(FallbackButtonPress01);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        HookAllButtons();
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => HookAllButtons();

    static void HookAllButtons()
    {
        var buttons = Object.FindObjectsByType<Button>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < buttons.Length; i++)
        {
            var button = buttons[i];
            if (button == null)
                continue;

            button.onClick.RemoveListener(PlayClick);
            button.onClick.AddListener(PlayClick);
        }
    }
}
