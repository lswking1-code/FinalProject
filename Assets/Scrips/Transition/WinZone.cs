using UnityEngine;

[RequireComponent(typeof(Collider2D))]
public class WinZone : MonoBehaviour, IInteractable
{
    public VoidEventSO gameClearEvent;

    bool triggered;
    Collider2D pendingPlayer;

    void Awake()
    {
        var col = GetComponent<Collider2D>();
        if (col != null && !col.isTrigger)
            Debug.LogWarning("WinZone: Collider2D 应勾选 Is Trigger。", this);
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (triggered || other == null)
            return;

        var character = other.GetComponentInParent<Character>();
        if (character == null || !character.CompareTag("Player") || character.IsDead)
            return;

        pendingPlayer = other;
        TriggerAction();
    }

    public void TriggerAction()
    {
        if (triggered)
            return;

        triggered = true;

        ResolvePlayer(out Character character, out PlayerMovement movement);

        if (character != null)
            character.SetForcedInvulnerable(true);
        if (movement != null)
            movement.BeginExternalControl(false);

        GameplayHold.Hold();
        gameClearEvent?.RaiseEvent();
    }

    void ResolvePlayer(out Character character, out PlayerMovement movement)
    {
        character = null;
        movement = null;

        if (pendingPlayer != null)
        {
            character = pendingPlayer.GetComponentInParent<Character>();
            movement = pendingPlayer.GetComponentInParent<PlayerMovement>();
            pendingPlayer = null;
        }

        if (character != null && movement != null)
            return;

        var player = GameObject.FindGameObjectWithTag("Player");
        if (player == null)
            return;

        if (character == null)
            character = player.GetComponent<Character>() ?? player.GetComponentInParent<Character>();
        if (movement == null)
            movement = player.GetComponent<PlayerMovement>() ?? player.GetComponentInParent<PlayerMovement>();
    }
}
