using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

[DefaultExecutionOrder(50)]
[RequireComponent(typeof(PlayerAnimBase))]
[RequireComponent(typeof(PlayerMovement))]
[RequireComponent(typeof(DataDefination))]
public class PlayerWeaponController : MonoBehaviour, ISaveable
{
    const string WeaponIdKeySuffix = "weaponId";

    [SerializeField] WeaponDefinition[] weapons;
    [SerializeField] int initialWeaponId = 0;
    [SerializeField] float holdToInitialDuration = 0.4f;
    [SerializeField] int currentWeaponId;

    InputSystem_Actions actions;
    PlayerAnimBase playerAnim;
    PlayerMovement playerMovement;
    Character character;

    float prevHoldTime;
    float nextHoldTime;
    bool prevLongFired;
    bool nextLongFired;
    bool prevWasPressed;
    bool nextWasPressed;

    public int CurrentWeaponId => currentWeaponId;
    public int InitialWeaponId => initialWeaponId;

    public WeaponDefinition CurrentDefinition => GetDefinition(currentWeaponId);

    void Awake()
    {
        actions = new InputSystem_Actions();
        playerAnim = PlayerAnimBase.Resolve(gameObject);
        playerMovement = GetComponent<PlayerMovement>();
        character = GetComponent<Character>();
        currentWeaponId = initialWeaponId;
    }

    void Start()
    {
        var def = GetDefinition(currentWeaponId);
        if (def != null && playerAnim != null)
            playerAnim.ApplyWeaponDefinition(def);

        ReconcileCurrentWeapon();
    }

    void OnEnable()
    {
        actions.Player.Enable();
        ((ISaveable)this).RegisterSaveData();
    }

    void OnDisable()
    {
        actions.Player.Disable();
        ((ISaveable)this).UnregisterSaveData();
    }

    void OnDestroy() => actions?.Dispose();

    void Update()
    {
        if (playerMovement != null && playerMovement.IsActionLocked)
            return;

        // 死亡外随时可换枪：翻滚/切枪中等由 TryPlayWeaponSwitchAnim 决定是否播切枪动画
        if (playerAnim != null && playerAnim.IsDead)
            return;

        HandleWeaponInput(actions.Player.Previous, ref prevWasPressed, ref prevHoldTime, ref prevLongFired, -1);
        HandleWeaponInput(actions.Player.Next, ref nextWasPressed, ref nextHoldTime, ref nextLongFired, +1);
    }

    void HandleWeaponInput(
        InputAction action,
        ref bool wasPressed,
        ref float holdTime,
        ref bool longFired,
        int cycleDir)
    {
        bool pressed = action.IsPressed();

        if (pressed && !wasPressed)
        {
            holdTime = 0f;
            longFired = false;
        }

        if (pressed)
        {
            holdTime += Time.deltaTime;
            if (!longFired && holdTime >= holdToInitialDuration)
            {
                longFired = true;
                TrySwitchTo(initialWeaponId);
            }
        }
        else if (wasPressed && !longFired)
        {
            TryCycle(cycleDir);
        }

        wasPressed = pressed;
    }

    void TryCycle(int dir)
    {
        if (weapons == null || weapons.Length == 0)
            return;

        int count = weapons.Length;
        int index = FindIndexByWeaponId(currentWeaponId);
        if (index < 0)
            index = 0;

        for (int step = 1; step <= count; step++)
        {
            int nextIndex = ((index + dir * step) % count + count) % count;
            var def = weapons[nextIndex];
            if (!IsInRuntimeCycle(def))
                continue;
            if (def.weaponId == currentWeaponId)
                return;
            TrySwitchTo(def.weaponId);
            return;
        }
    }

    public void ResetToInitialWeapon()
    {
        currentWeaponId = initialWeaponId;
        var def = GetDefinition(currentWeaponId);
        if (def != null && playerAnim != null)
            playerAnim.ApplyWeaponDefinition(def);
    }

    public bool TrySwitchTo(int weaponId)
    {
        if (weaponId == currentWeaponId)
            return false;

        var def = GetDefinition(weaponId);
        if (def == null)
            return false;

        if (playerAnim == null)
            return false;

        var fromDef = GetDefinition(currentWeaponId);
        if (!playerAnim.TryPlayWeaponSwitchAnim(fromDef, def))
            return false;

        currentWeaponId = weaponId;
        PlaySessionRecorder.Instance?.RecordWeaponSwitch();
        return true;
    }

    public WeaponDefinition GetDefinition(int weaponId)
    {
        if (weapons == null)
            return null;

        for (int i = 0; i < weapons.Length; i++)
        {
            var def = weapons[i];
            if (def != null && def.weaponId == weaponId)
                return def;
        }

        return null;
    }

    /// <summary>
    /// 按 weapons 数组顺序收集当前运行时循环（有弹药且允许轮换；全空时仅手枪）。
    /// </summary>
    public int GetRuntimeCycleIds(List<int> buffer)
    {
        if (buffer == null)
            return 0;

        buffer.Clear();
        if (weapons == null)
            return 0;

        for (int i = 0; i < weapons.Length; i++)
        {
            var def = weapons[i];
            if (IsInRuntimeCycle(def))
                buffer.Add(def.weaponId);
        }

        return buffer.Count;
    }

    /// <summary>
    /// 某类弹药数量变化：0→有弹则入循环并切到该武器；当前武器弹尽则切到 +1 下一把，没有则回手枪。
    /// </summary>
    public void OnAmmoChanged(AmmoType type, int before, int after)
    {
        if (before == after)
            return;

        int weaponId = Character.WeaponIdFromAmmo(type);
        if (weaponId <= 0)
            return;

        if (before == 0 && after > 0)
        {
            SwitchToWeapon(weaponId);
            return;
        }

        if (after == 0 && currentWeaponId == weaponId)
            SwitchToNextRemainingOrPistol();
    }

    /// <summary>存档/开局纠偏：当前武器已不在循环且不是手枪时，切到下一把或手枪。</summary>
    public void ReconcileCurrentWeapon()
    {
        if (currentWeaponId == initialWeaponId)
            return;
        if (IsInRuntimeCycle(GetDefinition(currentWeaponId)))
            return;

        SwitchToNextRemainingOrPistol();
    }

    bool IsInRuntimeCycle(WeaponDefinition def)
    {
        if (def == null)
            return false;
        if (def.weaponId == 0)
            return !HasAnySpecialAmmo();
        if (!def.enabledInCycle)
            return false;
        return GetAmmoForWeapon(def.weaponId) > 0;
    }

    bool HasAnySpecialAmmo() => character != null && character.HasAnySpecialAmmo;

    int GetAmmoForWeapon(int weaponId) =>
        character != null ? character.GetAmmoForWeapon(weaponId) : 0;

    void SwitchToWeapon(int weaponId)
    {
        if (weaponId == currentWeaponId)
            return;
        if (TrySwitchTo(weaponId))
            return;
        ForceSwitchTo(weaponId);
    }

    void ForceSwitchTo(int weaponId)
    {
        var def = GetDefinition(weaponId);
        if (def == null)
            return;

        currentWeaponId = weaponId;
        if (playerAnim != null)
            playerAnim.ApplyWeaponDefinition(def);
    }

    void SwitchToNextRemainingOrPistol()
    {
        if (weapons == null || weapons.Length == 0)
        {
            SwitchToWeapon(initialWeaponId);
            return;
        }

        int index = FindIndexByWeaponId(currentWeaponId);
        if (index < 0)
            index = 0;

        int count = weapons.Length;
        for (int step = 1; step <= count; step++)
        {
            int nextIndex = (index + step) % count;
            var def = weapons[nextIndex];
            if (!IsInRuntimeCycle(def))
                continue;
            if (def.weaponId == currentWeaponId)
                break;
            SwitchToWeapon(def.weaponId);
            return;
        }

        SwitchToWeapon(initialWeaponId);
    }

    int FindIndexByWeaponId(int weaponId)
    {
        if (weapons == null)
            return -1;

        for (int i = 0; i < weapons.Length; i++)
        {
            if (weapons[i] != null && weapons[i].weaponId == weaponId)
                return i;
        }

        return -1;
    }

    public DataDefination GetDataID() => GetComponent<DataDefination>();

    public void GetSaveData(Data data)
    {
        var dataId = GetDataID();
        if (dataId == null || string.IsNullOrEmpty(dataId.ID))
            return;

        data.floatSavedData[dataId.ID + WeaponIdKeySuffix] = currentWeaponId;
    }

    public void LoadSaveData(Data data)
    {
        var dataId = GetDataID();
        if (dataId == null || string.IsNullOrEmpty(dataId.ID))
            return;

        if (!data.floatSavedData.TryGetValue(dataId.ID + WeaponIdKeySuffix, out float saved))
            return;

        int weaponId = Mathf.RoundToInt(saved);
        var def = GetDefinition(weaponId);
        if (def == null)
            return;

        currentWeaponId = weaponId;
        if (playerAnim != null)
            playerAnim.ApplyWeaponDefinition(def);

        ReconcileCurrentWeapon();
    }
}
