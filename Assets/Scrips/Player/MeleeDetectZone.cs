using System.Collections.Generic;
using UnityEngine;

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

    void Awake()
    {
        boxCollider = GetComponent<BoxCollider2D>();
        boxCollider.isTrigger = true;
    }

    void LateUpdate() => PruneInvalidTargets();

    void OnTriggerEnter2D(Collider2D other)
    {
        bool accepted = TryGetTargetRoot(other, out Transform root);
        if (accepted)
            targets.Add(root);
    }

    void OnTriggerExit2D(Collider2D other)
    {
        bool accepted = TryGetTargetRoot(other, out Transform root);
        if (accepted)
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

        targets.RemoveWhere(target => target == null || !IsAliveTarget(target));
    }

    bool TryGetTargetRoot(Collider2D other, out Transform root)
    {
        root = null;
        if (other == null)
            return false;

        if (boxCollider != null && other == boxCollider)
            return false;

        var selfBody = boxCollider != null ? boxCollider.attachedRigidbody : null;
        if (selfBody != null && other.attachedRigidbody == selfBody)
            return false;

        if (other.CompareTag("Player"))
            return false;

        if (!other.CompareTag("Enemy"))
        {
            var character = other.GetComponentInParent<Character>();
            if (character == null)
                return false;

            root = character.transform;
            return IsAliveTarget(root);
        }

        var enemy = other.GetComponentInParent<Enemy>();
        root = enemy != null ? enemy.transform : other.transform;
        return IsAliveTarget(root);
    }

    static bool IsAliveTarget(Transform target)
    {
        if (target == null)
            return false;

        var enemy = target.GetComponent<Enemy>();
        if (enemy != null)
            return enemy.IsHittable;

        var character = target.GetComponent<Character>();
        if (character != null && !character.CanReceiveHits)
            return false;

        return true;
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
