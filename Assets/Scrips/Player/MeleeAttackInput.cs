using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 近战角色攻击输入：范围内按 Attack 触发 <see cref="PlayerMelee"/>。勿与 <see cref="PlayerShooting"/> 同挂。
/// </summary>
[DefaultExecutionOrder(100)]
[RequireComponent(typeof(PlayerAnimBase))]
[RequireComponent(typeof(PlayerMovement))]
[RequireComponent(typeof(PlayerMelee))]
public class MeleeAttackInput : MonoBehaviour
{
    PlayerMelee playerMelee;
    PlayerMovement playerMovement;
    InputSystem_Actions actions;

    void Awake()
    {
        playerMelee = GetComponent<PlayerMelee>();
        playerMovement = GetComponent<PlayerMovement>();
        actions = new InputSystem_Actions();
    }

    void OnEnable() => actions.Player.Enable();

    void OnDisable() => actions.Player.Disable();

    void OnDestroy() => actions?.Dispose();

    void Update()
    {
        if (playerMovement.IsActionLocked)
            return;

        if (!actions.Player.Attack.WasPressedThisFrame())
            return;

        if (playerMelee != null && playerMelee.IsEnemyInMeleeRange())
            playerMelee.TryMelee();
    }
}
