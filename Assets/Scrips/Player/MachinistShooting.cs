using UnityEngine;
using UnityEngine.InputSystem;

[DefaultExecutionOrder(100)]
[RequireComponent(typeof(PlayerAnim))]
[RequireComponent(typeof(PlayerMovement))]
public class MachinistShooting : MonoBehaviour
{
    enum ShootPhase { Idle, Pressing, Charging }

    [Header("子弹")]
    [SerializeField] PlayerProjectile normalProjectilePrefab;
    [SerializeField] PlayerProjectile comboProjectilePrefab;
    [SerializeField] PlayerProjectile chargeProjectilePrefab;

    [Header("发射点")]
    [SerializeField] Transform forwardPoint;
    [SerializeField] Transform crouchPoint;
    [SerializeField] Transform upPoint;
    [SerializeField] Transform downPoint;

    [Header("连击")]
    [SerializeField] int comboFinisherCount = 4;
    [SerializeField] float comboResetWindow = 0.8f;

    [Header("蓄力")]
    [SerializeField] float chargeHoldThreshold = 0.3f;

    InputSystem_Actions actions;
    PlayerAnim playerAnim;
    PlayerMovement playerMovement;

    ShootPhase phase = ShootPhase.Idle;
    float pressTime;
    int comboCount;
    float lastShotTime;

    void Awake()
    {
        actions = new InputSystem_Actions();
        playerAnim = GetComponent<PlayerAnim>();
        playerMovement = GetComponent<PlayerMovement>();
    }

    void OnEnable() => actions.Player.Enable();

    void OnDisable() => actions.Player.Disable();

    void OnDestroy() => actions?.Dispose();

    void Update()
    {
        if (playerMovement.IsActionLocked)
            return;

        switch (phase)
        {
            case ShootPhase.Idle:
                if (playerAnim.IsPlayingMachinistComboShoot)
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

    bool IsComboExpired() =>
        comboCount > 0 && Time.time - lastShotTime > comboResetWindow;

    void FireTapShot()
    {
        if (IsComboExpired())
            comboCount = 0;

        comboCount++;
        var kind = comboCount >= comboFinisherCount ? MachinistShootKind.Combo : MachinistShootKind.Normal;

        if (!playerAnim.TryPlayMachinistShootAnim(kind))
        {
            comboCount--;
            return;
        }

        lastShotTime = Time.time;
        var prefab = kind == MachinistShootKind.Combo ? comboProjectilePrefab : normalProjectilePrefab;
        Fire(ResolveFireDir(), prefab);

        if (kind == MachinistShootKind.Combo)
            comboCount = 0;
    }

    void FireChargeShot()
    {
        comboCount = 0;
        if (!playerAnim.ReleaseMachinistCharge())
            return;

        Fire(ResolveFireDir(), chargeProjectilePrefab);
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

    void Fire(FireDir dir, PlayerProjectile prefab)
    {
        if (prefab == null)
            return;

        Transform point = GetFirePoint(dir);
        float faceY = playerMovement.FaceDirection > 0f ? 0f : 180f;
        var projectile = Instantiate(prefab, point.position, Quaternion.identity);
        projectile.Init(dir, faceY);
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
