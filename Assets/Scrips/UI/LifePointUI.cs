using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 在 LifePoint 下按生命点数量生成 / 销毁 Life 图标。
/// </summary>
public class LifePointUI : MonoBehaviour
{
    [SerializeField] GameObject lifePrefab;

    readonly List<GameObject> icons = new List<GameObject>();
    PlayerLifePoints bound;

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
            if (bound != null && icons.Count != bound.Current)
                Rebuild(bound.Current);
            return;
        }

        Unsubscribe(bound);
        bound = found;
        Subscribe(bound);
        Rebuild(bound != null ? bound.Current : 0);
    }

    void Subscribe(PlayerLifePoints source)
    {
        if (source == null)
            return;

        source.Changed += Rebuild;
    }

    void Unsubscribe(PlayerLifePoints source)
    {
        if (source == null)
            return;

        source.Changed -= Rebuild;
    }

    void Rebuild(int count)
    {
        ClearPreviewChildren();

        if (lifePrefab == null)
            return;

        count = Mathf.Max(0, count);

        while (icons.Count > count)
        {
            int last = icons.Count - 1;
            if (icons[last] != null)
                Destroy(icons[last]);
            icons.RemoveAt(last);
        }

        while (icons.Count < count)
        {
            var instance = Instantiate(lifePrefab, transform, false);
            instance.name = "Life";
            icons.Add(instance);
        }
    }

    void ClearPreviewChildren()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            var child = transform.GetChild(i).gameObject;
            if (icons.Contains(child))
                continue;

            Destroy(child);
        }
    }
}
