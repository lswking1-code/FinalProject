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
        if (evt.IsNull)
            return;

        try
        {
            EventInstance instance = RuntimeManager.CreateInstance(evt);
            if (!instance.isValid())
                return;

            if (!string.IsNullOrEmpty(paramName) && !string.IsNullOrEmpty(label))
                instance.setParameterByNameWithLabel(paramName, label);

            instance.start();
            instance.release();
        }
        catch (EventNotFoundException)
        {
        }
    }
}
