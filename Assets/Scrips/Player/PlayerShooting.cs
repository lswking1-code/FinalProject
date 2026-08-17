using System;
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

[Serializable]
public class WeaponFirePointSet
{
    public int weaponId;
    public Transform forwardPoint;
    public Transform crouchPoint;
    public Transform upPoint;
    public Transform downPoint;
}

[Serializable]
public class WeaponFireConfig
{
    public int weaponId;
    [Tooltip("普通子弹需 IPlayerAmmo；holdToFire 时为 PlayerLaserBeam prefab")]
    public GameObject projectilePrefab;
    [Tooltip("两次开火之间的最小间隔（秒）；0 表示不限制。连发整段算一次开火")]
    public float fireInterval = 0f;
    [Tooltip("单次按下连发数；1 为单发")]
    public int burstCount = 1;
    [Tooltip("连发间隔（秒）")]
    public float burstInterval = 0.06f;
    [Tooltip("相对枪口的最大位置偏移；水平射击为上下，仰俯射为左右；0 为无散射")]
    public float spreadOffset = 0f;
    [Tooltip("按住持续开火（镭射枪）；松手结束")]
    public bool holdToFire;
    [Tooltip("短按点射/连发，长按进入射速渐快的持续开火（机枪）")]
    public bool spinUpOnHold;
    [Tooltip("按住超过该秒数且 burst 结束后进入 SpinUp")]
    public float spinUpHoldThreshold = 0.2f;
    [Tooltip("SpinUp 起始开火间隔（秒）")]
    public float spinUpStartInterval = 0.12f;
    [Tooltip("SpinUp 极限开火间隔（秒）")]
    public float spinUpMinInterval = 0.04f;
    [Tooltip("从起始间隔加速到极限间隔所需秒数")]
    public float spinUpRampDuration = 1.5f;
    [Tooltip("按住蓄力：未蓄满松手普通射击，蓄满松手发射 chargedProjectilePrefab")]
    public bool chargeOnHold;
    [Tooltip("按住达到该秒数后进入蓄满状态")]
    public float chargeHoldThreshold = 0.35f;
    [Tooltip("蓄满松手时的子弹 prefab（如龙息）；为空则回退普通 projectilePrefab")]
    public GameObject chargedProjectilePrefab;
    [Tooltip("每次出弹消耗弹药数；weaponId 0 永远不耗弹。1/2/3 对应 BulletS/M/L")]
    public int ammoCost = 1;
    [Tooltip("holdToFire 时每隔该秒数再耗弹一次；0 表示仅开束时耗一次")]
    public float holdAmmoInterval = 0.1f;
}

[DefaultExecutionOrder(100)]
[RequireComponent(typeof(PlayerAnim))]
[RequireComponent(typeof(PlayerMovement))]
[RequireComponent(typeof(PlayerMelee))]
public class PlayerShooting : MonoBehaviour
{
    enum ChargeShootPhase { Idle, Pressing, Charging }

    [SerializeField] GameObject projectilePrefab;
    [SerializeField] WeaponFireConfig[] fireConfigs;
    [SerializeField] Transform forwardPoint;
    [SerializeField] Transform crouchPoint;
    [SerializeField] Transform upPoint;
    [SerializeField] Transform downPoint;
    [SerializeField] WeaponFirePointSet[] firePointSets;

    InputSystem_Actions actions;
    PlayerAnimBase playerAnim;
    PlayerMovement playerMovement;
    PlayerMelee playerMelee;
    PlayerWeaponController weaponController;
    Character character;

    Coroutine burstRoutine;
    float lastSpreadOffset;
    float nextFireTime;

    PlayerLaserBeam activeLaser;

    bool isSpinningUp;
    bool attackHeld;
    float attackPressTime;
    float spinUpStartTime;
    float spinNextFireTime;
    WeaponFireConfig spinUpConfig;

    ChargeShootPhase chargePhase = ChargeShootPhase.Idle;
    float chargePressTime;
    int chargeWeaponId = -1;
    float laserNextAmmoTime;

    void Awake()
    {
        actions = new InputSystem_Actions();
        playerAnim = PlayerAnimBase.Resolve(gameObject);
        playerMovement = GetComponent<PlayerMovement>();
        playerMelee = GetComponent<PlayerMelee>();
        weaponController = GetComponent<PlayerWeaponController>();
        character = GetComponent<Character>();
    }

    void OnEnable() => actions.Player.Enable();

    void OnDisable()
    {
        actions.Player.Disable();
        StopBurst();
        ExitSpinUp();
        CancelChargeShot(playRelease: false);
        EndLaser(immediate: true);
    }

    void OnDestroy()
    {
        ExitSpinUp();
        CancelChargeShot(playRelease: false);
        EndLaser(immediate: true);
        actions?.Dispose();
    }

    void Update()
    {
        WeaponFireConfig config = ResolveFireConfig();
        bool holdLaser = config != null && config.holdToFire;
        bool chargeHold = config != null && config.chargeOnHold;

        if (ShouldForceEndLaser())
        {
            EndLaser(immediate: false);
            if (!holdLaser || !actions.Player.Attack.IsPressed())
                return;
        }

        if (holdLaser)
        {
            if (isSpinningUp || attackHeld)
                ExitSpinUp();
            if (chargePhase != ChargeShootPhase.Idle)
                CancelChargeShot(playRelease: false);
            UpdateHoldLaser(config);
            return;
        }

        if (activeLaser != null)
            EndLaser(immediate: false);

        if (ShouldForceCancelCharge(config))
        {
            CancelChargeShot(playRelease: false);
            return;
        }

        if (chargeHold)
        {
            if (isSpinningUp || attackHeld)
                ExitSpinUp();
            UpdateChargeWeapon(config);
            return;
        }

        if (chargePhase != ChargeShootPhase.Idle)
            CancelChargeShot(playRelease: false);

        if (ShouldForceEndSpinUp())
        {
            ExitSpinUp();
            return;
        }

        if (config == null || !config.spinUpOnHold)
        {
            if (isSpinningUp || attackHeld)
                ExitSpinUp();
            UpdateTapFire(config);
            return;
        }

        UpdateSpinUpWeapon(config);
    }

    void UpdateTapFire(WeaponFireConfig config)
    {
        if (playerMovement.IsActionLocked)
            return;

        if (playerAnim != null && playerAnim.IsRolling)
            return;

        if (playerAnim != null && playerAnim.IsSwitchingWeapon)
            return;

        if (playerAnim != null && playerAnim.IsMelee)
            return;

        if (burstRoutine != null)
            return;

        if (!actions.Player.Attack.WasPressedThisFrame())
            return;

        if (playerMelee != null && playerMelee.IsEnemyInMeleeRange()
            && playerMelee.TryMelee())
            return;

        if (!HasAmmo(config))
            return;

        TryFireNormalShot(config);
    }

    void UpdateChargeWeapon(WeaponFireConfig config)
    {
        switch (chargePhase)
        {
            case ChargeShootPhase.Idle:
                if (playerMovement.IsActionLocked)
                    return;
                if (playerAnim != null && (playerAnim.IsRolling || playerAnim.IsSwitchingWeapon || playerAnim.IsMelee))
                    return;
                if (burstRoutine != null)
                    return;

                if (!actions.Player.Attack.WasPressedThisFrame())
                    return;

                if (playerMelee != null && playerMelee.IsEnemyInMeleeRange()
                    && playerMelee.TryMelee())
                    return;

                if (!HasAmmo(config))
                    return;

                chargePressTime = Time.time;
                chargeWeaponId = config.weaponId;
                chargePhase = ChargeShootPhase.Pressing;
                break;

            case ChargeShootPhase.Pressing:
            {
                float threshold = Mathf.Max(0f, config.chargeHoldThreshold);
                if (Time.time - chargePressTime >= threshold)
                {
                    if (playerAnim != null && playerAnim.BeginMachinistCharge())
                    {
                        chargePhase = ChargeShootPhase.Charging;
                        SyncChargeAim();
                    }
                    else
                    {
                        // 动画进入失败：当作未蓄满，允许松手点射
                    }
                }
                else if (actions.Player.Attack.WasReleasedThisFrame())
                {
                    TryFireNormalShot(config);
                    chargePhase = ChargeShootPhase.Idle;
                    chargeWeaponId = -1;
                }
                break;
            }

            case ChargeShootPhase.Charging:
                SyncChargeAim();
                if (actions.Player.Attack.WasReleasedThisFrame())
                {
                    TryFireChargedShot(config);
                    chargePhase = ChargeShootPhase.Idle;
                    chargeWeaponId = -1;
                }
                break;
        }
    }

    void SyncChargeAim()
    {
        if (playerAnim == null || playerMovement == null)
            return;

        playerAnim.SyncChargeAimFromInput(
            playerMovement.GetShootLookUp(),
            playerMovement.GetShootLookDown(),
            playerAnim.IsCrouching);
    }

    bool TryFireNormalShot(WeaponFireConfig config)
    {
        float fireInterval = config != null ? Mathf.Max(0f, config.fireInterval) : 0f;
        if (fireInterval > 0f && Time.time < nextFireTime)
            return false;

        if (!HasAmmo(config))
            return false;

        if (playerAnim == null || !playerAnim.TryPlayShootAnim())
            return false;

        nextFireTime = Time.time + fireInterval;
        BeginFire(config);
        return true;
    }

    bool TryFireChargedShot(WeaponFireConfig config)
    {
        float fireInterval = config != null ? Mathf.Max(0f, config.fireInterval) : 0f;
        if (fireInterval > 0f && Time.time < nextFireTime)
        {
            playerAnim?.CancelCharge();
            return false;
        }

        // 先扣弹再播释放动画，避免空放
        if (!TryConsumeAmmo(config))
        {
            playerAnim?.CancelCharge();
            return false;
        }

        if (playerAnim == null || !playerAnim.ReleaseMachinistCharge())
        {
            playerAnim?.CancelCharge();
            return false;
        }

        nextFireTime = Time.time + fireInterval;
        if (playerMovement != null && playerMovement.GetShootLookDown())
            playerMovement.NotifyAirHangFromDownShot();

        FireDir dir = ResolveFireDir();
        Fire(dir, 0f, config, config != null ? config.chargedProjectilePrefab : null);
        return true;
    }

    bool ShouldForceCancelCharge(WeaponFireConfig config)
    {
        if (chargePhase == ChargeShootPhase.Idle)
            return false;

        if (playerMovement != null && playerMovement.IsActionLocked)
            return true;
        if (playerAnim != null && (playerAnim.IsRolling || playerAnim.IsSwitchingWeapon || playerAnim.IsDead))
            return true;

        int weaponId = weaponController != null ? weaponController.CurrentWeaponId : 0;
        if (chargeWeaponId >= 0 && chargeWeaponId != weaponId)
            return true;

        if (config == null || !config.chargeOnHold)
            return true;

        return false;
    }

    void CancelChargeShot(bool playRelease)
    {
        if (chargePhase == ChargeShootPhase.Idle && (playerAnim == null || !playerAnim.IsCharging))
            return;

        chargePhase = ChargeShootPhase.Idle;
        chargeWeaponId = -1;

        if (playerAnim == null)
            return;

        if (playRelease && playerAnim.IsCharging)
            playerAnim.ReleaseMachinistCharge();
        else
            playerAnim.CancelCharge();
    }

    void UpdateSpinUpWeapon(WeaponFireConfig config)
    {
        if (isSpinningUp)
        {
            if (!actions.Player.Attack.IsPressed())
            {
                ExitSpinUp();
                return;
            }

            UpdateSpinUpFiring(config);
            return;
        }

        if (playerMovement.IsActionLocked)
            return;

        if (playerAnim != null && playerAnim.IsRolling)
            return;

        if (playerAnim != null && playerAnim.IsSwitchingWeapon)
            return;

        if (playerAnim != null && playerAnim.IsMelee)
            return;

        if (actions.Player.Attack.WasPressedThisFrame())
        {
            if (playerMelee != null && playerMelee.IsEnemyInMeleeRange()
                && playerMelee.TryMelee())
                return;

            float fireInterval = Mathf.Max(0f, config.fireInterval);
            if (fireInterval > 0f && Time.time < nextFireTime)
                return;

            if (burstRoutine != null)
                return;

            if (!HasAmmo(config))
                return;

            if (!playerAnim.TryPlayShootAnim())
                return;

            attackHeld = true;
            attackPressTime = Time.time;
            nextFireTime = Time.time + fireInterval;
            if (playerMovement.GetShootLookDown())
                playerMovement.NotifyAirHangFromDownShot();
            BeginFire(config);
            return;
        }

        if (!attackHeld)
            return;

        if (!actions.Player.Attack.IsPressed())
        {
            attackHeld = false;
            return;
        }

        float threshold = Mathf.Max(0f, config.spinUpHoldThreshold);
        if (burstRoutine != null)
            return;

        if (Time.time - attackPressTime < threshold)
            return;

        EnterSpinUp(config);
    }

    bool ShouldForceEndSpinUp()
    {
        if (!isSpinningUp && !attackHeld)
            return false;

        if (playerMovement != null && playerMovement.IsActionLocked)
            return true;
        if (playerAnim != null && (playerAnim.IsRolling || playerAnim.IsSwitchingWeapon || playerAnim.IsDead))
            return true;

        int weaponId = weaponController != null ? weaponController.CurrentWeaponId : 0;
        if (spinUpConfig != null && spinUpConfig.weaponId != weaponId)
            return true;

        WeaponFireConfig current = ResolveFireConfig();
        if (current == null || !current.spinUpOnHold)
            return true;

        return false;
    }

    void EnterSpinUp(WeaponFireConfig config)
    {
        isSpinningUp = true;
        attackHeld = true;
        spinUpConfig = config;
        spinUpStartTime = Time.time;
        spinNextFireTime = Time.time;

        if (playerAnim != null)
        {
            playerAnim.SetHeavySpinFiring(true);
            playerAnim.SetSustainShoot(true);
            if (!playerAnim.IsShooting)
                playerAnim.TryPlayShootAnim();
        }
    }

    void ExitSpinUp()
    {
        bool wasSpinning = isSpinningUp;
        isSpinningUp = false;
        attackHeld = false;
        spinUpConfig = null;

        if (playerAnim != null)
        {
            playerAnim.SetHeavySpinFiring(false);
            if (wasSpinning)
                playerAnim.SetSustainShoot(false);
        }
    }

    void UpdateSpinUpFiring(WeaponFireConfig config)
    {
        if (playerAnim != null && !playerAnim.IsShooting)
            playerAnim.TryPlayShootAnim();

        float interval = ResolveSpinUpInterval(config);
        if (Time.time < spinNextFireTime)
            return;

        if (!HasAmmo(config))
        {
            ExitSpinUp();
            return;
        }

        float spreadOffset = Mathf.Max(0f, config.spreadOffset);
        if (!FireOnce(spreadOffset, config))
        {
            ExitSpinUp();
            return;
        }

        spinNextFireTime = Time.time + interval;
        nextFireTime = spinNextFireTime;
    }

    float ResolveSpinUpInterval(WeaponFireConfig config)
    {
        float start = Mathf.Max(0.01f, config.spinUpStartInterval);
        float min = Mathf.Max(0.01f, config.spinUpMinInterval);
        if (min > start)
            min = start;

        float ramp = Mathf.Max(0.01f, config.spinUpRampDuration);
        float t = Mathf.Clamp01((Time.time - spinUpStartTime) / ramp);
        return Mathf.Lerp(start, min, t);
    }

    bool ShouldForceEndLaser()
    {
        if (activeLaser == null)
            return false;

        if (playerMovement != null && playerMovement.IsActionLocked)
            return true;
        if (playerAnim != null && (playerAnim.IsRolling || playerAnim.IsSwitchingWeapon || playerAnim.IsDead || playerAnim.IsMelee))
            return true;
        if (playerMelee != null && playerMelee.IsEnemyInMeleeRange() && actions.Player.Attack.WasPressedThisFrame())
            return true;

        return false;
    }

    void UpdateHoldLaser(WeaponFireConfig config)
    {
        if (playerMovement.IsActionLocked || (playerAnim != null && (playerAnim.IsRolling || playerAnim.IsSwitchingWeapon || playerAnim.IsMelee)))
        {
            EndLaser(immediate: false);
            return;
        }

        bool pressed = actions.Player.Attack.IsPressed();
        if (!pressed)
        {
            EndLaser(immediate: false);
            return;
        }

        if (playerMelee != null && playerMelee.IsEnemyInMeleeRange() && activeLaser == null
            && actions.Player.Attack.WasPressedThisFrame()
            && playerMelee.TryMelee())
            return;

        if (activeLaser == null)
        {
            if (!TryBeginLaser(config))
                return;
        }

        if (activeLaser == null || activeLaser.IsEnding)
            return;

        if (!TryDrainHoldAmmo(config))
        {
            EndLaser(immediate: false);
            return;
        }

        FireDir dir = ResolveFireDir();
        Transform point = GetFirePoint(dir);
        float faceY = playerMovement.FaceDirection > 0f ? 0f : 180f;
        activeLaser.UpdateBeam(point, dir, faceY);

        if (dir == FireDir.Down)
            playerMovement.NotifyAirHangFromDownShot();

        if (playerAnim != null && !playerAnim.IsShooting)
            playerAnim.TryPlayShootAnim();
    }

    bool TryBeginLaser(WeaponFireConfig config)
    {
        GameObject prefab = config != null ? config.projectilePrefab : null;
        if (prefab == null)
            return false;

        if (!TryConsumeAmmo(config))
            return false;

        if (playerAnim == null || !playerAnim.TryPlayShootAnim())
            return false;

        playerAnim.SetSustainShoot(true);

        FireDir dir = ResolveFireDir();
        if (dir == FireDir.Down)
            playerMovement.NotifyAirHangFromDownShot();

        Transform point = GetFirePoint(dir);
        float faceY = playerMovement.FaceDirection > 0f ? 0f : 180f;
        var instance = Instantiate(prefab, point.position, Quaternion.identity);
        activeLaser = instance.GetComponent<PlayerLaserBeam>();
        if (activeLaser == null)
        {
            Debug.LogError($"Laser prefab '{prefab.name}' is missing PlayerLaserBeam.", prefab);
            Destroy(instance);
            playerAnim.SetSustainShoot(false);
            return false;
        }

        activeLaser.Begin(point, dir, faceY, character);
        float holdInterval = config != null ? Mathf.Max(0f, config.holdAmmoInterval) : 0f;
        laserNextAmmoTime = holdInterval > 0f ? Time.time + holdInterval : float.PositiveInfinity;
        return true;
    }

    /// <summary>镭射持续按住时的周期性耗弹；弹药不足返回 false。</summary>
    bool TryDrainHoldAmmo(WeaponFireConfig config)
    {
        float holdInterval = config != null ? Mathf.Max(0f, config.holdAmmoInterval) : 0f;
        if (holdInterval <= 0f)
            return true;

        while (Time.time >= laserNextAmmoTime)
        {
            if (!TryConsumeAmmo(config))
                return false;
            laserNextAmmoTime += holdInterval;
        }

        return true;
    }

    void EndLaser(bool immediate)
    {
        if (playerAnim != null)
            playerAnim.SetSustainShoot(false);

        if (activeLaser == null)
            return;

        var beam = activeLaser;
        activeLaser = null;

        if (immediate || beam == null)
        {
            if (beam != null)
                Destroy(beam.gameObject);
            return;
        }

        if (!beam.IsEnding)
            beam.BeginEnd();
    }

    void BeginFire(WeaponFireConfig config)
    {
        int burstCount = config != null ? Mathf.Max(1, config.burstCount) : 1;
        float burstInterval = config != null ? Mathf.Max(0f, config.burstInterval) : 0.06f;
        float spreadOffset = config != null ? Mathf.Max(0f, config.spreadOffset) : 0f;

        if (burstCount <= 1)
        {
            FireOnce(spreadOffset, config);
            return;
        }

        burstRoutine = StartCoroutine(BurstFire(burstCount, burstInterval, spreadOffset, config));
    }

    IEnumerator BurstFire(int burstCount, float burstInterval, float spreadOffset, WeaponFireConfig config)
    {
        for (int i = 0; i < burstCount; i++)
        {
            if (playerMovement != null && playerMovement.IsActionLocked)
                break;

            if (playerAnim != null && playerAnim.IsRolling)
                break;

            if (!FireOnce(spreadOffset, config))
                break;

            if (i < burstCount - 1 && burstInterval > 0f)
                yield return new WaitForSeconds(burstInterval);
        }

        burstRoutine = null;
    }

    void StopBurst()
    {
        if (burstRoutine == null)
            return;

        StopCoroutine(burstRoutine);
        burstRoutine = null;
    }

    bool FireOnce(float spreadOffset, WeaponFireConfig config)
    {
        if (!TryConsumeAmmo(config))
            return false;

        FireDir dir = ResolveFireDir();
        float offset = NextSpreadOffset(spreadOffset);
        Fire(dir, offset, config);
        return true;
    }

    float NextSpreadOffset(float spreadOffset)
    {
        if (spreadOffset <= 0f)
            return 0f;

        float minDelta = spreadOffset * 0.25f;
        float offset = UnityEngine.Random.Range(-spreadOffset, spreadOffset);

        if (Mathf.Abs(offset - lastSpreadOffset) < minDelta)
        {
            float flipped = -lastSpreadOffset;
            if (Mathf.Abs(flipped) < 0.001f)
                flipped = lastSpreadOffset >= 0f ? -spreadOffset * 0.5f : spreadOffset * 0.5f;
            offset = Mathf.Clamp(flipped, -spreadOffset, spreadOffset);
        }

        lastSpreadOffset = offset;
        return offset;
    }

    static Vector3 SpreadPositionDelta(FireDir dir, float offset)
    {
        // 水平/蹲射：上下；仰俯射：左右。方向不变。
        return dir switch
        {
            FireDir.Up or FireDir.Down => new Vector3(offset, 0f, 0f),
            _ => new Vector3(0f, offset, 0f),
        };
    }

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

    void Fire(FireDir dir, float spreadOffset, WeaponFireConfig config, GameObject prefabOverride = null)
    {
        GameObject prefab = prefabOverride;
        if (prefab == null)
            prefab = config != null ? config.projectilePrefab : null;
        if (prefab == null)
            prefab = projectilePrefab;
        if (prefab == null)
            return;

        Transform point = GetFirePoint(dir);
        Vector3 spawnPos = point.position + SpreadPositionDelta(dir, spreadOffset);
        float faceY = playerMovement.FaceDirection > 0f ? 0f : 180f;
        var instance = Instantiate(prefab, spawnPos, Quaternion.identity);
        var ammo = instance.GetComponent<IPlayerAmmo>();
        if (ammo == null)
        {
            Debug.LogError($"Projectile prefab '{prefab.name}' is missing IPlayerAmmo.", prefab);
            Destroy(instance);
            return;
        }

        ammo.Init(dir, faceY, character);
    }

    /// <summary>
    /// weaponId：0 无限手枪；1/2/3 → BulletS/M/L（与 Character.TryAmmoFromWeaponId 一致）。
    /// </summary>
    static bool TryResolveAmmoType(int weaponId, out AmmoType ammoType) =>
        Character.TryAmmoFromWeaponId(weaponId, out ammoType);

    int ResolveAmmoCost(WeaponFireConfig config)
    {
        if (config == null)
            return 0;
        if (!TryResolveAmmoType(config.weaponId, out _))
            return 0;
        return Mathf.Max(0, config.ammoCost);
    }

    bool HasAmmo(WeaponFireConfig config)
    {
        int cost = ResolveAmmoCost(config);
        if (cost <= 0)
            return true;
        if (character == null || !TryResolveAmmoType(config.weaponId, out AmmoType type))
            return false;

        return type switch
        {
            AmmoType.S => character.BulletS >= cost,
            AmmoType.M => character.BulletM >= cost,
            AmmoType.L => character.BulletL >= cost,
            _ => false,
        };
    }

    bool TryConsumeAmmo(WeaponFireConfig config)
    {
        int cost = ResolveAmmoCost(config);
        if (cost <= 0)
            return true;
        if (character == null || !TryResolveAmmoType(config.weaponId, out AmmoType type))
            return false;

        return character.TrySpendAmmo(type, cost);
    }

    WeaponFireConfig ResolveFireConfig()
    {
        int weaponId = weaponController != null ? weaponController.CurrentWeaponId : 0;
        if (fireConfigs == null)
            return null;

        for (int i = 0; i < fireConfigs.Length; i++)
        {
            var config = fireConfigs[i];
            if (config != null && config.weaponId == weaponId)
                return config;
        }

        return null;
    }

    Transform GetFirePoint(FireDir dir)
    {
        int weaponId = weaponController != null ? weaponController.CurrentWeaponId : 0;
        WeaponFirePointSet set = ResolveFirePointSet(weaponId);

        Transform point = dir switch
        {
            FireDir.Forward => set.forwardPoint,
            FireDir.Crouch => set.crouchPoint,
            FireDir.Up => set.upPoint,
            FireDir.Down => set.downPoint,
            _ => null,
        };

        return point != null ? point : transform;
    }

    WeaponFirePointSet ResolveFirePointSet(int weaponId)
    {
        WeaponFirePointSet matched = FindFirePointSet(weaponId);
        if (matched != null)
            return matched;

        if (weaponId != 0)
        {
            WeaponFirePointSet pistol = FindFirePointSet(0);
            if (pistol != null)
                return pistol;
        }

        return new WeaponFirePointSet
        {
            weaponId = 0,
            forwardPoint = forwardPoint,
            crouchPoint = crouchPoint,
            upPoint = upPoint,
            downPoint = downPoint,
        };
    }

    WeaponFirePointSet FindFirePointSet(int weaponId)
    {
        if (firePointSets == null)
            return null;

        for (int i = 0; i < firePointSets.Length; i++)
        {
            var set = firePointSets[i];
            if (set != null && set.weaponId == weaponId)
                return set;
        }

        return null;
    }
}
