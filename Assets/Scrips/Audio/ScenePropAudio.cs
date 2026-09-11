using FMODUnity;
using UnityEngine;

/// <summary>
/// 场景机关共用 FMOD：受击 hit_normal、破坏 explode_01，以及子弹打墙/打盾 fallback。
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

    public static readonly EventReference FallbackArmorBullet = Create(
        "{b73df57c-a407-4a73-8053-d7eef36097c6}",
        "event:/Enemy/hit_armor_bullet");

    public static readonly EventReference FallbackShieldBullet = Create(
        "{e7aad2c0-82f1-4827-8dd1-8614534d5c17}",
        "event:/Enemy/hit_shield_bullet");

    public static void PlayHitNormal(EventReference evt, Vector3 worldPosition)
        => FmodAudio.Play(evt.IsNull ? FallbackHitNormal : evt, worldPosition);

    public static void PlayExplode01(EventReference evt, Vector3 worldPosition)
        => FmodAudio.Play(evt.IsNull ? FallbackExplode01 : evt, worldPosition);

    public static void PlayBulletSurface(Vector3 worldPosition)
        => FmodAudio.Play(FallbackArmorBullet, worldPosition);

    public static void PlayBulletShield(Vector3 worldPosition)
        => FmodAudio.Play(FallbackShieldBullet, worldPosition);

    static EventReference Create(string guid, string path)
        => FmodAudio.Create(guid, path);
}
