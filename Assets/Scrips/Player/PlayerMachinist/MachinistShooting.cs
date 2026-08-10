using UnityEngine;
using UnityEngine.InputSystem;

[DefaultExecutionOrder(100)]
[RequireComponent(typeof(PlayerAnim))]
[RequireComponent(typeof(PlayerMovement))]
[RequireComponent(typeof(SpecialMagazine))]
public class MachinistShooting : MonoBehaviour
{
    enum ShootPhase { Idle, Pressing, Charging }

    [Header("子弹")]
    [SerializeField] PlayerMNormalBullet normalProjectilePrefab;
    [SerializeField] PlayerMNormalBullet comboProjectilePrefab;
    [Tooltip("下标对应 WeaponId：0 不耗弹，1=BulletS，2=BulletM，3=BulletL；需实现 IPlayerAmmo")]
    [SerializeField] GameObject[] chargeProjectilePrefabs;
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
    [Tooltip("特殊弹 L / BlastShoot：动画开始后延迟多久再生成子弹；<0 则复用 comboFireDelay")]
    [SerializeField] float blastFireDelay = -1f;
    [Tooltip("特殊弹 M / ElectricShoot：动画开始后延迟多久再生成子弹；<0 则复用 blastFireDelay（再回退 comboFireDelay）")]
    [SerializeField] float electricFireDelay = -1f;

    [Header("蓄力")]
    [SerializeField] float chargeHoldThreshold = 0.3f;
    [Tooltip("下标对应 WeaponId：0 忽略；1/2/3 为每次蓄力消耗的 BulletS/M/L 数量")]
    [SerializeField] int[] chargeAmmoCosts = { 0, 1, 1, 1 };

    [Header("事件")]
    [SerializeField] VoidEventSO robotComboEvent;
    [SerializeField] VoidEventSO robotBlastComboEvent;

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

        bool forceBlastFromSpecialL =
            specialMagazine != null
            && specialMagazine.TryPeek(out SpecialAmmoType peek)
            && peek == SpecialAmmoType.L;

        bool forceElectricFromSpecialM =
            !forceBlastFromSpecialL
            && specialMagazine != null
            && specialMagazine.TryPeek(out peek)
            && peek == SpecialAmmoType.M;

        bool forceComboFromSpecialS =
            !forceBlastFromSpecialL
            && !forceElectricFromSpecialM
            && specialMagazine != null
            && specialMagazine.TryPeek(out peek)
            && peek == SpecialAmmoType.S;

        bool isHorizontalForward = IsHorizontalForwardAim();
        bool isFinisherNext = forceBlastFromSpecialL
            || forceElectricFromSpecialM
            || forceComboFromSpecialS
            || comboCount + 1 >= comboFinisherCount;
        if (!isFinisherNext && normalFireInterval > 0f && Time.time - lastShotTime < normalFireInterval)
            return;

        bool advancesComboCount =
            !forceBlastFromSpecialL && !forceElectricFromSpecialM && !forceComboFromSpecialS;
        if (advancesComboCount)
            comboCount++;

        MachinistShootKind kind;
        if (forceBlastFromSpecialL)
            kind = MachinistShootKind.Blast;
        else if (forceElectricFromSpecialM)
            kind = MachinistShootKind.Electric;
        else
            kind = ResolveTapShootKind(forceComboFromSpecialS, isHorizontalForward);

        if (!playerAnim.TryPlayMachinistShootAnim(kind))
        {
            if (advancesComboCount)
                comboCount--;
            return;
        }

        lastShotTime = Time.time;
        bool isCombo = kind == MachinistShootKind.Combo;
        bool isBlast = kind == MachinistShootKind.Blast;
        bool isElectric = kind == MachinistShootKind.Electric;

        // 终结 / Blast：射击动画一开始就触发机器人连携，不等子弹生成（Electric/M 不触发）
        if (isBlast)
            robotBlastComboEvent?.RaiseEvent();
        else if (isCombo)
            robotComboEvent?.RaiseEvent();

        // 动画开始即滞空：下射，或空中水平终结/Blast/Electric
        if (playerMovement.GetShootLookDown() || playerAnim.IsForcedAirCombo)
            playerMovement.NotifyAirHangFromDownShot();

        var prefab = isCombo ? comboProjectilePrefab : normalProjectilePrefab;
        float delay;
        if (isElectric)
        {
            if (electricFireDelay >= 0f)
                delay = electricFireDelay;
            else if (blastFireDelay >= 0f)
                delay = blastFireDelay;
            else
                delay = comboFireDelay;
        }
        else if (isBlast)
            delay = blastFireDelay >= 0f ? blastFireDelay : comboFireDelay;
        else
            delay = isCombo ? comboFireDelay : normalFireDelay;

        FireDir fireDir = ResolveFireDir();
        if ((isCombo || isBlast || isElectric) && isHorizontalForward)
        {
            // 空中水平终结/Blast/Electric 用前方水平弹；地面/蹲姿仍蹲射点
            fireDir = playerAnim.IsForcedAirCombo ? FireDir.Forward : FireDir.Crouch;
        }
        ScheduleFire(fireDir, prefab, delay);

        if (isCombo && !forceComboFromSpecialS)
            comboCount = 0;
        else if (isBlast || isElectric)
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

        // 蓄力下射：释放射击动画开始时即滞空
        if (fireDir == FireDir.Down)
            playerMovement.NotifyAirHangFromDownShot();

        FireCharge(fireDir, ResolveChargePrefab());
    }

    GameObject ResolveChargePrefab()
    {
        int weaponId = weaponController != null ? weaponController.CurrentWeaponId : 0;
        if (weaponId < 0 || weaponId >= 4)
            weaponId = 0;

        GameObject fallback = GetChargePrefab(0);

        if (weaponId == 0)
            return fallback;

        GameObject prefab = GetChargePrefab(weaponId);
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

    GameObject GetChargePrefab(int index)
    {
        if (chargeProjectilePrefabs == null || index < 0 || index >= chargeProjectilePrefabs.Length)
            return null;
        return chargeProjectilePrefabs[index];
    }

    void FireCharge(FireDir dir, GameObject prefab)
    {
        if (prefab == null)
            return;

        Transform point = GetFirePoint(dir);
        float faceY = playerMovement.FaceDirection > 0f ? 0f : 180f;
        var projectile = Instantiate(prefab, point.position, Quaternion.identity);
        var ammo = projectile.GetComponent<IPlayerAmmo>();
        if (ammo == null)
        {
            Debug.LogError($"Charge prefab '{prefab.name}' is missing IPlayerAmmo.", prefab);
            Destroy(projectile);
            return;
        }

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
