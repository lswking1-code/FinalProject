using FMODUnity;
using UnityEngine;

internal static class GunnerAudio
{
    internal static readonly EventReference LaserHit = FmodAudio.Create(
        "{9347a416-dd28-4a8f-b523-70c2ad5d7f38}", "event:/Player/Gunner/LaserHit");
    internal static readonly EventReference MissileExplode = FmodAudio.Create(
        "{127faef9-6a5d-460d-87af-57b2f49ce516}", "event:/Player/Gunner/MissileExplode");

    // Return true even when throttled: the caller must not play its default hit sound.
    internal static bool TryPlayLaserHit(Attack attacker, Enemy target, Vector3 position)
    {
        var laser = attacker != null ? attacker.GetComponentInParent<PlayerLaserBeam>() : null;
        if (laser == null)
            return false;

        laser.PlayHitSfx(target.GetInstanceID(), position);
        return true;
    }
}
