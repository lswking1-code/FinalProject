using UnityEngine;
using UnityEngine.InputSystem;

[DefaultExecutionOrder(100)]
[RequireComponent(typeof(PlayerAnim))]
[RequireComponent(typeof(PlayerMovement))]
[RequireComponent(typeof(SpecialMagazine))]
public class MachinistShooting : MonoBehaviour
{
    enum ShootPhase { Idle, Pressing, Charging }
    enum ComboStance { Ground, Air, Crouch }

    [Header("子弹")]
    [SerializeField] PlayerMNormalBullet normalProjectilePrefab;
    [SerializeField] PlayerMNormalBullet comboProjectilePrefab;
    [Tooltip("下标对应 WeaponId：0 不耗弹，1=BulletS，2=BulletM，3=BulletL")]
    [SerializeField] PlayerMChargeBullet[] chargeProjectilePrefabs;
    [Header("特殊弹子弹")]
    [SerializeField] GameObject specialProjectilePrefabS;
    [SerializeField] GameObject specialProjectilePrefabM;
    [SerializeField] GameObject specialProjectilePrefabL;

    [Header("发射点")]
    [SerializeField] Transform forwardPoint;
    [SerializeField] Transform crouchPoint;
    [SerializeField] Transform upPoint;
    [SerializeField] Transform downPoint;

    [Header("连击")]
    [SerializeField] int comboFinisherCount = 4;
    [SerializeField] float comboResetWindow = 0.8f;
    [Tooltip("普通射击最短间隔（秒），0 表示不限制；不影响终结连击与蓄力")]
    [SerializeField] float normalFireInterval = 0f;
    [Tooltip("普通连击：动画开始后延迟多久再生成子弹")]
    [SerializeField] float normalFireDelay = 0f;
    [Tooltip("终结连击：动画开始后延迟多久再生成子弹")]
    [SerializeField] float comboFireDelay = 0f;

    [Header("蓄力")]
    [SerializeField] float chargeHoldThreshold = 0.3f;
    [Tooltip("下标对应 WeaponId：0 忽略；1/2/3 为每次蓄力消耗的 BulletS/M/L 数量")]
    [SerializeField] int[] chargeAmmoCosts = { 0, 1, 1, 1 };

    [Header("事件")]
    [SerializeField] VoidEventSO robotComboEvent;

    InputSystem_Actions actions;
    PlayerAnim playerAnim;
    PlayerMovement playerMovement;
    PlayerWeaponController weaponController;
    Character character;
    SpecialMagazine specialMagazine;

    ShootPhase phase = ShootPhase.Idle;
    float pressTime;
    int comboCount;
    float lastShotTime;

    bool hasPendingFire;
    float pendingFireAt;
    FireDir pendingFireDir;
    PlayerMNormalBullet pendingPrefab;

    bool hasTrackedComboStance;
    ComboStance lastComboStance;

    void Awake()
    {
        actions = new InputSystem_Actions();
        playerAnim = GetComponent<PlayerAnim>();
        playerMovement = GetComponent<PlayerMovement>();
        weaponController = GetComponent<PlayerWeaponController>();
        character = GetComponent<Character>();
        specialMagazine = GetComponent<SpecialMagazine>();
    }

    void OnEnable() => actions.Player.Enable();

    void OnDisable() => actions.Player.Disable();

    void OnDestroy() => actions?.Dispose();

    void Update()
    {
        TrySpawnPendingProjectile();
        TryResetComboOnStanceChange();

        if (playerMovement.IsActionLocked || playerAnim.IsDispatching || playerAnim.IsPlayingLoadBullet)
            return;

        switch (phase)
        {
            case ShootPhase.Idle:
                if (playerAnim.IsPlayingMachinistComboShoot || playerAnim.IsPlayingLoadBullet)
                    break;

                if (actions.Player.Attack.WasPressedThisFrame())
                {
                    pressTime = Time.time;
                    phase = ShootPhase.Pressing;
                }
                break;

            case ShootPhase.Pressing:
                if (Time.time - pressTime >= chargeHoldThreshold)
                {
                    phase = ShootPhase.Charging;
                    playerAnim.BeginMachinistCharge();
                }
                else if (actions.Player.Attack.WasReleasedThisFrame())
                {
                    FireTapShot();
                    phase = ShootPhase.Idle;
                }
                break;

            case ShootPhase.Charging:
                if (actions.Player.Attack.WasReleasedThisFrame())
                {
                    FireChargeShot();
                    phase = ShootPhase.Idle;
                }
                break;
        }

        TryResetComboOnTimeout();
    }

    void TryResetComboOnTimeout()
    {
        if (comboCount > 0 && Time.time - lastShotTime > comboResetWindow)
            comboCount = 0;
    }

    void TryResetComboOnStanceChange()
    {
        var stance = ResolveComboStance();
        if (!hasTrackedComboStance)
        {
            hasTrackedComboStance = true;
            lastComboStance = stance;
            return;
        }

        if (stance == lastComboStance)
            return;

        comboCount = 0;
        lastComboStance = stance;
    }

    ComboStance ResolveComboStance()
    {
        if (playerAnim.IsCrouching)
            return ComboStance.Crouch;
        if (playerAnim.CurrentAirPhase != PlayerAnimBase.AirPhaseType.Ground)
            return ComboStance.Air;
        return ComboStance.Ground;
    }

    void TrySpawnPendingProjectile()
    {
        if (!hasPendingFire || Time.time < pendingFireAt)
            return;

        hasPendingFire = false;
        Fire(pendingFireDir, pendingPrefab);
        pendingPrefab = null;
    }

    bool IsComboExpired() =>
        comboCount > 0 && Time.time - lastShotTime > comboResetWindow;

    void FireTapShot()
    {
        if (IsComboExpired())
            comboCount = 0;

        bool forceComboFromSpecialS =
            specialMagazine != null
            && specialMagazine.TryPeek(out SpecialAmmoType peek)
            && peek == SpecialAmmoType.S;

        bool isHorizontalForward = IsHorizontalForwardAim();
        bool isFinisherNext = forceComboFromSpecialS
            || comboCount + 1 >= comboFinisherCount;
        if (!isFinisherNext && normalFireInterval > 0f && Time.time - lastShotTime < normalFireInterval)
            return;

        if (!forceComboFromSpecialS)
            comboCount++;

        var kind = ResolveTapShootKind(forceComboFromSpecialS, isHorizontalForward);

        if (!playerAnim.TryPlayMachinistShootAnim(kind))
        {
            if (!forceComboFromSpecialS)
                comboCount--;
            return;
        }

        lastShotTime = Time.time;
        bool isCombo = kind == MachinistShootKind.Combo;
        // 终结连击：射击动画一开始就触发机器人连携，不等子弹生成
        if (isCombo)
            robotComboEvent?.RaiseEvent();

        var prefab = isCombo ? comboProjectilePrefab : normalProjectilePrefab;
        float delay = isCombo ? comboFireDelay : normalFireDelay;
        FireDir fireDir = ResolveFireDir();
        if (isCombo && isHorizontalForward)
            fireDir = FireDir.Crouch;
        ScheduleFire(fireDir, prefab, delay);

        if (isCombo && !forceComboFromSpecialS)
            comboCount = 0;
    }

    bool IsHorizontalForwardAim()
    {
        if (playerAnim.IsCrouching)
            return true;
        if (playerMovement.GetShootLookUp())
            return false;
        if (playerMovement.GetShootLookDown())
            return false;
        return true;
    }

    MachinistShootKind ResolveTapShootKind(bool forceComboFromSpecialS, bool isHorizontalForward)
    {
        if (forceComboFromSpecialS)
            return MachinistShootKind.Combo;

        if (isHorizontalForward && comboFinisherCount >= 3)
        {
            if (comboCount >= comboFinisherCount)
                return MachinistShootKind.Combo;
            if (comboCount == comboFinisherCount - 1)
                return MachinistShootKind.Combo2;
            if (comboCount == comboFinisherCount - 2)
                return MachinistShootKind.Combo1;
            return MachinistShootKind.Normal;
        }

        return comboCount >= comboFinisherCount
            ? MachinistShootKind.Combo
            : MachinistShootKind.Normal;
    }

    void ScheduleFire(FireDir dir, PlayerMNormalBullet prefab, float delay)
    {
        if (prefab == null)
        {
            hasPendingFire = false;
            pendingPrefab = null;
            return;
        }

        if (delay <= 0f)
        {
            hasPendingFire = false;
            pendingPrefab = null;
            Fire(dir, prefab);
            return;
        }

        hasPendingFire = true;
        pendingFireAt = Time.time + delay;
        pendingFireDir = dir;
        pendingPrefab = prefab;
    }

    void FireChargeShot()
    {
        comboCount = 0;
        // Release 会清 ActiveChargeAim，须先解析方向
        var fireDir = ResolveChargeFireDir();
        if (!playerAnim.ReleaseMachinistCharge())
            return;

        FireCharge(fireDir, ResolveChargePrefab());
    }

    PlayerMChargeBullet ResolveChargePrefab()
    {
        int weaponId = weaponController != null ? weaponController.CurrentWeaponId : 0;
        if (weaponId < 0 || weaponId >= 4)
            weaponId = 0;

        PlayerMChargeBullet fallback = GetChargePrefab(0);

        if (weaponId == 0)
            return fallback;

        PlayerMChargeBullet prefab = GetChargePrefab(weaponId);
        if (prefab == null)
            return fallback;

        AmmoType ammoType = weaponId switch
        {
            1 => AmmoType.S,
            2 => AmmoType.M,
            3 => AmmoType.L,
            _ => AmmoType.S,
        };

        int cost = GetChargeAmmoCost(weaponId);
        if (character == null || !character.TrySpendAmmo(ammoType, cost))
            return fallback;

        return prefab;
    }

    int GetChargeAmmoCost(int weaponId)
    {
        if (chargeAmmoCosts == null || weaponId < 0 || weaponId >= chargeAmmoCosts.Length)
            return 1;
        return chargeAmmoCosts[weaponId];
    }

    PlayerMChargeBullet GetChargePrefab(int index)
    {
        if (chargeProjectilePrefabs == null || index < 0 || index >= chargeProjectilePrefabs.Length)
            return null;
        return chargeProjectilePrefabs[index];
    }

    void FireCharge(FireDir dir, PlayerMChargeBullet prefab)
    {
        if (prefab == null)
            return;

        Transform point = GetFirePoint(dir);
        float faceY = playerMovement.FaceDirection > 0f ? 0f : 180f;
        var projectile = Instantiate(prefab, point.position, Quaternion.identity);
        IPlayerAmmo ammo = projectile;
        ammo.Init(dir, faceY, character);
    }

    FireDir ResolveChargeFireDir() => playerAnim.ActiveChargeAim switch
    {
        MachinistChargeAim.Up => FireDir.Up,
        MachinistChargeAim.Down => FireDir.Down,
        MachinistChargeAim.Crouch => FireDir.Crouch,
        _ => FireDir.Forward,
    };

    FireDir ResolveFireDir()
    {
        if (playerAnim.IsCrouching)
            return FireDir.Crouch;
        if (playerMovement.GetShootLookUp())
            return FireDir.Up;
        if (playerMovement.GetShootLookDown())
            return FireDir.Down;
        return FireDir.Forward;
    }

    void Fire(FireDir dir, PlayerMNormalBullet fallbackPrefab)
    {
        if (fallbackPrefab == null)
            return;

        GameObject prefabGo = fallbackPrefab.gameObject;

        if (specialMagazine != null && specialMagazine.TryConsume(out SpecialAmmoType specialType))
        {
            GameObject specialGo = ResolveSpecialPrefab(specialType);
            if (specialGo != null && specialGo.GetComponent<IPlayerAmmo>() != null)
                prefabGo = specialGo;
            else
                Debug.LogWarning(
                    $"MachinistShooting: 特殊弹 {specialType} 的 Prefab 未配置或缺少 IPlayerAmmo，回退普通/连击弹。",
                    this);
        }

        Transform point = GetFirePoint(dir);
        float faceY = playerMovement.FaceDirection > 0f ? 0f : 180f;
        var instance = Instantiate(prefabGo, point.position, Quaternion.identity);
        var ammo = instance.GetComponent<IPlayerAmmo>();
        if (ammo == null)
        {
            Debug.LogError($"Projectile prefab '{prefabGo.name}' is missing IPlayerAmmo.", prefabGo);
            Destroy(instance);
            return;
        }

        ammo.Init(dir, faceY, character);
    }

    GameObject ResolveSpecialPrefab(SpecialAmmoType type) => type switch
    {
        SpecialAmmoType.S => specialProjectilePrefabS,
        SpecialAmmoType.M => specialProjectilePrefabM,
        SpecialAmmoType.L => specialProjectilePrefabL,
        _ => null,
    };

    Transform GetFirePoint(FireDir dir) => dir switch
    {
        FireDir.Forward => forwardPoint != null ? forwardPoint : transform,
        FireDir.Crouch => crouchPoint != null ? crouchPoint : transform,
        FireDir.Up => upPoint != null ? upPoint : transform,
        FireDir.Down => downPoint != null ? downPoint : transform,
        _ => transform,
    };
}
