using UnityEngine;

/// <summary>
/// Persistent 场景中的界面语言开关。勾选 Use English 后显示英文，打包也以此为准。
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(-200)]
[AddComponentMenu("Lost Division/Game Language Settings")]
public class GameLanguageSettings : MonoBehaviour
{
    public GameLanguageSO language;

    [Tooltip("勾选后显示英文，取消则为中文。打包以此勾选为准。")]
    public bool useEnglish;

    void Awake()
    {
        Apply();
    }

    void OnEnable()
    {
        Apply();
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        Apply();
    }
#endif

    public void Apply()
    {
        if (language == null)
            return;

        language.SetUseEnglish(useEnglish);
#if UNITY_EDITOR
        if (!Application.isPlaying)
            UnityEditor.EditorUtility.SetDirty(language);
#endif
    }
}
