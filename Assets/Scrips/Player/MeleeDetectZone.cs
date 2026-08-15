using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(BoxCollider2D))]
public class MeleeDetectZone : MonoBehaviour
{
    readonly HashSet<Transform> targets = new();

    BoxCollider2D boxCollider;

    public bool HasValidTarget
    {
        get
        {
            PruneInvalidTargets();
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
        if (!TryGetTargetRoot(other, out Transform root))
            return;

        targets.Add(root);
    }

    void OnTriggerExit2D(Collider2D other)
    {
        if (!TryGetTargetRoot(other, out Transform root))
            return;

        targets.Remove(root);
    }

    public Transform GetNearestTarget(Vector2 from)
    {
        PruneInvalidTargets();

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

    void PruneInvalidTargets()
    {
        if (targets.Count == 0)
            return;

        targets.RemoveWhere(target => target == null || !IsAliveTarget(target));
    }

    static bool TryGetTargetRoot(Collider2D other, out Transform root)
    {
        root = null;
        if (other == null)
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
