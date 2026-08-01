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
}

[DefaultExecutionOrder(100)]
[RequireComponent(typeof(PlayerAnim))]
[RequireComponent(typeof(PlayerMovement))]
[RequireComponent(typeof(PlayerMelee))]
public class PlayerShooting : MonoBehaviour
{
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

    void Awake()
    {
        actions = new InputSystem_Actions();
        playerAnim = GetComponent<PlayerAnimBase>();
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
        EndLaser(immediate: true);
    }

    void OnDestroy()
    {
        EndLaser(immediate: true);
        actions?.Dispose();
    }

    void Update()
    {
        WeaponFireConfig config = ResolveFireConfig();
        bool holdLaser = config != null && config.holdToFire;

        if (ShouldForceEndLaser())
        {
            EndLaser(immediate: false);
            if (!holdLaser || !actions.Player.Attack.IsPressed())
                return;
        }

        if (holdLaser)
        {
            UpdateHoldLaser(config);
            return;
        }

        if (activeLaser != null)
            EndLaser(immediate: false);

        if (playerMovement.IsActionLocked)
            return;

        if (playerAnim != null && playerAnim.IsRolling)
            return;

        if (playerAnim != null && playerAnim.IsSwitchingWeapon)
            return;

        if (burstRoutine != null)
            return;

        if (actions.Player.Attack.WasPressedThisFrame())
        {
            if (playerMelee != null && playerMelee.IsEnemyInMeleeRange()
                && playerMelee.TryMelee())
                return;

            float fireInterval = config != null ? Mathf.Max(0f, config.fireInterval) : 0f;
            if (fireInterval > 0f && Time.time < nextFireTime)
                return;

            if (playerAnim.TryPlayShootAnim())
            {
                nextFireTime = Time.time + fireInterval;
                BeginFire(config);
            }
        }
    }

    bool ShouldForceEndLaser()
    {
        if (activeLaser == null)
            return false;

        if (playerMovement != null && playerMovement.IsActionLocked)
            return true;
        if (playerAnim != null && (playerAnim.IsRolling || playerAnim.IsSwitchingWeapon || playerAnim.IsDead))
            return true;
        if (playerMelee != null && playerMelee.IsEnemyInMeleeRange() && actions.Player.Attack.WasPressedThisFrame())
            return true;

        return false;
    }

    void UpdateHoldLaser(WeaponFireConfig config)
    {
        if (playerMovement.IsActionLocked || (playerAnim != null && (playerAnim.IsRolling || playerAnim.IsSwitchingWeapon)))
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

        FireDir dir = ResolveFireDir();
        Transform point = GetFirePoint(dir);
        float faceY = playerMovement.FaceDirection > 0f ? 0f : 180f;
        activeLaser.UpdateBeam(point, dir, faceY);

        if (playerAnim != null && !playerAnim.IsShooting)
            playerAnim.TryPlayShootAnim();
    }

    bool TryBeginLaser(WeaponFireConfig config)
    {
        GameObject prefab = config != null ? config.projectilePrefab : null;
        if (prefab == null)
            return false;

        if (playerAnim == null || !playerAnim.TryPlayShootAnim())
            return false;

        playerAnim.SetSustainShoot(true);

        FireDir dir = ResolveFireDir();
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

            FireOnce(spreadOffset, config);

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

    void FireOnce(float spreadOffset, WeaponFireConfig config)
    {
        FireDir dir = ResolveFireDir();
        float offset = NextSpreadOffset(spreadOffset);
        Fire(dir, offset, config);
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

    void Fire(FireDir dir, float spreadOffset, WeaponFireConfig config)
    {
        GameObject prefab = config != null ? config.projectilePrefab : null;
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
