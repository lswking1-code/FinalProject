using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Events;
using Object = UnityEngine.Object;

/// <summary>在 Play Mode 的临时物体上验证，不修改场景或磁盘存档。</summary>
public static class ChargeDoorSaveChecks
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    [MenuItem("Tools/Save/验证充能门存档", true)]
    static bool CanRun() => EditorApplication.isPlaying;

    [MenuItem("Tools/Save/验证充能门存档")]
    public static void Run()
    {
        if (!EditorApplication.isPlaying)
            throw new InvalidOperationException("Run in Play Mode");
        var root = new GameObject("ChargeDoorSaveCheck");
        root.transform.position = new Vector3(10000, 10000, 0);
        try
        {
            var nodeObject = new GameObject("Node");
            nodeObject.transform.SetParent(root.transform, false);
            var node = nodeObject.AddComponent<EnergyNode>();
            var gateObject = new GameObject("Gate");
            gateObject.transform.SetParent(root.transform, false);
            var collider = gateObject.AddComponent<BoxCollider2D>();
            var renderer = gateObject.AddComponent<SpriteRenderer>();
            gateObject.AddComponent<AnimatedDestroy>();
            var gate = gateObject.AddComponent<BoundDevice>();
            Set(gate, "nodes", new[] { node });
            var events = new UnityEvent();
            int eventCount = 0;
            events.AddListener(() => eventCount++);
            Set(gate, "onActivated", events);

            var closed = new Data();
            gate.GetSaveData(closed);
            node.RestoreChargeState(3.5f, false);
            var partial = new Data();
            gate.GetSaveData(partial);
            gate.LoadSaveData(closed);
            Check(!node.IsCharged && collider.enabled, "Closed checkpoint restores uncharged node and collision");
            gate.LoadSaveData(partial);
            Check(node.IsCharged && !node.IsHeld && Mathf.Approximately(node.RemainingChargeTime, 3.5f), "Partial charge survives round trip");

            Set(gate, "pendingActivate", true);
            node.HoldCharged();
            var pending = new Data();
            gate.GetSaveData(pending);
            gate.LoadSaveData(closed);
            gate.LoadSaveData(pending);
            Check(gate.IsPendingActivate && !gate.IsPermanentlyActive && node.IsHeld && collider.enabled, "Pending checkpoint stays closed and holds charge");

            typeof(BoundDevice).GetMethod("LockActivate", Private).Invoke(gate, null);
            var open = new Data();
            gate.GetSaveData(open);
            gate.LoadSaveData(closed);
            Check(!gate.IsPermanentlyActive && collider.enabled && renderer.enabled, "Loading closed checkpoint rolls back opening animation");
            gate.LoadSaveData(open);
            gate.LoadSaveData(open);
            Check(gate.IsPermanentlyActive && !collider.enabled && !renderer.enabled && node.IsHeld, "Open checkpoint restores passage silently");
            Check(eventCount == 1, "Repeated loads must not replay activation events");
            gate.LoadSaveData(new Data());
            Check(!gate.IsPermanentlyActive && !gate.IsPendingActivate && !node.IsCharged && collider.enabled && renderer.enabled, "Old saves restore initial state");
            Debug.Log("ChargeDoorSaveChecks: all checks passed.");
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    static void Set(object target, string field, object value) =>
        target.GetType().GetField(field, Private).SetValue(target, value);

    static void Check(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
