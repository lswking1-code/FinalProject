using System;
using System.Collections.Generic;
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
    readonly List<int> cycleIds = new List<int>(4);

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
        int cycleCount = weaponController != null
            ? weaponController.GetRuntimeCycleIds(cycleIds)
            : 0;

        bool pistolOnly = weaponId == 0
            && (cycleCount == 0 || (cycleCount == 1 && cycleIds[0] == 0));

        if (pistolOnly)
        {
            SetActive(normalGun, true);
            SetActive(normalGunNumber, true);
            SetActive(bulletSlot, false);
            SetActive(bulletNumber != null ? bulletNumber.gameObject : null, false);
            return;
        }

        SetActive(normalGun, false);
        SetActive(bulletSlot, true);

        ApplyIcon(bulletSlot1, weaponId);
        SetSlotVisible(bulletSlot1, true);

        ResolveForwardSlots(weaponId, out int nextId, out int nextNextId);
        bool showSlot2 = cycleCount >= 2 && nextId >= 0;
        bool showSlot3 = cycleCount >= 3 && nextNextId >= 0;
        SetSlotVisible(bulletSlot2, showSlot2);
        SetSlotVisible(bulletSlot3, showSlot3);
        if (showSlot2)
            ApplyIcon(bulletSlot2, nextId);
        if (showSlot3)
            ApplyIcon(bulletSlot3, nextNextId);

        bool showInfinity = weaponId == 0;
        SetActive(normalGunNumber, showInfinity);
        SetActive(bulletNumber != null ? bulletNumber.gameObject : null, !showInfinity);
        if (!showInfinity && bulletNumber != null)
            bulletNumber.text = GetAmmoCount(weaponId).ToString();
    }

    void ResolveForwardSlots(int currentId, out int nextId, out int nextNextId)
    {
        nextId = -1;
        nextNextId = -1;
        int n = cycleIds.Count;
        if (n == 0)
            return;

        int index = cycleIds.IndexOf(currentId);
        if (index < 0)
        {
            if (n >= 1)
                nextId = cycleIds[0];
            if (n >= 2)
                nextNextId = cycleIds[1];
            return;
        }

        if (n >= 2)
            nextId = cycleIds[(index + 1) % n];
        if (n >= 3)
            nextNextId = cycleIds[(index + 2) % n];
    }

    int GetAmmoCount(int weaponId)
    {
        if (character == null)
            return 0;
        return character.GetAmmoForWeapon(weaponId);
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

    void SetSlotVisible(Image slot, bool visible)
    {
        if (slot == null)
            return;

        var root = slot.transform.parent != null
            ? slot.transform.parent.gameObject
            : slot.gameObject;
        SetActive(root, visible);
    }

    static void SetActive(GameObject go, bool active)
    {
        if (go != null && go.activeSelf != active)
            go.SetActive(active);
    }
}
