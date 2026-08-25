using UnityEngine;

/// <summary>
/// 教学关中英文引导切换：勾选 Use English 后只显示 GuideEN。
/// </summary>
[ExecuteAlways]
public class TutorialGuideLanguage : MonoBehaviour
{
    public GameLanguageSO language;
    [Tooltip("勾选后显示英文引导 GuideEN")]
    public bool useEnglish;
    public GameObject guideCN;
    public GameObject guideEN;

    void Awake()
    {
        SyncFromAsset();
        Apply();
    }

    void OnEnable()
    {
        SyncFromAsset();
        Apply();
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        if (language != null)
            language.useEnglish = useEnglish;

        UnityEditor.EditorApplication.delayCall += ApplyIfAlive;
    }

    void ApplyIfAlive()
    {
        if (this == null)
            return;
        Apply();
    }
#endif

    void SyncFromAsset()
    {
        if (language != null)
            useEnglish = language.useEnglish;
    }

    public void Apply()
    {
        bool english = language != null ? language.useEnglish : useEnglish;
        useEnglish = english;

        if (guideCN == null || guideEN == null)
            AutoFind();

        if (guideCN != null && guideCN.activeSelf == english)
            guideCN.SetActive(!english);
        if (guideEN != null && guideEN.activeSelf != english)
            guideEN.SetActive(english);

        RefreshPrompts(english ? guideEN : guideCN);
    }

    void AutoFind()
    {
        var scene = gameObject.scene;
        if (!scene.IsValid())
            return;

        var roots = scene.GetRootGameObjects();
        for (int i = 0; i < roots.Length; i++)
        {
            var root = roots[i];
            if (root == null)
                continue;
            if (guideCN == null && root.name == "GuideCN")
                guideCN = root;
            else if (guideEN == null && root.name == "GuideEN")
                guideEN = root;
        }
    }

    static void RefreshPrompts(GameObject root)
    {
        if (root == null)
            return;

        var prompts = root.GetComponentsInChildren<InputPromptText>(true);
        for (int i = 0; i < prompts.Length; i++)
        {
            if (prompts[i] != null)
                prompts[i].Refresh();
        }
    }
}
