using UnityEngine;

public class BulletBox : MonoBehaviour
{
    [SerializeField] AmmoType ammoType = AmmoType.S;
    [SerializeField] int amount = 1;

    void OnTriggerEnter2D(Collider2D other) => TryPickup(other);

    void OnTriggerStay2D(Collider2D other) => TryPickup(other);

    void TryPickup(Collider2D other)
    {
        if (!other.CompareTag("Player"))
            return;

        var character = other.GetComponent<Character>()
            ?? other.GetComponentInParent<Character>();
        if (character == null)
            return;

        var weaponController = character.GetComponent<PlayerWeaponController>()
            ?? character.GetComponentInParent<PlayerWeaponController>();

        bool shouldAutoSwitch = weaponController != null
            && weaponController.CurrentWeaponId == 0
            && character.BulletS == 0
            && character.BulletM == 0
            && character.BulletL == 0;

        if (!character.AddAmmo(ammoType, amount))
            return;

        if (shouldAutoSwitch)
            weaponController.TrySwitchTo(AmmoTypeToWeaponId(ammoType));

        Destroy(gameObject);
    }

    static int AmmoTypeToWeaponId(AmmoType type) => type switch
    {
        AmmoType.S => 1,
        AmmoType.M => 2,
        AmmoType.L => 3,
        _ => 0,
    };
}
