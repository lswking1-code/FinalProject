using UnityEngine;

/// <summary>
/// 斜坡交界 Trigger：检测玩家蹲/站，并在交界处闩锁「坡道路径 / 平地路径」。
/// 合金弹头式规则：
/// - 坡脚：站立移动或上跳才上坡；蹲行不上坡；已在坡上蹲不掉落；下方站起不上坡
/// - 坡顶：蹲行才下坡；站立移动/上跳不下坡；已在坡上站不起挤到上方；上方蹲着不下坡
/// </summary>
[RequireComponent(typeof(BoxCollider2D))]
public class SlopeJunctionTrigger : MonoBehaviour
{
    public enum JunctionKind
    {
        Bottom,
        Top
    }

    [SerializeField] JunctionKind kind = JunctionKind.Bottom;
    [SerializeField] SlopeOneWayPlatform slope;

    bool playerInside;
    bool onSlopePath;
    Collider2D playerCollider;
    PlayerMovement playerMovement;
    PhysicsCheck physicsCheck;
    PlayerAnimBase playerAnim;
    Rigidbody2D playerRb;

    public JunctionKind Kind => kind;
    public SlopeOneWayPlatform Slope => slope;
    public bool PlayerInside => playerInside;
    public bool OnSlopePath => onSlopePath;

    public void Initialize(SlopeOneWayPlatform owner, JunctionKind junctionKind)
    {
        slope = owner;
        kind = junctionKind;
    }

    /// <summary>
    /// 交界 Trigger 对指定玩家碰撞体是否有强制路径覆盖。
    /// shouldCollide=true 表示走坡路径；false 表示走平地路径（忽略斜坡）。
    /// </summary>
    public bool TryGetCollisionOverride(Collider2D playerCol, out bool shouldCollide)
    {
        shouldCollide = false;
        if (!playerInside || playerCol == null || playerCollider != playerCol)
            return false;

        // 在被 PlatformDropThrough 查询时同步刷新，避免 FixedUpdate 顺序导致晚一帧
        UpdatePathLatch();
        shouldCollide = onSlopePath;
        return true;
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (!TryBindPlayer(other))
            return;

        playerInside = true;
        onSlopePath = IsStandingOnThisSlope();
        UpdatePathLatch();
    }

    void OnTriggerStay2D(Collider2D other)
    {
        if (!playerInside)
        {
            if (!TryBindPlayer(other))
                return;
            playerInside = true;
            onSlopePath = IsStandingOnThisSlope();
        }

        if (playerCollider != null && other != playerCollider)
            return;

        UpdatePathLatch();
    }

    void OnTriggerExit2D(Collider2D other)
    {
        if (playerCollider == null || other != playerCollider)
            return;

        ClearPlayer();
    }

    void FixedUpdate()
    {
        if (!playerInside || playerMovement == null)
            return;

        UpdatePathLatch();
    }

    void UpdatePathLatch()
    {
        if (slope == null || playerMovement == null || physicsCheck == null)
            return;

        // 已在坡上：保持坡路径（蹲下也不会掉落 / 站起也不会被挤出）
        if (IsStandingOnThisSlope())
        {
            onSlopePath = true;
            return;
        }

        float threshold = playerMovement.InputThreshold;
        Vector2 moveInput = playerMovement.MoveInput;
        float moveX = Mathf.Abs(moveInput.x) > threshold ? Mathf.Sign(moveInput.x) : 0f;
        bool crouching = playerAnim != null && playerAnim.IsCrouching;
        float vy = playerRb != null ? playerRb.linearVelocity.y : 0f;
        Vector2 horizontalMove = new Vector2(moveX, 0f);

        bool onFlatGround = physicsCheck.isGround && physicsCheck.groundNormal.y > 0.9f;

        if (onFlatGround)
        {
            // 平地上默认走平地路径；仅在有明确进入意图时切到坡路径
            // 「站起不上坡 / 蹲着不下坡」都由此保证
            bool wantEnter = false;
            if (kind == JunctionKind.Bottom)
            {
                float towardAscent = Vector2.Dot(horizontalMove, slope.AscentDirection);
                wantEnter = !crouching && towardAscent > threshold;
            }
            else
            {
                float towardDescent = Vector2.Dot(horizontalMove, -slope.AscentDirection);
                wantEnter = crouching && towardDescent > threshold;
            }

            onSlopePath = wantEnter;
            return;
        }

        // 空中：坡脚允许上跳切入坡路径；坡顶上跳不切入
        if (!onSlopePath && kind == JunctionKind.Bottom && !crouching && vy > 0.15f)
        {
            float towardAscent = Vector2.Dot(horizontalMove, slope.AscentDirection);
            // 无水平输入的原地跳也可在交界处上坡
            if (towardAscent > threshold || Mathf.Approximately(moveX, 0f))
                onSlopePath = true;
        }
    }

    /// <summary>是否正站在该斜坡上（倾斜地面），不含交界旁的水平地面。</summary>
    bool IsStandingOnThisSlope()
    {
        if (slope == null || physicsCheck == null || !physicsCheck.isGround)
            return false;

        if (!physicsCheck.isOnSlope)
            return false;

        return slope.IsFeetAboveSurface(GetFeetPosition());
    }

    Vector2 GetFeetPosition()
    {
        if (playerCollider == null)
            return Vector2.zero;
        Bounds b = playerCollider.bounds;
        return new Vector2(b.center.x, b.min.y);
    }

    bool TryBindPlayer(Collider2D other)
    {
        if (other == null || !other.CompareTag("Player"))
            return false;

        playerCollider = other;
        playerMovement = other.GetComponent<PlayerMovement>()
            ?? other.GetComponentInParent<PlayerMovement>();
        physicsCheck = other.GetComponent<PhysicsCheck>()
            ?? other.GetComponentInParent<PhysicsCheck>();
        playerAnim = PlayerAnimBase.Resolve(other.gameObject);
        playerRb = other.attachedRigidbody != null
            ? other.attachedRigidbody
            : other.GetComponentInParent<Rigidbody2D>();
        return playerMovement != null;
    }

    void ClearPlayer()
    {
        playerInside = false;
        onSlopePath = false;
        playerCollider = null;
        playerMovement = null;
        physicsCheck = null;
        playerAnim = null;
        playerRb = null;
    }

    void OnDisable()
    {
        ClearPlayer();
    }
}
