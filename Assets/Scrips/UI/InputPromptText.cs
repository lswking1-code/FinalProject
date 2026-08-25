using TMPro;
using UnityEngine;

/// <summary>
/// 把带 {Action} / {Move/down} 占位符的引导文案渲染成按键图标混排。
/// 挂在任意 TMP_Text（世界空间背景板或 HUD）上。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(TMP_Text))]
public class InputPromptText : MonoBehaviour
{
    [TextArea(2, 6)]
    [SerializeField] string sourceText = "按 {Ability2} 召唤机器人";

    TMP_Text label;
    string lastRendered;

    public string SourceText
    {
        get => sourceText;
        set
        {
            sourceText = value;
            Refresh();
        }
    }

    void Awake() => CacheLabel();

    void OnEnable()
    {
        CacheLabel();
        AssignSpriteAssets();
        InputPromptDeviceTracker.Changed += Refresh;
        Refresh();
    }

    void OnDisable() => InputPromptDeviceTracker.Changed -= Refresh;

    void CacheLabel()
    {
        if (label == null)
            label = GetComponent<TMP_Text>();
    }

    public void Refresh()
    {
        CacheLabel();
        if (label == null)
            return;

        AssignSpriteAssets();
        string rendered = InputPromptFormatter.Format(sourceText);
        rendered = InputPromptFormatter.CollapseSpriteTagWhitespace(rendered);
        if (rendered == lastRendered && label.text == rendered)
            return;

        lastRendered = rendered;
        label.text = rendered;
    }

    void AssignSpriteAssets()
    {
        if (label == null)
            return;

        var keyIcons = Resources.Load<TMP_SpriteAsset>("Sprite Assets/KeyIcons");
        var gamepadIcons = Resources.Load<TMP_SpriteAsset>("Sprite Assets/GamepadIcons");
        if (keyIcons != null)
            label.spriteAsset = keyIcons;

        if (keyIcons != null && gamepadIcons != null)
        {
            if (keyIcons.fallbackSpriteAssets == null)
                keyIcons.fallbackSpriteAssets = new System.Collections.Generic.List<TMP_SpriteAsset>();
            if (!keyIcons.fallbackSpriteAssets.Contains(gamepadIcons))
                keyIcons.fallbackSpriteAssets.Add(gamepadIcons);
        }
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        if (!isActiveAndEnabled)
            return;
        Refresh();
    }
#endif
}
