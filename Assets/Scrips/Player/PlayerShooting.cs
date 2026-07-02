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
public class PlayerShooting : MonoBehaviour
{
    InputSystem_Actions actions;
    PlayerAnim playerAnim;

    void Awake()
    {
        actions = new InputSystem_Actions();
        playerAnim = GetComponent<PlayerAnim>();
    }

    void OnEnable() => actions.Player.Enable();

    void OnDisable() => actions.Player.Disable();

    void OnDestroy() => actions?.Dispose();

    void Update()
    {
        if (actions.Player.Attack.WasPressedThisFrame())
            playerAnim.TryPlayShootAnim();
    }
}
