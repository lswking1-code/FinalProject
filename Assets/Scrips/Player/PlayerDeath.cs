using FMODUnity;
using UnityEngine;

[RequireComponent(typeof(Character))]
[RequireComponent(typeof(PlayerMovement))]
public class PlayerDeath : MonoBehaviour
{
    [Header("事件")]
    [SerializeField] VoidEventSO gameOverEvent;
    [SerializeField] VoidEventSO loadDataEvent;
    [SerializeField] VoidEventSO newGameEvent;
    [SerializeField] VoidEventSO backToMenuEvent;

    [Header("音效")]
    [SerializeField] EventReference hitEvent;
    [SerializeField] EventReference dieEvent;

    Character character;
    PlayerAnimBase playerAnim;
    PlayerMovement playerMovement;

    bool deathHandled;
    bool gameOverRaised;

    public bool IsDeathHandled => deathHandled;

    void Awake()
    {
        character = GetComponent<Character>();
        playerAnim = PlayerAnimBase.Resolve(gameObject);
        playerMovement = GetComponent<PlayerMovement>();

        if (character != null)
            character.OnTakeDamage.AddListener(OnTakeDamage);

        // 回菜单会 SetActive(false)，必须用 Awake/OnDestroy 订阅，否则会错过 newGame
        if (loadDataEvent != null)
            loadDataEvent.OnEventRaised += Revive;
        if (newGameEvent != null)
            newGameEvent.OnEventRaised += ResetForNewGame;
        if (backToMenuEvent != null)
            backToMenuEvent.OnEventRaised += ResetForNewGame;
    }

    void OnDestroy()
    {
        if (character != null)
            character.OnTakeDamage.RemoveListener(OnTakeDamage);

        if (loadDataEvent != null)
            loadDataEvent.OnEventRaised -= Revive;
        if (newGameEvent != null)
            newGameEvent.OnEventRaised -= ResetForNewGame;
        if (backToMenuEvent != null)
            backToMenuEvent.OnEventRaised -= ResetForNewGame;
    }

    void OnEnable()
    {
        // 重新启用时若仍卡在死亡态，补一次重置
        if (deathHandled || (character != null && character.IsDead))
            ResetForNewGame();
    }

    void Update()
    {
        if (!deathHandled || gameOverRaised)
            return;

        if (playerAnim.TryGetDieAnimProgress(out float normalizedTime) && normalizedTime >= 1f)
            ResolveDeath();
    }

    public void HandleDeath()
    {
        if (deathHandled)
            return;

        deathHandled = true;
        gameOverRaised = false;
        PlaySessionRecorder.Instance?.RecordDeath();
        character.SetForcedInvulnerable(true);
        playerMovement.BeginExternalControl();
        playerAnim.PlayDieAnim();
        if (!dieEvent.IsNull)
            FmodAudio.Play(dieEvent);
    }

    void OnTakeDamage(Transform _)
    {
        if (hitEvent.IsNull || character == null || character.currentHealth <= 0)
            return;

        FmodAudio.Play(hitEvent);
    }

    public void OnDeathAnimationFinished()
    {
        if (!deathHandled || gameOverRaised)
            return;

        ResolveDeath();
    }

    void ResolveDeath()
    {
        if (gameOverRaised)
            return;

        gameOverRaised = true;

        if (HasPlayerCheckpoint() && PlayerLifePoints.Instance != null && PlayerLifePoints.Instance.TryConsume())
        {
            loadDataEvent?.RaiseEvent();
            return;
        }

        gameOverEvent?.RaiseEvent();
    }

    bool HasPlayerCheckpoint()
    {
        if (DataManager.instance == null || character == null)
            return false;

        var def = character.GetDataID();
        if (def == null || string.IsNullOrEmpty(def.ID))
            return false;

        return DataManager.instance.HasPlayerCheckpoint(def.ID);
    }

    /// <summary>读档复活：清死亡态，血量由 Character.LoadSaveData 覆盖。</summary>
    public void Revive()
    {
        deathHandled = false;
        gameOverRaised = false;
        character?.Revive();
        playerAnim?.ResetFromDeath();
        playerMovement?.EndExternalControl();
    }

    /// <summary>新游戏 / 回菜单：清死亡态并回满基础属性。</summary>
    public void ResetForNewGame()
    {
        Revive();
        character?.ResetForNewGame();
    }
}
