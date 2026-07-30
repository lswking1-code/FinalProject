using UnityEngine;
using UnityEngine.InputSystem;

[DefaultExecutionOrder(50)]
[RequireComponent(typeof(PlayerAnim))]
[RequireComponent(typeof(PlayerMovement))]
public class PlayerWeaponController : MonoBehaviour
{
    [SerializeField] WeaponDefinition[] weapons;
    [SerializeField] int initialWeaponId = 0;
    [SerializeField] float holdToInitialDuration = 0.4f;
    [SerializeField] int currentWeaponId;

    InputSystem_Actions actions;
    PlayerAnimBase playerAnim;
    PlayerMovement playerMovement;

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
        playerAnim = GetComponent<PlayerAnimBase>();
        playerMovement = GetComponent<PlayerMovement>();
        currentWeaponId = initialWeaponId;
    }

    void Start()
    {
        var def = GetDefinition(currentWeaponId);
        if (def != null && playerAnim != null)
            playerAnim.ApplyWeaponDefinition(def);
    }

    void OnEnable() => actions.Player.Enable();

    void OnDisable() => actions.Player.Disable();

    void OnDestroy() => actions?.Dispose();

    void Update()
    {
        if (playerMovement != null && playerMovement.IsActionLocked)
            return;

        if (playerAnim != null && (playerAnim.IsDead || playerAnim.IsSwitchingWeapon || playerAnim.IsRolling))
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
            if (def == null || !def.CanEnterCycle)
                continue;
            if (def.weaponId == currentWeaponId)
                return;
            TrySwitchTo(def.weaponId);
            return;
        }
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

        if (!playerAnim.TryPlayWeaponSwitchAnim(def))
            return false;

        currentWeaponId = weaponId;
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
    /// 在 CanEnterCycle 武器列表中取环形前一位 / 后一位（与 Q/E 轮换同一过滤规则）。
    /// </summary>
    public bool TryGetCycleNeighbors(int weaponId, out int prevId, out int nextId)
    {
        prevId = weaponId;
        nextId = weaponId;

        if (weapons == null || weapons.Length == 0)
            return false;

        var cycleIds = new System.Collections.Generic.List<int>(weapons.Length);
        for (int i = 0; i < weapons.Length; i++)
        {
            var def = weapons[i];
            if (def != null && def.CanEnterCycle)
                cycleIds.Add(def.weaponId);
        }

        if (cycleIds.Count == 0)
            return false;

        int index = cycleIds.IndexOf(weaponId);
        if (index < 0)
            return false;

        int count = cycleIds.Count;
        prevId = cycleIds[(index - 1 + count) % count];
        nextId = cycleIds[(index + 1) % count];
        return true;
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
}
