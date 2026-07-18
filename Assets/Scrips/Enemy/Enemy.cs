using System.Collections;
using UnityEngine;

/// <summary>
/// 敌人基类，管理移动、状态机切换、受伤与死亡逻辑。
/// 子类需在 Awake 中初始化对应状态实例。
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
public class Enemy : MonoBehaviour
{
    Rigidbody2D rb;

    [HideInInspector] public Animator anim;
    [HideInInspector] public PhysicsCheck physicsCheck;

    [Header("基本参数")]
    public float normalSpeed;
    public float chaseSpeed;
    [HideInInspector] public float currentSpeed;

    public Vector3 faceDir;
    public float hurtForce;
    public Transform attacker;

    [Header("检测")]
    public Vector2 centerOffset;
    public Vector2 checkSize;
    public float checkDistance;
    public LayerMask attackLayer;

    [Header("计时器")]
    public float waitTime;
    public float waitTimeCounter;
    public bool wait;
    public float lostTime;
    public float lostTimeCounter;

    [Header("状态")]
    public bool isHurt;
    public bool isDead;
    [HideInInspector] public bool isMarked;

    [HideInInspector] public Transform player;

    private BaseState currentState;
    protected BaseState patroState;
    protected BaseState chaseState;
    protected BaseState getCloseState;
    protected BaseState shotState;
    protected BaseState moveState;

    public Rigidbody2D Rb => rb;

    protected virtual void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        anim = GetComponent<Animator>();
        physicsCheck = GetComponent<PhysicsCheck>();

        rb.constraints = RigidbodyConstraints2D.FreezeRotation;
        currentSpeed = normalSpeed;

        EnsurePlayerReference();
    }

    /// <summary>
    /// 缓存玩家引用。玩家在 Persistent 场景中可能晚于敌人 Awake 才激活。
    /// </summary>
    public void EnsurePlayerReference()
    {
        if (player != null)
            return;

        var playerObj = GameObject.FindGameObjectWithTag("Player");
        if (playerObj != null)
            player = playerObj.transform;
    }

    protected virtual void OnEnable()
    {
        currentState = GetInitialState();
        currentState.OnEnter(this);
    }

    protected virtual BaseState GetInitialState() => patroState;

    protected virtual void Update()
    {
        EnsurePlayerReference();
        faceDir = new Vector3(-transform.localScale.x, 0, 0);

        currentState.LogicUpdate();

        if (ShouldRunTimeCounter())
            TimeCounter();
    }

    protected virtual void FixedUpdate()
    {
        if (ShouldAutoMove())
            Move();

        currentState.PhysicsUpdate();
    }

    protected virtual bool ShouldRunTimeCounter() => true;

    protected virtual bool ShouldAutoMove() => !isHurt && !isDead && !wait;

    protected virtual void OnDisable()
    {
        currentState?.OnExit();
    }

    /// <summary>
    /// 按当前朝向与速度移动
    /// </summary>
    public virtual void Move()
    {
        rb.linearVelocity = new Vector2(currentSpeed * faceDir.x * Time.deltaTime, rb.linearVelocity.y);
    }

    /// <summary>
    /// 更新等待转身与丢失目标计时
    /// </summary>
    public void TimeCounter()
    {
        if (wait)
        {
            waitTimeCounter -= Time.deltaTime;
            if (waitTimeCounter <= 0)
            {
                wait = false;
                waitTimeCounter = waitTime;
                transform.localScale = new Vector3(faceDir.x, 1, 1);
            }
        }

        if (!FoundPlayer() && lostTimeCounter > 0)
            lostTimeCounter -= Time.deltaTime;
        else if (FoundPlayer())
            lostTimeCounter = lostTime;
    }

    /// <summary>
    /// 使用 BoxCast 检测前方是否存在玩家
    /// </summary>
    public bool FoundPlayer()
    {
        return Physics2D.BoxCast(
            transform.position + (Vector3)centerOffset,
            checkSize, 0, faceDir, checkDistance, attackLayer);
    }

    /// <summary>
    /// 与玩家的水平距离
    /// </summary>
    public float GetHorizontalDistanceToPlayer()
    {
        if (player == null)
            return float.MaxValue;

        return Mathf.Abs(transform.position.x - player.position.x);
    }

    /// <summary>
    /// 面向玩家
    /// </summary>
    public void FacePlayer()
    {
        if (player == null)
            return;

        float dx = player.position.x - transform.position.x;
        if (dx > 0)
            transform.localScale = new Vector3(-1, 1, 1);
        else if (dx < 0)
            transform.localScale = new Vector3(1, 1, 1);
    }

    /// <summary>
    /// 切换 AI 状态
    /// </summary>
    public virtual void SwitchState(NPCState state)
    {
        var newState = state switch
        {
            NPCState.Patrol => patroState,
            NPCState.Chase => chaseState,
            NPCState.GetClose => getCloseState,
            NPCState.Shot => shotState,
            NPCState.Move => moveState,
            _ => null
        };

        if (newState == null)
            return;

        currentState?.OnExit();
        currentState = newState;
        currentState.OnEnter(this);
    }

    #region 事件执行方法

    /// <summary>
    /// 受到伤害时调用，转向攻击者并触发击退
    /// </summary>
    public void OnTakeDamage(Transform attackTrans)
    {
        attacker = attackTrans;

        if (attackTrans.position.x - transform.position.x > 0)
            transform.localScale = new Vector3(-1, 1, 1);
        if (attackTrans.position.x - transform.position.x < 0)
            transform.localScale = new Vector3(1, 1, 1);

        isHurt = true;
        anim.SetTrigger("hurt");

        Vector2 dir = new Vector2(transform.position.x - attacker.position.x, 0).normalized;
        rb.linearVelocity = new Vector2(0, rb.linearVelocity.y);
        StartCoroutine(OnHurt(dir));
    }

    /// <summary>
    /// 受伤击退协程，短暂禁用移动后恢复
    /// </summary>
    private IEnumerator OnHurt(Vector2 dir)
    {
        if (rb.bodyType == RigidbodyType2D.Kinematic)
        {
            const float duration = 0.45f;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                transform.position += (Vector3)(dir * (hurtForce * Time.fixedDeltaTime / duration));
                if (rb.simulated)
                    rb.MovePosition(transform.position);
                elapsed += Time.fixedDeltaTime;
                yield return new WaitForFixedUpdate();
            }
        }
        else
        {
            rb.AddForce(dir * hurtForce, ForceMode2D.Impulse);
            yield return new WaitForSeconds(0.45f);
        }

        isHurt = false;
    }

    /// <summary>
    /// 死亡处理，切换图层并播放死亡动画
    /// </summary>
    public void OnDie()
    {
        gameObject.layer = 2;
        anim.SetBool("dead", true);
        isDead = true;
    }

    /// <summary>
    /// 死亡动画结束后销毁对象（由动画事件调用）
    /// </summary>
    public void DestroyAfterAnimation()
    {
        Destroy(gameObject);
    }

    #endregion

    private void OnDrawGizmosSelected()
    {
        Gizmos.DrawWireSphere(
            transform.position + new Vector3(checkDistance * faceDir.x, 0)
            + (Vector3)centerOffset + new Vector3(checkDistance * -transform.localScale.x, 0),
            0.2f);
    }
}
