using TMPro;
using UnityEngine;

/// <summary>
/// 在 LifeNum 上显示生命点数量（如 X5）。
/// </summary>
public class LifePointUI : MonoBehaviour
{
    [SerializeField] TMP_Text lifeNum;

    PlayerLifePoints bound;

    void Awake()
    {
        if (lifeNum == null)
            lifeNum = FindLifeNum();
    }

    void OnEnable()
    {
        TryBind();
    }

    void OnDisable()
    {
        Unsubscribe(bound);
        bound = null;
    }

    void LateUpdate()
    {
        if (bound == PlayerLifePoints.Instance)
            return;

        TryBind();
    }

    void TryBind()
    {
        var found = PlayerLifePoints.Instance;
        if (found == bound)
        {
            if (bound != null)
                Refresh(bound.Current);
            return;
        }

        Unsubscribe(bound);
        bound = found;
        Subscribe(bound);
        Refresh(bound != null ? bound.Current : 0);
    }

    void Subscribe(PlayerLifePoints source)
    {
        if (source == null)
            return;

        source.Changed += Refresh;
    }

    void Unsubscribe(PlayerLifePoints source)
    {
        if (source == null)
            return;

        source.Changed -= Refresh;
    }

    void Refresh(int count)
    {
        if (lifeNum == null)
            lifeNum = FindLifeNum();
        if (lifeNum == null)
            return;

        lifeNum.text = $"X{Mathf.Max(0, count)}";
    }

    TMP_Text FindLifeNum()
    {
        var texts = GetComponentsInChildren<TMP_Text>(true);
        for (int i = 0; i < texts.Length; i++)
        {
            if (texts[i] != null && texts[i].name == "LifeNum")
                return texts[i];
        }

        return texts.Length > 0 ? texts[0] : null;
    }
}
