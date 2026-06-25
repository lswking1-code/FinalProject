using UnityEngine;

public class Player : MonoBehaviour
{
    public float moveSpeed;
    public float jumpForce;
    public float crouchSpeed;

    [Header("地面检测")]
    [SerializeField] Transform groundCheck;
    [SerializeField] float groundCheckRadius = 0.1f;
    [SerializeField] LayerMask groundLayer;

    private Rigidbody2D rb;
    private PlayerAnim playerAnim;
    private bool isGrounded;

    public bool IsGrounded => isGrounded;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        playerAnim = GetComponent<PlayerAnim>();

        if (groundCheck == null)
        {
            var check = transform.Find("GroundCheck");
            if (check != null)
                groundCheck = check;
        }
    }

    public void PlayerMove()
    {
        float moveInput = Input.GetAxis("Horizontal");
        if (moveInput > 0)
        {
            transform.rotation = Quaternion.Euler(0, 0, 0);
            playerAnim.PlayRunAnim();
        }
        else if (moveInput < 0)
        {
            transform.rotation = Quaternion.Euler(0, 180, 0);
            playerAnim.PlayRunAnim();
        }
        else
        {
            playerAnim.PlayIdleAnim();
        }
        rb.linearVelocity = new Vector2(moveInput * moveSpeed, rb.linearVelocity.y);
    }

    public void PlayerJump()
    {
        if (Input.GetKeyDown(KeyCode.K) && isGrounded)
        {
            rb.AddForce(new Vector2(0, jumpForce), ForceMode2D.Impulse);
            playerAnim.PlayJumpAnim();
            isGrounded = false;
        }
    }

    void FixedUpdate()
    {
        CheckGround();
        playerAnim.UpdateGroundedState(isGrounded);
        PlayerMove();
        PlayerJump();
    }

    void CheckGround()
    {
        if (groundCheck == null)
            return;

        isGrounded = Physics2D.OverlapCircle(groundCheck.position, groundCheckRadius, groundLayer);
    }

    void OnDrawGizmosSelected()//在Scene视图中绘制地面检测范围
    {
        if (groundCheck == null)
            return;

        Gizmos.color = isGrounded ? Color.green : Color.red;
        Gizmos.DrawWireSphere(groundCheck.position, groundCheckRadius);
    }
}
