using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Melee_Player（Bob）专属能力管理。不改动其他玩家脚本，仅在本组件内扩展能力。
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(PhysicsCheck))]
public class Bob_Controller : MonoBehaviour
{
    [Header("二段跳")]
    [Tooltip("二段跳目标高度；若勾选下方选项则改用 PlayerMovement.jumpHeight")]
    [SerializeField] float doubleJumpHeight = 4.5f;
    [SerializeField] bool usePlayerJumpHeight = true;
    [Tooltip("摇杆死区，与 PlayerMovement 默认一致")]
    [SerializeField] float inputThreshold = 0.5f;

    [Header("普通攻击（J / Attack）")]
    [SerializeField] int meleeDamage = 40;
    [SerializeField] float hitStart = 0.15f;
    [SerializeField] float hitEnd = 0.45f;
    [SerializeField] Transform meleePoint1;
    [SerializeField] Transform meleePoint2;
    [SerializeField] GameObject meleeHitbox;
    [SerializeField] MeleeDetectZone detectZone;

    Rigidbody2D rb;
    PhysicsCheck physicsCheck;
    PlayerMovement playerMovement;
    PlayerAnimBase playerAnim;
    InputSystem_Actions actions;
    Attack meleeAttack;

    bool jumpPressedThisFrame;
    bool hasUsedDoubleJump;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        physicsCheck = GetComponent<PhysicsCheck>();
        playerMovement = GetComponent<PlayerMovement>();
        playerAnim = PlayerAnimBase.Resolve(gameObject);
        actions = new InputSystem_Actions();

        if (meleeHitbox != null)
        {
            meleeAttack = meleeHitbox.GetComponent<Attack>();
            if (meleeAttack != null)
            {
                meleeAttack.damage = meleeDamage;
                meleeAttack.attackType = AttackType.Melee;
                meleeAttack.ignoreTag = "Player";
            }

            meleeHitbox.SetActive(false);
        }
    }

    void OnEnable()
    {
        actions.Player.Enable();
    }

    void OnDisable()
    {
        actions.Player.Disable();
    }

    void OnDestroy()
    {
        actions?.Dispose();
    }

    void Update()
    {
        if (actions.Player.Jump.WasPressedThisFrame())
            jumpPressedThisFrame = true;

        TryStartMeleeAttack();
        UpdateMeleeHitbox();
    }

    void LateUpdate() => SyncDetectZoneAnchor();

    void FixedUpdate()
    {
        if (physicsCheck.isGround)
        {
            hasUsedDoubleJump = false;
            jumpPressedThisFrame = false;
            return;
        }

        if (jumpPressedThisFrame && !hasUsedDoubleJump)
            TryDoubleJump();

        jumpPressedThisFrame = false;
    }

    void TryStartMeleeAttack()
    {
        if (playerMovement != null && playerMovement.IsActionLocked)
            return;

        if (playerAnim == null || playerAnim.IsDead)
            return;

        if (!actions.Player.Attack.WasPressedThisFrame())
            return;

        if (detectZone != null && detectZone.HasValidTarget)
        {
            var target = detectZone.GetNearestTarget(transform.position);
            if (target != null && playerMovement != null)
                playerMovement.FaceTowardWorldX(target.position.x);
        }

        playerAnim.TryPlayMeleeAnim();
    }

    void UpdateMeleeHitbox()
    {
        if (meleeHitbox == null || playerAnim == null)
            return;

        if (!playerAnim.IsMelee)
        {
            if (meleeHitbox.activeSelf)
                meleeHitbox.SetActive(false);
            return;
        }

        SyncHitboxAnchor();

        if (playerAnim.TryGetMeleeAnimProgress(out float t) && t >= hitStart && t <= hitEnd)
        {
            if (!meleeHitbox.activeSelf)
                meleeHitbox.SetActive(true);
        }
        else if (meleeHitbox.activeSelf)
        {
            meleeHitbox.SetActive(false);
        }
    }

    void SyncDetectZoneAnchor()
    {
        if (detectZone == null)
            return;

        Transform anchor = playerAnim != null && playerAnim.IsCrouching ? meleePoint2 : meleePoint1;
        if (anchor == null)
            anchor = transform;

        var zoneTransform = detectZone.transform;
        if (zoneTransform.parent == anchor)
            return;

        zoneTransform.SetParent(anchor, false);
        zoneTransform.localPosition = Vector3.zero;
        zoneTransform.localRotation = Quaternion.identity;
        zoneTransform.localScale = Vector3.one;
    }

    void SyncHitboxAnchor()
    {
        if (meleeHitbox == null)
            return;

        Transform anchor = playerAnim != null && playerAnim.IsCrouching ? meleePoint2 : meleePoint1;
        if (anchor == null)
            anchor = transform;

        var hitboxTransform = meleeHitbox.transform;
        if (hitboxTransform.parent == anchor)
            return;

        hitboxTransform.SetParent(anchor, false);
        hitboxTransform.localPosition = Vector3.zero;
        hitboxTransform.localRotation = Quaternion.identity;
        hitboxTransform.localScale = Vector3.one;
    }

    void TryDoubleJump()
    {
        if (playerMovement != null && playerMovement.IsActionLocked)
            return;

        if (playerAnim != null && playerAnim.IsDead)
            return;

        float height = doubleJumpHeight;
        if (usePlayerJumpHeight && playerMovement != null)
            height = playerMovement.jumpHeight;

        float gravityScale = rb.gravityScale;
        if (gravityScale < 0.01f && playerMovement != null)
            gravityScale = 3f; // 空中偶发 gravityScale≈0 时回退到 Prefab 默认值

        float gravity = Mathf.Abs(Physics2D.gravity.y * gravityScale);
        float jumpVelocity = Mathf.Sqrt(2f * gravity * height);

        Vector2 move = actions.Player.Move.ReadValue<Vector2>();
        bool hasHorizontal = Mathf.Abs(move.x) > inputThreshold;

        float velocityX = rb.linearVelocity.x;
        if (hasHorizontal && playerMovement != null)
            velocityX = Mathf.Sign(move.x) * playerMovement.runSpeed;

        rb.linearVelocity = new Vector2(velocityX, jumpVelocity);
        hasUsedDoubleJump = true;

        playerAnim?.PlayJumpAnim(hasHorizontal);
    }
}
