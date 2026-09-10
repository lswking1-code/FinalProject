using UnityEngine;

[DefaultExecutionOrder(100)]
[RequireComponent(typeof(PlayerAnimBase))]
[RequireComponent(typeof(PlayerMovement))]
public class PlayerThrow : MonoBehaviour
{
    [SerializeField] PlayerGrenade grenadePrefab;
    [SerializeField] Transform standingThrowPoint;
    [SerializeField] Transform crouchThrowPoint;

    PlayerAnimBase playerAnim;
    PlayerMovement playerMovement;
    Collider2D playerCollider;
    Rigidbody2D playerRb;

    void Awake()
    {
        playerAnim = PlayerAnimBase.Resolve(gameObject);
        playerMovement = GetComponent<PlayerMovement>();
        playerCollider = GetComponent<Collider2D>();
        playerRb = GetComponent<Rigidbody2D>();
    }

    public bool TryThrowGrenade()
    {
        if (playerMovement.IsActionLocked || playerAnim.IsRolling)
            return false;

        if (grenadePrefab == null)
            return false;

        if (!playerAnim.TryPlayThrowAnim())
            return false;

        Transform point = playerAnim.IsCrouching ? crouchThrowPoint : standingThrowPoint;
        if (point == null)
            point = transform;

        var grenade = Instantiate(grenadePrefab, point.position, Quaternion.identity);
        grenade.Init(playerMovement.FaceDirection, playerRb.linearVelocity, playerCollider);
        return true;
    }
}
