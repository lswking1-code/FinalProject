using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 特殊弹夹 UI：在 LoadZone 下按 FIFO 生成/销毁 SpecialBulletS/M/L 图标。
/// </summary>
public class SpecialMagazineUI : MonoBehaviour
{
    [SerializeField] GameObject prefabS;
    [SerializeField] GameObject prefabM;
    [SerializeField] GameObject prefabL;

    readonly List<GameObject> icons = new List<GameObject>();
    SpecialMagazine boundMagazine;

    void OnDisable()
    {
        Unsubscribe(boundMagazine);
        boundMagazine = null;
    }

    void LateUpdate()
    {
        var found = FindFirstObjectByType<SpecialMagazine>();
        if (found == boundMagazine)
            return;

        Unsubscribe(boundMagazine);
        boundMagazine = found;
        Subscribe(boundMagazine);
        RebuildFromMagazine();
    }

    void Subscribe(SpecialMagazine magazine)
    {
        if (magazine == null)
            return;

        magazine.RoundLoaded += OnRoundLoaded;
        magazine.RoundConsumed += OnRoundConsumed;
    }

    void Unsubscribe(SpecialMagazine magazine)
    {
        if (magazine == null)
            return;

        magazine.RoundLoaded -= OnRoundLoaded;
        magazine.RoundConsumed -= OnRoundConsumed;
    }

    void RebuildFromMagazine()
    {
        ClearIcons();

        if (boundMagazine == null)
            return;

        foreach (var type in boundMagazine.EnumerateRounds())
            Spawn(type);
    }

    void OnRoundLoaded(SpecialAmmoType type) => Spawn(type);

    void OnRoundConsumed(SpecialAmmoType type)
    {
        if (icons.Count == 0)
            return;

        var icon = icons[0];
        icons.RemoveAt(0);
        if (icon != null)
            Destroy(icon);
    }

    void Spawn(SpecialAmmoType type)
    {
        GameObject prefab = ResolvePrefab(type);
        if (prefab == null)
            return;

        var instance = Instantiate(prefab, transform, false);
        icons.Add(instance);
    }

    GameObject ResolvePrefab(SpecialAmmoType type) => type switch
    {
        SpecialAmmoType.S => prefabS,
        SpecialAmmoType.M => prefabM,
        SpecialAmmoType.L => prefabL,
        _ => null,
    };

    void ClearIcons()
    {
        for (int i = 0; i < icons.Count; i++)
        {
            if (icons[i] != null)
                Destroy(icons[i]);
        }
        icons.Clear();

        // 清理场景中预览用的示例子物体（不在 icons 列表里）
        for (int i = transform.childCount - 1; i >= 0; i--)
            Destroy(transform.GetChild(i).gameObject);
    }
}
