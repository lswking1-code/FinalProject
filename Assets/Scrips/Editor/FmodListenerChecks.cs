using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public static class FmodListenerChecks
{
    [MenuItem("Tools/Audio/验证切换角色后的受击音效距离")]
    public static void Run()
    {
        const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
        var cache = typeof(FmodAudio).GetField("listenerCache", flags);
        var resolve = typeof(FmodAudio).GetMethod("TryGetListenerPosition", flags);
        var evaluate = typeof(FmodAudio).GetMethod("TryEvaluateSpatial", flags);
        object previousCache = cache.GetValue(null);
        var activePlayer = GameObject.FindGameObjectWithTag("Player");
        bool ownsPlayer = activePlayer == null;
        if (ownsPlayer)
        {
            activePlayer = new GameObject("AudioCheckActivePlayer");
            activePlayer.tag = "Player";
        }
        var oldPlayer = new GameObject("AudioCheckInactivePlayer");
        try
        {
            oldPlayer.transform.position = activePlayer.transform.position + Vector3.right * 100000f;
            cache.SetValue(null, oldPlayer.transform);
            object[] spatial = { activePlayer.transform.position, 0f, 0f };
            Check(!(bool)evaluate.Invoke(null, spatial), "Distant cached player reproduces silent hit sounds");
            oldPlayer.SetActive(false);
            object[] position = { Vector2.zero };
            Check((bool)resolve.Invoke(null, position), "Must find an active player after character switch");
            Check((Vector2)position[0] == (Vector2)activePlayer.transform.position, "Must discard the inactive player position");
            spatial = new object[] { activePlayer.transform.position, 0f, 0f };
            Check((bool)evaluate.Invoke(null, spatial) && Mathf.Approximately((float)spatial[1], 1f),
                "Nearby hit must be audible after invalidating old character cache");
            Transform destroyedTransform = oldPlayer.transform;
            Object.DestroyImmediate(oldPlayer);
            cache.SetValue(null, destroyedTransform);
            position = new object[] { Vector2.zero };
            Check((bool)resolve.Invoke(null, position), "Destroyed cache must also reacquire a player");
            Debug.Log("FmodListenerChecks: all checks passed.");
        }
        finally
        {
            cache.SetValue(null, previousCache);
            if (oldPlayer != null) Object.DestroyImmediate(oldPlayer);
            if (ownsPlayer) Object.DestroyImmediate(activePlayer);
        }
    }

    static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
