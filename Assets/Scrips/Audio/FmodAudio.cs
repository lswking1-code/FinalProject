using FMOD.Studio;
using FMODUnity;

public static class FmodAudio
{
    public static void Play(EventReference evt)
    {
        Play(evt, null, null);
    }

    public static void Play(EventReference evt, string paramName, string label)
    {
        EventInstance instance = CreateStarted(evt, paramName, label, 0f, false);
        if (!instance.isValid())
            return;

        instance.release();
    }

    public static void Play(EventReference evt, string paramName, float value)
    {
        EventInstance instance = CreateStarted(evt, paramName, null, value, true);
        if (!instance.isValid())
            return;

        instance.release();
    }

    public static EventInstance PlayHeld(EventReference evt)
    {
        return CreateStarted(evt, null, null, 0f, false);
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

    static EventInstance CreateStarted(
        EventReference evt,
        string paramName,
        string label,
        float numericValue,
        bool hasNumeric)
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

            instance.start();
            return instance;
        }
        catch (EventNotFoundException)
        {
            return default;
        }
    }
}
