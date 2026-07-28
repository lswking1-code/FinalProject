using UnityEngine;

[RequireComponent(typeof(Character))]
[RequireComponent(typeof(PlayerMovement))]
public class PlayerDeath : MonoBehaviour
{
    [Header("事件")]
    [SerializeField] VoidEventSO gameOverEvent;
    [SerializeField] VoidEventSO loadDataEvent;
    [SerializeField] VoidEventSO newGameEvent;

    Character character;
    PlayerAnimBase playerAnim;
    PlayerMovement playerMovement;

    bool deathHandled;
    bool gameOverRaised;

    public bool IsDeathHandled => deathHandled;

    void Awake()
    {
        character = GetComponent<Character>();
        playerAnim = GetComponent<PlayerAnimBase>();
        playerMovement = GetComponent<PlayerMovement>();
    }

    void OnEnable()
    {
        if (loadDataEvent != null)
            loadDataEvent.OnEventRaised += Revive;
        if (newGameEvent != null)
            newGameEvent.OnEventRaised += Revive;
    }

    void OnDisable()
    {
        if (loadDataEvent != null)
            loadDataEvent.OnEventRaised -= Revive;
        if (newGameEvent != null)
            newGameEvent.OnEventRaised -= Revive;
    }

    void Update()
    {
        if (!deathHandled || gameOverRaised)
            return;

        if (playerAnim.TryGetDieAnimProgress(out float normalizedTime) && normalizedTime >= 1f)
            RaiseGameOver();
    }

    public void HandleDeath()
    {
        if (deathHandled)
            return;

        deathHandled = true;
        gameOverRaised = false;
        character.SetForcedInvulnerable(true);
        playerMovement.BeginExternalControl();
        playerAnim.PlayDieAnim();
    }

    public void OnDeathAnimationFinished()
    {
        if (!deathHandled || gameOverRaised)
            return;

        RaiseGameOver();
    }

    void RaiseGameOver()
    {
        if (gameOverRaised)
            return;

        gameOverRaised = true;
        gameOverEvent?.RaiseEvent();
    }

    void Revive()
    {
        deathHandled = false;
        gameOverRaised = false;
        character.Revive();
        playerAnim.ResetFromDeath();
        playerMovement.EndExternalControl();
    }
}
