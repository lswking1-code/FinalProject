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
        EventInstance instance = CreateStarted(evt, paramName, label);
        if (!instance.isValid())
            return;

        instance.release();
    }

    public static EventInstance PlayHeld(EventReference evt)
    {
        return CreateStarted(evt, null, null);
    }

    public static void Stop(ref EventInstance instance)
    {
        if (!instance.isValid())
        {
            instance.clearHandle();
            return;
        }

        instance.stop(FMOD.Studio.STOP_MODE.IMMEDIATE);
        instance.release();
        instance.clearHandle();
    }

    static EventInstance CreateStarted(EventReference evt, string paramName, string label)
    {
        if (evt.IsNull)
            return default;

        try
        {
            EventInstance instance = RuntimeManager.CreateInstance(evt);
            if (!instance.isValid())
                return default;

            if (!string.IsNullOrEmpty(paramName) && !string.IsNullOrEmpty(label))
                instance.setParameterByNameWithLabel(paramName, label);

            instance.start();
            return instance;
        }
        catch (EventNotFoundException)
        {
            return default;
        }
    }
}
