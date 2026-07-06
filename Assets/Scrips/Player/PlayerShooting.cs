using UnityEngine;
using UnityEngine.InputSystem;

public enum FireDir // 射击朝向
{
    Up,
    Down,
    Forward,
    Crouch,
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

    InputSystem_Actions actions;
    PlayerAnim playerAnim;
    PlayerMovement playerMovement;
    PlayerMelee playerMelee;

    void Awake()
    {
        actions = new InputSystem_Actions();
        playerAnim = GetComponent<PlayerAnim>();
        playerMovement = GetComponent<PlayerMovement>();
        playerMelee = GetComponent<PlayerMelee>();
    }

    void OnEnable() => actions.Player.Enable();

    void OnDisable() => actions.Player.Disable();

    void OnDestroy() => actions?.Dispose();

    void Update()
    {
        if (playerMovement.IsActionLocked)
            return;

        if (actions.Player.Attack.WasPressedThisFrame())
        {
            if (playerMelee != null && playerMelee.IsEnemyInMeleeRange()
                && playerMelee.TryMelee())
                return;

            if (playerAnim.TryPlayShootAnim())
                Fire(ResolveFireDir());
        }
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

    void Fire(FireDir dir)
    {
        if (projectilePrefab == null)
            return;

        Transform point = GetFirePoint(dir);
        float faceY = playerMovement.FaceDirection > 0f ? 0f : 180f;
        var projectile = Instantiate(projectilePrefab, point.position, Quaternion.identity);
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
