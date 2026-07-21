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

[DefaultExecutionOrder(100)]
[RequireComponent(typeof(PlayerAnim))]
[RequireComponent(typeof(PlayerMovement))]
[RequireComponent(typeof(PlayerMelee))]
public class PlayerShooting : MonoBehaviour
{
    [SerializeField] PlayerProjectile projectilePrefab;
    [SerializeField] Transform forwardPoint;
    [SerializeField] Transform crouchPoint;
    [SerializeField] Transform upPoint;
    [SerializeField] Transform downPoint;
    [SerializeField] WeaponFirePointSet[] firePointSets;

    InputSystem_Actions actions;
    PlayerAnim playerAnim;
    PlayerMovement playerMovement;
    PlayerMelee playerMelee;
    PlayerWeaponController weaponController;

    Coroutine burstRoutine;
    float lastSpreadOffset;

    void Awake()
    {
        actions = new InputSystem_Actions();
        playerAnim = GetComponent<PlayerAnim>();
        playerMovement = GetComponent<PlayerMovement>();
        playerMelee = GetComponent<PlayerMelee>();
        weaponController = GetComponent<PlayerWeaponController>();
    }

    void OnEnable() => actions.Player.Enable();

    void OnDisable()
    {
        actions.Player.Disable();
        StopBurst();
    }

    void OnDestroy() => actions?.Dispose();

    void Update()
    {
        if (playerMovement.IsActionLocked)
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

            if (playerAnim.TryPlayShootAnim())
                BeginFire();
        }
    }

    void BeginFire()
    {
        WeaponDefinition def = weaponController != null ? weaponController.CurrentDefinition : null;
        int burstCount = def != null ? Mathf.Max(1, def.burstCount) : 1;
        float burstInterval = def != null ? Mathf.Max(0f, def.burstInterval) : 0.06f;
        float spreadOffset = def != null ? Mathf.Max(0f, def.spreadOffset) : 0f;

        if (burstCount <= 1)
        {
            FireOnce(spreadOffset);
            return;
        }

        burstRoutine = StartCoroutine(BurstFire(burstCount, burstInterval, spreadOffset));
    }

    IEnumerator BurstFire(int burstCount, float burstInterval, float spreadOffset)
    {
        for (int i = 0; i < burstCount; i++)
        {
            if (playerMovement != null && playerMovement.IsActionLocked)
                break;

            FireOnce(spreadOffset);

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

    void FireOnce(float spreadOffset)
    {
        FireDir dir = ResolveFireDir();
        float offset = NextSpreadOffset(spreadOffset);
        Fire(dir, offset);
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

    void Fire(FireDir dir, float spreadOffset = 0f)
    {
        PlayerProjectile prefab = null;
        if (weaponController != null)
            prefab = weaponController.CurrentProjectilePrefab;
        if (prefab == null)
            prefab = projectilePrefab;
        if (prefab == null)
            return;

        Transform point = GetFirePoint(dir);
        Vector3 spawnPos = point.position + SpreadPositionDelta(dir, spreadOffset);
        float faceY = playerMovement.FaceDirection > 0f ? 0f : 180f;
        var projectile = Instantiate(prefab, spawnPos, Quaternion.identity);
        projectile.Init(dir, faceY);
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
