using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class BulletUI : MonoBehaviour
{
    [Serializable]
    class WeaponIconEntry
    {
        public int weaponId;
        public Sprite icon;
    }

    [SerializeField] GameObject normalGun;
    [SerializeField] GameObject normalGunNumber;
    [SerializeField] GameObject bulletSlot;
    [SerializeField] Image bulletSlot1;
    [SerializeField] Image bulletSlot2;
    [SerializeField] Image bulletSlot3;
    [SerializeField] TMP_Text bulletNumber;
    [SerializeField] WeaponIconEntry[] weaponIcons;

    Character character;
    PlayerWeaponController weaponController;

    public void OnCharacterChange(Character c)
    {
        character = c;
        weaponController = c != null ? c.GetComponent<PlayerWeaponController>() : null;
    }

    void LateUpdate()
    {
        if (weaponController == null)
            TryBindActivePlayer();

        Refresh();
    }

    void TryBindActivePlayer()
    {
        var found = FindFirstObjectByType<PlayerWeaponController>();
        if (found == null || !found.isActiveAndEnabled)
            return;

        OnCharacterChange(found.GetComponent<Character>());
    }

    void Refresh()
    {
        int weaponId = weaponController != null ? weaponController.CurrentWeaponId : 0;

        if (weaponId == 0)
        {
            SetActive(normalGun, true);
            SetActive(normalGunNumber, true);
            SetActive(bulletSlot, false);
            SetActive(bulletNumber != null ? bulletNumber.gameObject : null, false);
            return;
        }

        SetActive(normalGun, false);
        SetActive(normalGunNumber, false);
        SetActive(bulletSlot, true);
        SetActive(bulletNumber != null ? bulletNumber.gameObject : null, true);

        int prevId = weaponId;
        int nextId = weaponId;
        if (weaponController != null)
            weaponController.TryGetCycleNeighbors(weaponId, out prevId, out nextId);

        ApplyIcon(bulletSlot1, prevId);
        ApplyIcon(bulletSlot2, weaponId);
        ApplyIcon(bulletSlot3, nextId);

        if (bulletNumber != null)
            bulletNumber.text = GetAmmoCount(weaponId).ToString();
    }

    int GetAmmoCount(int weaponId)
    {
        if (character == null)
            return 0;

        return weaponId switch
        {
            1 => character.BulletS,
            2 => character.BulletM,
            3 => character.BulletL,
            _ => 0,
        };
    }

    void ApplyIcon(Image target, int weaponId)
    {
        if (target == null)
            return;

        target.sprite = FindIcon(weaponId);
    }

    Sprite FindIcon(int weaponId)
    {
        if (weaponIcons == null)
            return null;

        for (int i = 0; i < weaponIcons.Length; i++)
        {
            var entry = weaponIcons[i];
            if (entry != null && entry.weaponId == weaponId)
                return entry.icon;
        }

        return null;
    }

    static void SetActive(GameObject go, bool active)
    {
        if (go != null && go.activeSelf != active)
            go.SetActive(active);
    }
}
