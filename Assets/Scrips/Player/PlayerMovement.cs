using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(Animator))]
[RequireComponent(typeof(SpriteRenderer))]
public class PlayerMovement : MonoBehaviour
{
    [Header("Input")]
    [SerializeField] private InputActionAsset inputActionAsset;

    [Header("Movement")]
    [SerializeField] private float runSpeed = 4f;
    [SerializeField] private float crouchMoveSpeed = 2f;
    [SerializeField] private float jumpHeight = 2.5f;
    [SerializeField] private float gravity = 25f;
    [SerializeField] private float inputThreshold = 0.5f;

    [Header("Ground Check")]
    [SerializeField] private Transform groundCheck;
    [SerializeField] private float groundCheckRadius = 0.08f;
    [SerializeField] private LayerMask groundLayer;

    private Rigidbody2D rb;
    private Animator animator;
    private SpriteRenderer spriteRenderer;
    private InputSystem_Actions inputActions;

    private PlayerAnimState currentAnimState = PlayerAnimState.Idle;
    private bool lockedState;
    private bool wasGrounded = true;
    private bool facingRight = true;
    private bool wasRunningHorizontally;
    private float velocityY;
    private float airMoveX;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        animator = GetComponent<Animator>();
        spriteRenderer = GetComponent<SpriteRenderer>();

        rb.bodyType = RigidbodyType2D.Kinematic;
        rb.gravityScale = 0f;
        rb.constraints = RigidbodyConstraints2D.FreezeRotation;

        inputActions = new InputSystem_Actions(inputActionAsset);

        if (groundCheck == null)
        {
            var groundCheckObject = new GameObject("GroundCheck");
            groundCheckObject.transform.SetParent(transform);
            groundCheckObject.transform.localPosition = new Vector3(0f, 0.05f, 0f);
            groundCheck = groundCheckObject.transform;
        }

        if (groundLayer.value == 0)
            groundLayer = LayerMask.GetMask("Ground");
    }

    private void Start() => SetAnimState(PlayerAnimState.Idle);

    private void OnEnable() => inputActions?.Enable();

    private void OnDisable() => inputActions?.Disable();

    private void OnDestroy() => inputActions?.Dispose();

    private void Update()
    {
        Vector2 moveInput = inputActions.Move.ReadValue<Vector2>();
        bool jumpPressed = inputActions.Jump.WasPressedThisFrame();
        bool isGrounded = CheckGrounded();

        UpdateFacing(moveInput.x);

        if (jumpPressed && isGrounded && !lockedState && !IsCrouchInput(moveInput))
            BeginJump(moveInput.x);

        if (!isGrounded)
            UpdateAirborneState();
        else if (!wasGrounded && velocityY <= 0f &&
                 currentAnimState is PlayerAnimState.Jump or PlayerAnimState.Jump2)
            BeginLanding();
        else if (!lockedState)
            UpdateGroundedState(moveInput, isGrounded);

        AdvanceOneShotAnimations();
        AdvanceLoopingVariants();
        wasGrounded = isGrounded;
    }

    private void FixedUpdate()
    {
        bool isGrounded = CheckGrounded();
        float horizontalSpeed = GetHorizontalSpeed();

        if (!isGrounded)
        {
            velocityY -= gravity * Time.fixedDeltaTime;
        }
        else if (velocityY < 0f)
        {
            velocityY = 0f;
        }

        Vector2 delta = new Vector2(horizontalSpeed, velocityY) * Time.fixedDeltaTime;
        rb.MovePosition(rb.position + delta);
    }

    private void UpdateGroundedState(Vector2 moveInput, bool isGrounded)
    {
        float horizontal = GetHorizontalInput(moveInput);
        bool lookUp = moveInput.y > inputThreshold;
        bool crouch = IsCrouchInput(moveInput);

        if (crouch)
        {
            wasRunningHorizontally = false;
            if (Mathf.Abs(horizontal) > 0.01f)
                SetCrouchMoveState();
            else if (currentAnimState is PlayerAnimState.CrouchMove1 or PlayerAnimState.CrouchMove2)
                SetAnimState(PlayerAnimState.Crouch2);
            else if (currentAnimState == PlayerAnimState.Crouch2)
                return;
            else if (currentAnimState == PlayerAnimState.Crouch1)
                return;
            else
                SetAnimState(PlayerAnimState.Crouch1);
            return;
        }

        if (Mathf.Abs(horizontal) > 0.01f)
        {
            if (lookUp)
            {
                wasRunningHorizontally = true;
                SetLookUpRunState();
            }
            else
            {
                wasRunningHorizontally = true;
                SetRunState();
            }
            return;
        }

        if (wasRunningHorizontally && ShouldPlayStop())
        {
            wasRunningHorizontally = false;
            SetAnimState(PlayerAnimState.Stop1);
            lockedState = true;
            return;
        }

        wasRunningHorizontally = false;

        if (lookUp)
        {
            if (currentAnimState == PlayerAnimState.LookUp2)
                return;
            if (currentAnimState == PlayerAnimState.LookUp1)
                return;
            SetAnimState(PlayerAnimState.LookUp1);
            return;
        }

        if (currentAnimState is PlayerAnimState.Idle or PlayerAnimState.LookUp2 or PlayerAnimState.Stop2)
        {
            SetAnimState(PlayerAnimState.Idle);
            return;
        }

        if (!IsOneShotState(currentAnimState))
            SetAnimState(PlayerAnimState.Idle);
    }

    private void UpdateAirborneState()
    {
        wasRunningHorizontally = false;
        if (currentAnimState == PlayerAnimState.Jump)
            return;

        SetAnimState(PlayerAnimState.Jump2);
    }

    private void BeginJump(float moveX)
    {
        airMoveX = GetHorizontalInput(new Vector2(moveX, 0f));
        velocityY = Mathf.Sqrt(2f * gravity * jumpHeight);
        lockedState = false;
        SetAnimState(PlayerAnimState.Jump);
    }

    private void BeginLanding()
    {
        velocityY = 0f;
        SetAnimState(PlayerAnimState.Land1);
        lockedState = true;
    }

    private void AdvanceOneShotAnimations()
    {
        if (!IsOneShotState(currentAnimState) || !IsCurrentAnimFinished())
            return;

        switch (currentAnimState)
        {
            case PlayerAnimState.LookUp1:
                SetAnimState(PlayerAnimState.LookUp2);
                break;
            case PlayerAnimState.Run1:
                SetAnimState(PlayerAnimState.Run2);
                break;
            case PlayerAnimState.LookUpRun1:
                SetAnimState(PlayerAnimState.LookUpRun2);
                break;
            case PlayerAnimState.Crouch1:
                SetAnimState(PlayerAnimState.Crouch2);
                break;
            case PlayerAnimState.Jump:
                SetAnimState(PlayerAnimState.Jump2);
                break;
            case PlayerAnimState.Land1:
                lockedState = false;
                SetAnimState(PlayerAnimState.Idle);
                break;
            case PlayerAnimState.Stop1:
                SetAnimState(PlayerAnimState.Stop2);
                break;
            case PlayerAnimState.Stop2:
                lockedState = false;
                SetAnimState(PlayerAnimState.Idle);
                break;
        }
    }

    private void SetRunState()
    {
        if (currentAnimState is PlayerAnimState.Run2 or PlayerAnimState.Run1)
            return;

        if (currentAnimState is PlayerAnimState.LookUpRun1 or PlayerAnimState.LookUpRun2 or PlayerAnimState.LookUpRun3)
        {
            SetAnimState(PlayerAnimState.Run2);
            return;
        }

        SetAnimState(PlayerAnimState.Run1);
    }

    private void SetLookUpRunState()
    {
        if (currentAnimState is PlayerAnimState.LookUpRun1 or PlayerAnimState.LookUpRun2 or PlayerAnimState.LookUpRun3)
            return;

        if (currentAnimState == PlayerAnimState.Run2)
        {
            SetAnimState(PlayerAnimState.LookUpRun1);
            return;
        }

        SetAnimState(PlayerAnimState.LookUpRun1);
    }

    private void SetCrouchMoveState()
    {
        if (currentAnimState is PlayerAnimState.CrouchMove1 or PlayerAnimState.CrouchMove2)
            return;

        SetAnimState(PlayerAnimState.CrouchMove1);
    }

    private void AdvanceLoopingVariants()
    {
        if (!IsCurrentAnimFinished())
            return;

        switch (currentAnimState)
        {
            case PlayerAnimState.LookUpRun2:
                SetAnimState(PlayerAnimState.LookUpRun3);
                break;
            case PlayerAnimState.LookUpRun3:
                SetAnimState(PlayerAnimState.LookUpRun2);
                break;
            case PlayerAnimState.CrouchMove1:
                SetAnimState(PlayerAnimState.CrouchMove2);
                break;
            case PlayerAnimState.CrouchMove2:
                SetAnimState(PlayerAnimState.CrouchMove1);
                break;
        }
    }

    private float GetHorizontalSpeed()
    {
        if (lockedState || currentAnimState is PlayerAnimState.Stop1 or PlayerAnimState.Stop2 or PlayerAnimState.Land1)
            return 0f;

        float direction = facingRight ? 1f : -1f;

        switch (currentAnimState)
        {
            case PlayerAnimState.Run1:
            case PlayerAnimState.Run2:
            case PlayerAnimState.LookUpRun1:
            case PlayerAnimState.LookUpRun2:
            case PlayerAnimState.LookUpRun3:
                return direction * runSpeed;
            case PlayerAnimState.CrouchMove1:
            case PlayerAnimState.CrouchMove2:
                return direction * crouchMoveSpeed;
            case PlayerAnimState.Jump:
            case PlayerAnimState.Jump2:
                return airMoveX * runSpeed;
            default:
                return 0f;
        }
    }

    private bool ShouldPlayStop()
    {
        return currentAnimState is PlayerAnimState.Run1 or PlayerAnimState.Run2
            or PlayerAnimState.LookUpRun1 or PlayerAnimState.LookUpRun2 or PlayerAnimState.LookUpRun3;
    }

    private bool IsCrouchInput(Vector2 moveInput)
    {
        return moveInput.y < -inputThreshold || inputActions.Crouch.IsPressed();
    }

    private float GetHorizontalInput(Vector2 moveInput)
    {
        if (Mathf.Abs(moveInput.x) < inputThreshold)
            return 0f;

        return Mathf.Sign(moveInput.x);
    }

    private void UpdateFacing(float moveX)
    {
        if (Mathf.Abs(moveX) < inputThreshold)
            return;

        facingRight = moveX > 0f;
        spriteRenderer.flipX = !facingRight;
    }

    private bool CheckGrounded()
    {
        return Physics2D.OverlapCircle(groundCheck.position, groundCheckRadius, groundLayer);
    }

    private bool IsCurrentAnimFinished()
    {
        if (animator.IsInTransition(0))
            return false;

        AnimatorStateInfo info = animator.GetCurrentAnimatorStateInfo(0);
        return info.normalizedTime >= 1f;
    }

    private static bool IsOneShotState(PlayerAnimState state)
    {
        return state is PlayerAnimState.LookUp1 or PlayerAnimState.Run1 or PlayerAnimState.LookUpRun1
            or PlayerAnimState.Crouch1 or PlayerAnimState.Jump or PlayerAnimState.Land1
            or PlayerAnimState.Stop1 or PlayerAnimState.Stop2;
    }

    private void SetAnimState(PlayerAnimState state)
    {
        if (currentAnimState == state)
            return;

        currentAnimState = state;
        animator.Play(StateHashes[(int)state], 0, 0f);
    }

    private static readonly int[] StateHashes =
    {
        PlayerAnimatorIds.Idle,
        PlayerAnimatorIds.LookUp1,
        PlayerAnimatorIds.LookUp2,
        PlayerAnimatorIds.Run1,
        PlayerAnimatorIds.Run2,
        PlayerAnimatorIds.LookUpRun1,
        PlayerAnimatorIds.LookUpRun2,
        PlayerAnimatorIds.LookUpRun3,
        PlayerAnimatorIds.Crouch1,
        PlayerAnimatorIds.Crouch2,
        PlayerAnimatorIds.CrouchMove1,
        PlayerAnimatorIds.CrouchMove2,
        PlayerAnimatorIds.Jump,
        PlayerAnimatorIds.Jump2,
        PlayerAnimatorIds.Land1,
        PlayerAnimatorIds.Stop1,
        PlayerAnimatorIds.Stop2,
    };
}
