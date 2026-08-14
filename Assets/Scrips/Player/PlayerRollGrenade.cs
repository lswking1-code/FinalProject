using UnityEngine;

/// <summary>
/// 枪手 Ability1：短按投普通手雷；长按向前发射手雷弹；长按+下投放滚动炸弹；长按+上发射追踪导弹。
/// 仅挂在 Player prefab。
/// </summary>
[DefaultExecutionOrder(100)]
[RequireComponent(typeof(PlayerAnimBase))]
[RequireComponent(typeof(PlayerMovement))]
[RequireComponent(typeof(PlayerThrow))]
[RequireComponent(typeof(Character))]
public class PlayerRollGrenade : MonoBehaviour
{
    [Header("滚动炸弹（长按 + 下）")]
    [SerializeField] PlayerGrenade rollGrenadePrefab;
    [SerializeField] Transform throwPoint;
    [SerializeField] float abilityPowerCost = 20f;

    [Header("特殊弹（长按、无上下）")]
    [SerializeField] PlayerGrenadeBullet grenadeBulletPrefab;
    [SerializeField] Transform bulletFirePoint;
    [SerializeField] float bulletAbilityPowerCost = 20f;

    [Header("追踪导弹（长按 + 上）")]
    [SerializeField] PlayerHomingMissile missilePrefab;
    [SerializeField] Transform missileFirePoint;
    [SerializeField] float missileAbilityPowerCost = 20f;
    [SerializeField] float missileSpawnOffsetY = 1.5f;

    [SerializeField] float holdThreshold = 0.2f;

    InputSystem_Actions actions;
    PlayerAnimBase playerAnim;
    PlayerMovement playerMovement;
    PlayerThrow playerThrow;
    Character character;
    Collider2D playerCollider;
    Rigidbody2D playerRb;

    bool upIntent;
    bool rollIntent;
    bool forwardIntent;
    bool firedThisHold;
    float holdTime;

    void Awake()
    {
        actions = new InputSystem_Actions();
        playerAnim = PlayerAnimBase.Resolve(gameObject);
        playerMovement = GetComponent<PlayerMovement>();
        playerThrow = GetComponent<PlayerThrow>();
        character = GetComponent<Character>();
        playerCollider = GetComponent<Collider2D>();
        playerRb = GetComponent<Rigidbody2D>();
    }

    void OnEnable() => actions.Player.Enable();

    void OnDisable()
    {
        actions.Player.Disable();
        ResetHold();
    }

    void OnDestroy() => actions?.Dispose();

    void Update()
    {
        if (actions.Player.Ability1.WasReleasedThisFrame())
        {
            TryThrowOnShortRelease();
            ResetHold();
            return;
        }

        if (!actions.Player.Ability1.IsPressed())
        {
            ResetHold();
            return;
        }

        if (actions.Player.Ability1.WasPressedThisFrame())
        {
            bool lookingUp = IsHoldingUp();
            bool lookingDown = IsHoldingDown();
            upIntent = lookingUp;
            rollIntent = !lookingUp && lookingDown;
            forwardIntent = !lookingUp && !lookingDown;
        }

        holdTime += Time.deltaTime;

        if (playerMovement.IsActionLocked || playerAnim.IsRolling)
            return;

        if (firedThisHold)
            return;

        if (holdTime < holdThreshold)
            return;

        firedThisHold = true;

        if (upIntent)
        {
            if (IsHoldingUp())
                TrySpawnHomingMissile();
            return;
        }

        if (rollIntent)
        {
            if (IsHoldingDown())
                TrySpawnRollGrenade();
            return;
        }

        if (forwardIntent && !HasVertical())
            TrySpawnBullet();
    }

    void TryThrowOnShortRelease()
    {
        if (!forwardIntent || firedThisHold)
            return;

        if (holdTime >= holdThreshold)
            return;

        playerThrow.TryThrowGrenade();
    }

    void TrySpawnRollGrenade()
    {
        if (rollGrenadePrefab == null)
            return;

        if (abilityPowerCost > 0f
            && (character == null || character.AbilityPower < abilityPowerCost))
            return;

        Transform point = throwPoint != null ? throwPoint : transform;
        var grenade = Instantiate(rollGrenadePrefab, point.position, Quaternion.identity);
        grenade.Init(playerMovement.FaceDirection, playerRb.linearVelocity, playerCollider);

        if (abilityPowerCost > 0f)
            character.DrainAbilityPower(abilityPowerCost);

        playerAnim.TryPlayThrowAnim();
    }

    void TrySpawnBullet()
    {
        if (grenadeBulletPrefab == null)
            return;

        if (bulletAbilityPowerCost > 0f
            && (character == null || character.AbilityPower < bulletAbilityPowerCost))
            return;

        Transform point = bulletFirePoint != null ? bulletFirePoint : transform;
        var bullet = Instantiate(grenadeBulletPrefab, point.position, Quaternion.identity);
        bullet.Init(playerMovement.FaceDirection, playerCollider);

        if (bulletAbilityPowerCost > 0f)
            character.DrainAbilityPower(bulletAbilityPowerCost);
    }

    void TrySpawnHomingMissile()
    {
        if (missilePrefab == null)
            return;

        if (missileAbilityPowerCost > 0f
            && (character == null || character.AbilityPower < missileAbilityPowerCost))
            return;

        Vector3 spawnPos = missileFirePoint != null
            ? missileFirePoint.position
            : transform.position + new Vector3(0f, missileSpawnOffsetY, 0f);

        var missile = Instantiate(missilePrefab, spawnPos, Quaternion.identity);
        missile.Init(playerCollider);

        if (missileAbilityPowerCost > 0f)
            character.DrainAbilityPower(missileAbilityPowerCost);

        playerAnim.TryPlayThrowAnim();
    }

    bool IsHoldingUp() =>
        playerMovement.MoveInput.y > playerMovement.InputThreshold;

    bool IsHoldingDown() =>
        playerMovement.MoveInput.y < -playerMovement.InputThreshold;

    bool HasVertical() =>
        Mathf.Abs(playerMovement.MoveInput.y) > playerMovement.InputThreshold;

    void ResetHold()
    {
        holdTime = 0f;
        firedThisHold = false;
        upIntent = false;
        rollIntent = false;
        forwardIntent = false;
    }
}
