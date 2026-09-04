using System.Collections.Generic;
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

    [Header("复活")]
    [Tooltip("有命即时复活 / 回存档点后的无敌时长（秒）。")]
    [SerializeField, Min(0f)] float reviveInvulnerableDuration = 2f;
    [Tooltip("无敌白闪切换间隔（秒）。")]
    [SerializeField, Min(0.02f)] float reviveFlashInterval = 0.1f;

    [Header("音效")]
    [SerializeField] EventReference hitEvent;
    [SerializeField] EventReference dieEvent;

    Character character;
    PlayerAnimBase playerAnim;
    PlayerMovement playerMovement;
    PhysicsCheck physicsCheck;
    Rigidbody2D rb;

    bool deathHandled;
    bool gameOverRaised;

    Vector3 lastSafePosition;
    bool hasLastSafe;

    bool flashing;
    float flashRemaining;
    float flashToggleTimer;
    bool flashWhite;
    SpriteRenderer[] flashRenderers;
    Color[] flashOriginals;

    static readonly List<Collider2D> overlapBuffer = new();

    public bool IsDeathHandled => deathHandled;

    void Awake()
    {
        character = GetComponent<Character>();
        playerAnim = PlayerAnimBase.Resolve(gameObject);
        playerMovement = GetComponent<PlayerMovement>();
        physicsCheck = GetComponent<PhysicsCheck>();
        rb = GetComponent<Rigidbody2D>();

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
        {
            ResetForNewGame();
            return;
        }

        // 读档切场景会 SetActive(false)，无敌白闪需在重新启用后接上
        if (character != null && character.invulnerable)
        {
            character.SetForcedInvulnerable(true);
            StartWhiteFlash(reviveInvulnerableDuration);
        }
    }

    void OnDisable()
    {
        bool wasFlashing = flashing;
        StopWhiteFlash();
        if (wasFlashing)
            character?.SetForcedInvulnerable(false);
    }

    void Update()
    {
        UpdateWhiteFlash();

        if (!deathHandled || gameOverRaised)
            return;

        if (playerAnim.TryGetDieAnimProgress(out float normalizedTime) && normalizedTime >= 1f)
            ResolveDeath();
    }

    void FixedUpdate()
    {
        if (deathHandled || (character != null && character.IsDead))
            return;

        if (physicsCheck != null && physicsCheck.isSolidGround)
        {
            lastSafePosition = transform.position;
            hasLastSafe = true;
        }
    }

    public void HandleDeath()
    {
        if (deathHandled)
            return;

        // 必须在 BeginExternalControl 关 Collider 之前采样
        bool offSafeGround = IsOffSafeGround();

        PlaySessionRecorder.Instance?.RecordDeath();

        if (PlayerLifePoints.Instance != null && PlayerLifePoints.Instance.TryConsume())
        {
            ReviveFromLifePoint(offSafeGround);
            return;
        }

        deathHandled = true;
        gameOverRaised = false;
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

        if (HasPlayerCheckpoint())
        {
            loadDataEvent?.RaiseEvent();
            return;
        }

        if (TryGetSceneEntry(out Vector3 entry))
            ReviveAt(entry);
        else
            ReviveAt(transform.position);
    }

    void ReviveFromLifePoint(bool offSafeGround)
    {
        deathHandled = false;
        gameOverRaised = false;

        if (offSafeGround)
        {
            if (hasLastSafe)
            {
                ApplyFullRevive();
                TeleportTo(lastSafePosition);
                ApplyReviveInvulnerability();
                return;
            }

            if (HasPlayerCheckpoint())
            {
                loadDataEvent?.RaiseEvent();
                return;
            }
        }

        ApplyFullRevive();
        ApplyReviveInvulnerability();
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

    bool IsOffSafeGround()
    {
        if (OverlapsHazard())
            return true;

        return physicsCheck != null && !physicsCheck.isSolidGround;
    }

    bool OverlapsHazard()
    {
        var col = GetComponent<Collider2D>();
        if (col == null)
            return false;

        var filter = new ContactFilter2D();
        filter.NoFilter();
        filter.useTriggers = true;
        overlapBuffer.Clear();
        int count = col.Overlap(filter, overlapBuffer);
        for (int i = 0; i < count; i++)
        {
            Collider2D other = overlapBuffer[i];
            if (other == null)
                continue;
            if (other.CompareTag("Water"))
                return true;
            if (other.GetComponentInParent<DeathZone>() != null)
                return true;
        }

        return false;
    }

    bool TryGetSceneEntry(out Vector3 position)
    {
        var loader = FindFirstObjectByType<SceneLoader>();
        if (loader != null && loader.TryGetCurrentSceneEntry(out position))
            return true;

        position = default;
        return false;
    }

    void ReviveAt(Vector3 position)
    {
        ApplyFullRevive();
        TeleportTo(position);
        ApplyReviveInvulnerability();
    }

    void ApplyFullRevive()
    {
        deathHandled = false;
        gameOverRaised = false;
        StopWhiteFlash();
        character?.Revive();
        character?.RestoreFullHealth();
        playerAnim?.ResetFromDeath();
        ResetCombatState();
        playerMovement?.EndExternalControl();
    }

    void TeleportTo(Vector3 position)
    {
        if (rb != null)
        {
            rb.linearVelocity = Vector2.zero;
            rb.position = position;
        }

        transform.position = position;
        FindFirstObjectByType<CameraControl>()?.SnapCameraToFollowTarget();
    }

    /// <summary>读档复活：清死亡态，血量由 Character.LoadSaveData 覆盖。</summary>
    public void Revive()
    {
        deathHandled = false;
        gameOverRaised = false;
        StopWhiteFlash();
        character?.Revive();
        playerAnim?.ResetFromDeath();
        ResetCombatState();
        if (!IsSceneLoading())
            playerMovement?.EndExternalControl();
        ApplyReviveInvulnerability();
    }

    /// <summary>新游戏 / 回菜单：清死亡态并回满基础属性。</summary>
    public void ResetForNewGame()
    {
        deathHandled = false;
        gameOverRaised = false;
        StopWhiteFlash();
        character?.SetForcedInvulnerable(false);
        character?.Revive();
        playerAnim?.ResetFromDeath();
        ResetCombatState();
        playerMovement?.EndExternalControl();
        character?.ResetForNewGame();
    }

    void ResetCombatState()
    {
        GetComponent<PlayerShooting>()?.ResetCombatState();
        GetComponent<MachinistShooting>()?.ResetCombatState();
    }

    static bool IsSceneLoading()
    {
        var loader = FindFirstObjectByType<SceneLoader>();
        return loader != null && loader.IsLoading;
    }

    void ApplyReviveInvulnerability()
    {
        if (character == null)
            return;

        float duration = reviveInvulnerableDuration;
        if (duration <= 0f)
            return;

        character.TriggerInvulnerable(duration);
        character.SetForcedInvulnerable(true);
        StartWhiteFlash(duration);
    }

    void StartWhiteFlash(float duration)
    {
        StopWhiteFlash();

        flashRenderers = GetComponentsInChildren<SpriteRenderer>(true);
        if (flashRenderers == null || flashRenderers.Length == 0)
            return;

        flashOriginals = new Color[flashRenderers.Length];
        for (int i = 0; i < flashRenderers.Length; i++)
        {
            if (flashRenderers[i] != null)
                flashOriginals[i] = flashRenderers[i].color;
        }

        flashing = true;
        flashRemaining = duration;
        flashToggleTimer = 0f;
        flashWhite = true;
        ApplyFlashColors();
    }

    void UpdateWhiteFlash()
    {
        if (!flashing)
            return;

        flashRemaining -= Time.deltaTime;
        flashToggleTimer += Time.deltaTime;
        if (flashToggleTimer >= reviveFlashInterval)
        {
            flashToggleTimer = 0f;
            flashWhite = !flashWhite;
            ApplyFlashColors();
        }

        if (flashRemaining <= 0f)
        {
            StopWhiteFlash();
            character?.SetForcedInvulnerable(false);
        }
    }

    void ApplyFlashColors()
    {
        if (flashRenderers == null)
            return;

        for (int i = 0; i < flashRenderers.Length; i++)
        {
            SpriteRenderer renderer = flashRenderers[i];
            if (renderer == null)
                continue;

            renderer.color = flashWhite ? Color.white : FlashOffColor(flashOriginals[i]);
        }
    }

    static Color FlashOffColor(Color original)
    {
        // 原色接近白时略压暗，否则白闪看不出来
        if (original.r > 0.85f && original.g > 0.85f && original.b > 0.85f)
            return new Color(0.55f, 0.55f, 0.55f, original.a);

        return original;
    }

    void StopWhiteFlash()
    {
        if (!flashing)
            return;

        if (flashRenderers != null && flashOriginals != null)
        {
            int count = Mathf.Min(flashRenderers.Length, flashOriginals.Length);
            for (int i = 0; i < count; i++)
            {
                if (flashRenderers[i] != null)
                    flashRenderers[i].color = flashOriginals[i];
            }
        }

        flashing = false;
        flashRemaining = 0f;
        flashRenderers = null;
        flashOriginals = null;
    }
}
