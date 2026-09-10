using System;
using FMOD.Studio;
using FMODUnity;
using UnityEngine;

public class BgmManager : MonoBehaviour
{
    [Serializable]
    public class SceneBgm
    {
        public GameSceneSO scene;
        public EventReference bgmEvent;
    }

    [SerializeField] SceneBgm[] sceneTracks = Array.Empty<SceneBgm>();
    [SerializeField] EventReference menuEvent;

    static readonly EventReference FallbackMenu = FmodAudio.Create(
        "{54c7770b-4859-415c-be27-5c351b3c4d80}",
        "event:/Music/Menu");

    EventInstance currentInstance;
    EventReference currentEvent;

    public void PlayForScene(GameSceneSO scene)
    {
        EventReference next = FindEvent(scene);
        if (next.IsNull)
        {
            StopCurrent();
            return;
        }

        if (currentInstance.isValid() && currentEvent.Guid.Equals(next.Guid))
            return;

        StopCurrent();
        currentInstance = FmodAudio.PlayHeld(next);
        currentEvent = currentInstance.isValid() ? next : default;
    }

    public void StopCurrent()
    {
        FmodAudio.Stop(ref currentInstance, FMOD.Studio.STOP_MODE.ALLOWFADEOUT);
        currentEvent = default;
    }

    void OnDestroy()
    {
        FmodAudio.Stop(ref currentInstance, FMOD.Studio.STOP_MODE.IMMEDIATE);
        currentEvent = default;
    }

    EventReference FindEvent(GameSceneSO scene)
    {
        if (scene == null)
            return default;

        foreach (var track in sceneTracks)
        {
            if (IsSameScene(track.scene, scene))
                return track.bgmEvent;
        }

        if (scene.sceneType == SceneType.Menu)
            return menuEvent.IsNull ? FallbackMenu : menuEvent;

        return default;
    }

    static bool IsSameScene(GameSceneSO a, GameSceneSO b)
    {
        if (a == null || b == null)
            return false;

        if (a == b)
            return true;

        string guidA = a.sceneReference != null ? a.sceneReference.AssetGUID : null;
        string guidB = b.sceneReference != null ? b.sceneReference.AssetGUID : null;
        return !string.IsNullOrEmpty(guidA) && guidA == guidB;
    }
}
