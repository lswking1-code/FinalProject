using FMODUnity;

/// <summary>
/// 补给 / 存档等 Others 文件夹 FMOD。Inspector 未赋值时走 GUID fallback。
/// </summary>
public static class OtherSfx
{
    public static readonly EventReference FallbackAmmoGet = Create(
        "{f3ae73c8-f69c-4299-b23e-bccfcf27fb7c}",
        "event:/Others/ammo_get");

    public static readonly EventReference FallbackSavepoint = Create(
        "{73463cba-fd11-4cb0-8157-18e80405b6ed}",
        "event:/Others/savepoint");

    public static readonly EventReference FallbackTransition = Create(
        "{8e74ce85-f438-4ac7-a12b-2d6d6cda4fed}",
        "event:/Others/transition");

    public static void PlayAmmoGet(EventReference evt)
        => FmodAudio.Play(evt.IsNull ? FallbackAmmoGet : evt);

    public static void PlaySavepoint(EventReference evt)
        => FmodAudio.Play(evt.IsNull ? FallbackSavepoint : evt);

    public static void PlayTransition(EventReference evt)
        => FmodAudio.Play(evt.IsNull ? FallbackTransition : evt);

    static EventReference Create(string guid, string path)
        => FmodAudio.Create(guid, path);
}
