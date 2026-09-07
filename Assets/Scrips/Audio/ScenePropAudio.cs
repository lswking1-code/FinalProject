using FMODUnity;

/// <summary>
/// 场景机关共用 FMOD：受击 hit_normal、破坏 explode_01。
/// Inspector 未赋值时走 GUID fallback，覆盖场景内未挂 prefab 的实例。
/// </summary>
public static class ScenePropAudio
{
    public static readonly EventReference FallbackHitNormal = Create(
        "{60e880cc-78e0-4433-9db5-a9f4aa57ed57}",
        "event:/Enemy/hit_normal");

    public static readonly EventReference FallbackExplode01 = Create(
        "{49332bbd-52af-4d5a-b699-e0701ebb7447}",
        "event:/Enemy/explode_01");

    public static void PlayHitNormal(EventReference evt)
        => FmodAudio.Play(evt.IsNull ? FallbackHitNormal : evt);

    public static void PlayExplode01(EventReference evt)
        => FmodAudio.Play(evt.IsNull ? FallbackExplode01 : evt);

    static EventReference Create(string guid, string path)
    {
        return new EventReference
        {
            Guid = FMOD.GUID.Parse(guid),
            Path = path,
        };
    }
}
