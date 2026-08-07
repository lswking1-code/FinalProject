using UnityEngine;
using UnityEngine.InputSystem;

[DefaultExecutionOrder(100)]
[RequireComponent(typeof(PlayerAnimBase))]
[RequireComponent(typeof(PlayerMovement))]
public class PlayerThrow : MonoBehaviour
{
    [SerializeField] PlayerGrenade grenadePrefab;
    [SerializeField] Transform standingThrowPoint;
    [SerializeField] Transform crouchThrowPoint;

    InputSystem_Actions actions;
    PlayerAnimBase playerAnim;
    PlayerMovement playerMovement;
    Collider2D playerCollider;
    Rigidbody2D playerRb;

    void Awake()
    {
        actions = new InputSystem_Actions();
        playerAnim = PlayerAnimBase.Resolve(gameObject);
        playerMovement = GetComponent<PlayerMovement>();
        playerCollider = GetComponent<Collider2D>();
        playerRb = GetComponent<Rigidbody2D>();
    }

    void OnEnable() => actions.Player.Enable();

    void OnDisable() => actions.Player.Disable();

    void OnDestroy() => actions?.Dispose();

    void Update()
    {
        if (playerMovement.IsActionLocked)
            return;

        if (playerAnim.IsRolling)
            return;

        if (actions.Player.Throw.WasPressedThisFrame()
            && playerAnim.TryPlayThrowAnim())
            SpawnGrenade();
    }

    void SpawnGrenade()
    {
        if (grenadePrefab == null)
            return;

        Transform point = playerAnim.IsCrouching ? crouchThrowPoint : standingThrowPoint;
        if (point == null)
            point = transform;

        var grenade = Instantiate(grenadePrefab, point.position, Quaternion.identity);
        grenade.Init(playerMovement.FaceDirection, playerRb.linearVelocity, playerCollider);
    }
}
