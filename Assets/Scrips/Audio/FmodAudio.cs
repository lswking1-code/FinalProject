using FMOD.Studio;
using FMODUnity;
using UnityEngine;

public static class FmodAudio
{
    const float DefaultMinDistance = 8f;
    const float DefaultMaxDistance = 28f;
    const float DefaultPanRange = 16f;
    const float DefaultReferenceOrtho = 5f;
    const float SilentVolume = 0.001f;
    const int DefaultMaxWorldOneShotsPerFrame = 6;

    static Transform listenerCache;
    static int worldOneShotFrame = -1;
    static int worldOneShotsThisFrame;

    public static void Play(EventReference evt)
    {
        PlayInternal(evt, null, null, 0f, false, false, default);
    }

    public static void Play(EventReference evt, Vector3 worldPosition)
    {
        PlayInternal(evt, null, null, 0f, false, true, worldPosition);
    }

    public static void Play(EventReference evt, string paramName, string label)
    {
        PlayInternal(evt, paramName, label, 0f, false, false, default);
    }

    public static void Play(EventReference evt, string paramName, string label, Vector3 worldPosition)
    {
        PlayInternal(evt, paramName, label, 0f, false, true, worldPosition);
    }

    public static void Play(EventReference evt, string paramName, float value)
    {
        PlayInternal(evt, paramName, null, value, true, false, default);
    }

    public static void Play(EventReference evt, string paramName, float value, Vector3 worldPosition)
    {
        PlayInternal(evt, paramName, null, value, true, true, worldPosition);
    }

    public static EventInstance PlayHeld(EventReference evt)
    {
        return CreateStarted(evt, null, null, 0f, false, false, 1f, 0f);
    }

    public static void Stop(ref EventInstance instance)
    {
        Stop(ref instance, FMOD.Studio.STOP_MODE.IMMEDIATE);
    }

    public static void Stop(ref EventInstance instance, FMOD.Studio.STOP_MODE mode)
    {
        if (!instance.isValid())
        {
            instance.clearHandle();
            return;
        }

        instance.stop(mode);
        instance.release();
        instance.clearHandle();
    }

    static void PlayInternal(
        EventReference evt,
        string paramName,
        string label,
        float numericValue,
        bool hasNumeric,
        bool hasWorldPosition,
        Vector3 worldPosition)
    {
        float volume = 1f;
        float pan = 0f;
        if (hasWorldPosition && !TryEvaluateSpatial(worldPosition, out volume, out pan))
            return;
        if (hasWorldPosition && !TryConsumeWorldOneShotBudget())
            return;

        EventInstance instance = CreateStarted(
            evt,
            paramName,
            label,
            numericValue,
            hasNumeric,
            hasWorldPosition,
            volume,
            pan);
        if (!instance.isValid())
            return;

        instance.release();
    }

    static EventInstance CreateStarted(
        EventReference evt,
        string paramName,
        string label,
        float numericValue,
        bool hasNumeric,
        bool hasSpatial,
        float volume,
        float pan)
    {
        if (evt.IsNull)
            return default;

        try
        {
            EventInstance instance = RuntimeManager.CreateInstance(evt);
            if (!instance.isValid())
                return default;

            if (!string.IsNullOrEmpty(paramName))
            {
                if (!string.IsNullOrEmpty(label))
                    instance.setParameterByNameWithLabel(paramName, label);
                else if (hasNumeric)
                    instance.setParameterByName(paramName, numericValue);
            }

            if (hasSpatial)
                instance.setVolume(volume);

            instance.start();

            if (hasSpatial && Mathf.Abs(pan) > 0.001f)
                TrySetChannelPan(instance, pan);

            return instance;
        }
        catch (EventNotFoundException)
        {
            return default;
        }
    }

    static bool TryEvaluateSpatial(Vector3 worldPosition, out float volume, out float pan)
    {
        volume = 1f;
        pan = 0f;

        if (!TryGetListenerPosition(out Vector2 listener))
            return true;

        GetRange(out float minDistance, out float maxDistance, out float panRange, out bool enablePan, out AnimationCurve volumeCurve);

        if (maxDistance <= minDistance)
            maxDistance = minDistance + 0.01f;

        float distance = Vector2.Distance(listener, worldPosition);
        if (distance >= maxDistance)
            return false;

        float t = Mathf.InverseLerp(minDistance, maxDistance, distance);
        volume = EvaluateVolume(volumeCurve, t);
        if (volume <= SilentVolume)
            return false;

        if (enablePan && panRange > 0.01f)
            pan = Mathf.Clamp((worldPosition.x - listener.x) / panRange, -1f, 1f);

        return true;
    }

    static float EvaluateVolume(AnimationCurve volumeCurve, float t)
    {
        if (volumeCurve != null && volumeCurve.length > 0)
            return Mathf.Clamp01(volumeCurve.Evaluate(t));

        return 1f - Mathf.SmoothStep(0f, 1f, t);
    }

    static void GetRange(
        out float minDistance,
        out float maxDistance,
        out float panRange,
        out bool enablePan,
        out AnimationCurve volumeCurve)
    {
        FmodSfxDistanceSettings settings = FmodSfxDistanceSettings.Resolve();
        if (settings != null)
        {
            minDistance = settings.minDistance;
            maxDistance = settings.maxDistance;
            panRange = settings.panRange;
            enablePan = settings.enablePan;
            volumeCurve = settings.volumeCurve;
            if (settings.scaleWithCamera)
            {
                float scale = GetCameraRangeScale(settings.referenceOrthographicSize);
                minDistance *= scale;
                maxDistance *= scale;
                panRange *= scale;
            }

            return;
        }

        minDistance = DefaultMinDistance;
        maxDistance = DefaultMaxDistance;
        panRange = DefaultPanRange;
        enablePan = true;
        volumeCurve = null;

        float fallbackScale = GetCameraRangeScale(DefaultReferenceOrtho);
        minDistance *= fallbackScale;
        maxDistance *= fallbackScale;
        panRange *= fallbackScale;
    }

    static float GetCameraRangeScale(float referenceOrthographicSize)
    {
        if (referenceOrthographicSize <= 0.01f)
            return 1f;

        Camera cam = Camera.main;
        if (cam == null || !cam.orthographic)
            return 1f;

        return Mathf.Max(0.01f, cam.orthographicSize / referenceOrthographicSize);
    }

    static bool TryGetListenerPosition(out Vector2 position)
    {
        if (listenerCache == null)
        {
            GameObject player = GameObject.FindGameObjectWithTag("Player");
            if (player != null)
                listenerCache = player.transform;
        }

        if (listenerCache == null)
        {
            position = default;
            return false;
        }

        position = listenerCache.position;
        return true;
    }

    static bool TryConsumeWorldOneShotBudget()
    {
        int max = DefaultMaxWorldOneShotsPerFrame;
        FmodSfxDistanceSettings settings = FmodSfxDistanceSettings.Resolve();
        if (settings != null)
            max = settings.maxWorldOneShotsPerFrame;

        if (max <= 0)
            return true;

        int frame = Time.frameCount;
        if (worldOneShotFrame != frame)
        {
            worldOneShotFrame = frame;
            worldOneShotsThisFrame = 0;
        }

        if (worldOneShotsThisFrame >= max)
            return false;

        worldOneShotsThisFrame++;
        return true;
    }

    static bool TrySetChannelPan(EventInstance instance, float pan)
    {
        if (instance.getChannelGroup(out FMOD.ChannelGroup group) != FMOD.RESULT.OK)
            return false;

        return group.setPan(pan) == FMOD.RESULT.OK;
    }
}
