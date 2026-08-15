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

    [Header("MachineShoot（特殊弹 S）")]
    [SerializeField] int machineBurstCount = 4;
    [Tooltip("同一段 MachineShoot 内连续出弹的发间间隔（秒）")]
    [SerializeField] float machineFireInterval = 0.1f;
    [Tooltip("再次进入 MachineShoot 的最短间隔（秒），0 表示不限制")]
    [SerializeField] float machineReentryInterval = 0.2f;
    [Tooltip("MachineShoot 第一发相对动画开播延迟；<0 则复用 comboFireDelay")]
    [SerializeField] float machineFireDelay = -1f;

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
    GameObject pendingProjectilePrefab;

    bool machineBurstActive;
    int machineBurstRemaining;
    float machineBurstNextFireAt;
    FireDir machineBurstDir;

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
        TrySpawnMachineBurstProjectile();

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
                if (actions.Player.Attack.WasReleasedThisFrame())
                {
                    FireTapShot();
                    phase = ShootPhase.Idle;
                }
                else if (Time.time - pressTime >= chargeHoldThreshold
                    && playerAnim.BeginMachinistCharge())
                {
                    phase = ShootPhase.Charging;
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
        FireProjectile(pendingFireDir, pendingProjectilePrefab);
        pendingProjectilePrefab = null;
    }

    void TrySpawnMachineBurstProjectile()
    {
        if (!machineBurstActive || Time.time < machineBurstNextFireAt)
            return;

        FireProjectile(machineBurstDir, specialProjectilePrefabS);
        machineBurstRemaining--;
        if (machineBurstRemaining <= 0)
        {
            CancelMachineBurst();
            return;
        }

        machineBurstNextFireAt = Time.time + Mathf.Max(0f, machineFireInterval);
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

        bool forceMachineFromSpecialS =
            !forceBlastFromSpecialL
            && !forceElectricFromSpecialM
            && specialMagazine != null
            && specialMagazine.TryPeek(out peek)
            && peek == SpecialAmmoType.S;

        bool isHorizontalForward = IsHorizontalForwardAim();
        bool isFinisherNext = forceBlastFromSpecialL
            || forceElectricFromSpecialM
            || forceMachineFromSpecialS
            || comboCount + 1 >= comboFinisherCount;

        if (forceMachineFromSpecialS)
        {
            if (machineReentryInterval > 0f && Time.time - lastShotTime < machineReentryInterval)
                return;
        }
        else if (!isFinisherNext && normalFireInterval > 0f && Time.time - lastShotTime < normalFireInterval)
        {
            return;
        }

        bool advancesComboCount =
            !forceBlastFromSpecialL && !forceElectricFromSpecialM && !forceMachineFromSpecialS;
        if (advancesComboCount)
            comboCount++;

        MachinistShootKind kind;
        if (forceBlastFromSpecialL)
            kind = MachinistShootKind.Blast;
        else if (forceElectricFromSpecialM)
            kind = MachinistShootKind.Electric;
        else if (forceMachineFromSpecialS)
            kind = MachinistShootKind.Machine;
        else
            kind = ResolveTapShootKind(isHorizontalForward);

        if (!playerAnim.TryPlayMachinistShootAnim(kind))
        {
            if (advancesComboCount)
                comboCount--;
            return;
        }

        // 新射击成功开播后再取消上一段 Machine 连射（失败的普通射击不得取消）
        CancelMachineBurst();
        CancelPendingFire();

        lastShotTime = Time.time;
        bool isCombo = kind == MachinistShootKind.Combo;
        bool isBlast = kind == MachinistShootKind.Blast;
        bool isElectric = kind == MachinistShootKind.Electric;
        bool isMachine = kind == MachinistShootKind.Machine;

        FireDir fireDir = ResolveFireDir();
        if ((isCombo || isBlast || isElectric || isMachine) && isHorizontalForward)
        {
            // 空中水平终结/Blast/Electric 用前方水平弹；地面/蹲姿/Machine 仍蹲射点
            fireDir = playerAnim.IsForcedAirCombo ? FireDir.Forward : FireDir.Crouch;
        }

        // 特殊弹：进入射击即消耗，出弹只用锁定 Prefab
        if (isMachine)
        {
            if (!TryConsumeSpecial(SpecialAmmoType.S))
            {
                Debug.LogWarning("MachinistShooting: MachineShoot 消耗特殊弹 S 失败，取消连射。", this);
                playerAnim.CancelMachinistShootAnim();
                return;
            }

            robotComboEvent?.RaiseEvent();
            comboCount = 0;

            if (playerMovement.GetShootLookDown() || playerAnim.IsForcedAirCombo)
                playerMovement.NotifyAirHangFromDownShot();

            StartMachineBurst(fireDir);
            return;
        }

        if (isBlast)
        {
            if (!TryConsumeSpecial(SpecialAmmoType.L))
            {
                Debug.LogWarning("MachinistShooting: BlastShoot 消耗特殊弹 L 失败，取消出弹。", this);
                playerAnim.CancelMachinistShootAnim();
                return;
            }

            robotBlastComboEvent?.RaiseEvent();
            comboCount = 0;

            if (playerMovement.GetShootLookDown() || playerAnim.IsForcedAirCombo)
                playerMovement.NotifyAirHangFromDownShot();

            float delay = blastFireDelay >= 0f ? blastFireDelay : comboFireDelay;
            ScheduleFire(fireDir, specialProjectilePrefabL, delay);
            return;
        }

        if (isElectric)
        {
            if (!TryConsumeSpecial(SpecialAmmoType.M))
            {
                Debug.LogWarning("MachinistShooting: ElectricShoot 消耗特殊弹 M 失败，取消出弹。", this);
                playerAnim.CancelMachinistShootAnim();
                return;
            }

            comboCount = 0;

            if (playerMovement.GetShootLookDown() || playerAnim.IsForcedAirCombo)
                playerMovement.NotifyAirHangFromDownShot();

            float delay;
            if (electricFireDelay >= 0f)
                delay = electricFireDelay;
            else if (blastFireDelay >= 0f)
                delay = blastFireDelay;
            else
                delay = comboFireDelay;

            ScheduleFire(fireDir, specialProjectilePrefabM, delay);
            return;
        }

        // 普通 / 终结连击
        if (isCombo)
            robotComboEvent?.RaiseEvent();

        if (playerMovement.GetShootLookDown() || playerAnim.IsForcedAirCombo)
            playerMovement.NotifyAirHangFromDownShot();

        var prefab = isCombo ? comboProjectilePrefab : normalProjectilePrefab;
        float normalDelay = isCombo ? comboFireDelay : normalFireDelay;
        ScheduleFire(fireDir, prefab != null ? prefab.gameObject : null, normalDelay);

        if (isCombo)
            comboCount = 0;
    }

    bool TryConsumeSpecial(SpecialAmmoType expected)
    {
        if (specialMagazine == null)
            return false;

        if (!specialMagazine.TryConsume(out SpecialAmmoType consumed))
            return false;

        if (consumed != expected)
        {
            Debug.LogWarning(
                $"MachinistShooting: 期望消耗 {expected}，实际为 {consumed}。",
                this);
            return false;
        }

        return true;
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

    MachinistShootKind ResolveTapShootKind(bool isHorizontalForward)
    {
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

    void StartMachineBurst(FireDir dir)
    {
        CancelPendingFire();

        int count = Mathf.Max(1, machineBurstCount);
        machineBurstActive = true;
        machineBurstRemaining = count;
        machineBurstDir = dir;

        float delay = machineFireDelay >= 0f ? machineFireDelay : comboFireDelay;
        machineBurstNextFireAt = Time.time + Mathf.Max(0f, delay);
    }

    void CancelMachineBurst()
    {
        machineBurstActive = false;
        machineBurstRemaining = 0;
    }

    void CancelPendingFire()
    {
        hasPendingFire = false;
        pendingProjectilePrefab = null;
    }

    void ScheduleFire(FireDir dir, GameObject prefab, float delay)
    {
        if (prefab == null)
        {
            CancelPendingFire();
            return;
        }

        if (delay <= 0f)
        {
            CancelPendingFire();
            FireProjectile(dir, prefab);
            return;
        }

        hasPendingFire = true;
        pendingFireAt = Time.time + delay;
        pendingFireDir = dir;
        pendingProjectilePrefab = prefab;
    }

    void FireChargeShot()
    {
        CancelMachineBurst();
        CancelPendingFire();
        comboCount = 0;
        // Release 会清 ActiveChargeAim，须先解析方向
        var fireDir = ResolveChargeFireDir();
        if (!playerAnim.ReleaseMachinistCharge())
            return;

        // 空中蓄力释放：整段 ChargeShoot 满强度滞空（地面调用会被 Notify 内部忽略）
        playerMovement.NotifyAirHangFromDownShot();

        FireProjectile(fireDir, ResolveChargePrefab());
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

    void FireProjectile(FireDir dir, GameObject prefabGo)
    {
        if (prefabGo == null)
            return;

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

    Transform GetFirePoint(FireDir dir) => dir switch
    {
        FireDir.Forward => forwardPoint != null ? forwardPoint : transform,
        FireDir.Crouch => crouchPoint != null ? crouchPoint : transform,
        FireDir.Up => upPoint != null ? upPoint : transform,
        FireDir.Down => downPoint != null ? downPoint : transform,
        _ => transform,
    };
}
