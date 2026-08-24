using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 近战索敌传感器：只查询敌人，不参与物理接触，因此不会引爆导弹、也不会替玩家挨打。
/// </summary>
[RequireComponent(typeof(BoxCollider2D))]
public class MeleeDetectZone : MonoBehaviour
{
    readonly HashSet<Transform> targets = new();

    BoxCollider2D boxCollider;
    int lastRefreshFrame = -1;

    public bool HasValidTarget
    {
        get
        {
            RefreshTargetsFromOverlap();
            return targets.Count > 0;
        }
    }

    public static bool IsSensorCollider(Collider2D collider)
        => collider != null && collider.GetComponent<MeleeDetectZone>() != null;

    void Awake()
    {
        boxCollider = GetComponent<BoxCollider2D>();
        boxCollider.isTrigger = true;
        // 传感器：不产生物理接触 / 回调，避免被导弹和敌方攻击当成玩家身体
        boxCollider.includeLayers = 0;
        boxCollider.excludeLayers = ~0;
        boxCollider.callbackLayers = 0;
        boxCollider.contactCaptureLayers = 0;
    }

    void LateUpdate() => PruneInvalidTargets();

    void OnTriggerEnter2D(Collider2D other)
    {
        if (TryGetTargetRoot(other, out Transform root))
            targets.Add(root);
    }

    void OnTriggerExit2D(Collider2D other)
    {
        if (TryGetTargetRoot(other, out Transform root))
            targets.Remove(root);
    }

    public Transform GetNearestTarget(Vector2 from)
    {
        RefreshTargetsFromOverlap();

        Transform nearest = null;
        float nearestDist = float.MaxValue;

        foreach (var target in targets)
        {
            if (target == null)
                continue;

            float dist = Vector2.Distance(from, target.position);
            if (dist < nearestDist)
            {
                nearestDist = dist;
                nearest = target;
            }
        }

        return nearest;
    }

    void RefreshTargetsFromOverlap()
    {
        if (lastRefreshFrame == Time.frameCount)
            return;

        lastRefreshFrame = Time.frameCount;
        targets.Clear();

        if (boxCollider == null)
            boxCollider = GetComponent<BoxCollider2D>();
        if (boxCollider == null || !boxCollider.enabled)
            return;

        Vector2 center = boxCollider.bounds.center;
        Vector2 size = boxCollider.bounds.size;
        var hits = Physics2D.OverlapBoxAll(center, size, 0f);
        if (hits == null)
            return;

        for (int i = 0; i < hits.Length; i++)
        {
            if (!TryGetTargetRoot(hits[i], out Transform root))
                continue;
            targets.Add(root);
        }
    }

    void PruneInvalidTargets()
    {
        if (targets.Count == 0)
            return;

        targets.RemoveWhere(target => target == null || !IsAliveEnemy(target));
    }

    bool TryGetTargetRoot(Collider2D other, out Transform root)
    {
        root = null;
        if (other == null)
            return false;

        if (boxCollider != null && other == boxCollider)
            return false;

        var enemy = other.GetComponentInParent<Enemy>();
        if (enemy == null || !enemy.IsHittable)
            return false;

        root = enemy.transform;
        return true;
    }

    static bool IsAliveEnemy(Transform target)
    {
        if (target == null)
            return false;

        var enemy = target.GetComponent<Enemy>();
        return enemy != null && enemy.IsHittable;
    }

    void OnDrawGizmosSelected()
    {
        var collider = boxCollider != null ? boxCollider : GetComponent<BoxCollider2D>();
        if (collider == null)
            return;

        Gizmos.color = new Color(1f, 0.85f, 0.2f, 0.35f);
        var matrix = transform.localToWorldMatrix;
        Gizmos.matrix = matrix;
        Gizmos.DrawCube(collider.offset, collider.size);
    }
}
