using UnityEngine;

/// <summary>
/// 物理环境检测组件，用于检测地面与墙体碰撞状态。
/// 玩家与敌人均可挂载；敌人仅需地面/墙体检测，玩家可额外启用贴墙判定。
/// </summary>
public class PhysicsCheck : MonoBehaviour
{
    private CapsuleCollider2D coll;
    private Rigidbody2D rb;

    [Header("检测参数")]
    [Tooltip("勾选后使用手动配置的偏移量，否则根据碰撞体自动计算左右偏移")]
    public bool manual;
    [Tooltip("是否为玩家角色，玩家会额外进行贴墙判定")]
    public bool isPlayer;
    public Vector2 bottomOffset;
    public Vector2 leftOffset;
    public Vector2 rightOffset;
    public float checkRaduis;
    public LayerMask groundLayer;

    [Header("状态")]
    public bool isGround;
    public bool touchLeftWall;
    public bool touchRightWall;
    public bool onWall;

    private void Awake()
    {
        coll = GetComponent<CapsuleCollider2D>();
        rb = GetComponent<Rigidbody2D>();
        RecalculateOffsets();
    }

    private void Start()
    {
        Check();
    }

    private void Update()
    {
        Check();
    }

    void RecalculateOffsets()
    {
        if (manual || coll == null)
            return;

        rightOffset = new Vector2((coll.bounds.size.x + coll.offset.x) / 2, coll.bounds.size.y / 2);
        leftOffset = new Vector2(-rightOffset.x, rightOffset.y);
        bottomOffset = new Vector2(coll.offset.x, coll.offset.y - coll.size.y * 0.5f);
    }

    /// <summary>
    /// 执行地面、墙体及贴墙状态检测
    /// </summary>
    public void Check()
    {
        isGround = Physics2D.OverlapCircle(
            (Vector2)transform.position + new Vector2(bottomOffset.x * transform.localScale.x, bottomOffset.y),
            checkRaduis, groundLayer);

        touchLeftWall = Physics2D.OverlapCircle(
            (Vector2)transform.position + new Vector2(leftOffset.x, leftOffset.y),
            checkRaduis, groundLayer);
        touchRightWall = Physics2D.OverlapCircle(
            (Vector2)transform.position + new Vector2(rightOffset.x, rightOffset.y),
            checkRaduis, groundLayer);

        if (isPlayer && rb != null)
        {
            float inputX = Input.GetAxisRaw("Horizontal");
            onWall = (touchLeftWall && inputX < 0f || touchRightWall && inputX > 0f) && rb.linearVelocity.y < 0f;
        }
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.DrawWireSphere(
            (Vector2)transform.position + new Vector2(bottomOffset.x * transform.localScale.x, bottomOffset.y),
            checkRaduis);
        Gizmos.DrawWireSphere(
            (Vector2)transform.position + new Vector2(leftOffset.x, leftOffset.y),
            checkRaduis);
        Gizmos.DrawWireSphere(
            (Vector2)transform.position + new Vector2(rightOffset.x, rightOffset.y),
            checkRaduis);
    }
}
