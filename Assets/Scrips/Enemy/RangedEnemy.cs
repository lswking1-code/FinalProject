using UnityEngine;

/// <summary>
/// 远程敌人：距离判断优先，GetClose / Shot / Move 三状态循环，带动态 Action 概率。
/// </summary>
public class RangedEnemy : Enemy
{
    const float DefaultShotProbability = 0.7f;
    const float DefaultMoveProbability = 0.3f;
    const float ProbabilityStep = 0.1f;

    [Header("远程参数")]
    public float shootRange = 5f;
    public float actionDuration = 3f;
    public float fireInterval = 0.5f;
    public EnemyProjectile projectilePrefab;
    public Transform firePoint;

    [HideInInspector] public float shotProbability = DefaultShotProbability;
    [HideInInspector] public float moveProbability = DefaultMoveProbability;
    [HideInInspector] public EnemyAction? lastAction;

    protected override void Awake()
    {
        base.Awake();
        getCloseState = new RangedGetCloseState();
        shotState = new RangedShotState();
        moveState = new RangedMoveState();

        if (normalSpeed <= 0f)
            normalSpeed = 2f;
        if (chaseSpeed <= 0f)
            chaseSpeed = 4f;

        if (Rb != null)
        {
            Rb.bodyType = RigidbodyType2D.Kinematic;
            Rb.gravityScale = 0f;
            Rb.simulated = false;
        }

        // #region agent log
        DebugAgentLog.Log("H2", "RangedEnemy.Awake", "rb init",
            $"{{\"rbNull\":{(Rb == null).ToString().ToLower()},\"bodyType\":\"{(Rb != null ? Rb.bodyType.ToString() : "none")}\",\"normalSpeed\":{normalSpeed},\"chaseSpeed\":{chaseSpeed},\"runId\":\"post-fix\"}}");
        // #endregion
    }

    void Start()
    {
        ConfigurePhysicsCheck();
        SyncRigidbodyToTransform();
    }

    void SyncRigidbodyToTransform()
    {
        if (Rb != null)
            Rb.position = transform.position;
    }

    void LateUpdate()
    {
        // #region agent log
        if (Time.frameCount % 60 == 0)
            DebugAgentLog.Log("H7", "RangedEnemy.LateUpdate", "pos check",
                $"{{\"runId\":\"post-fix-v3\",\"transformX\":{transform.position.x},\"rbX\":{(Rb != null ? Rb.position.x : 0f)},\"simulated\":{(Rb != null && Rb.simulated).ToString().ToLower()}}}");
        // #endregion
    }

    void ConfigurePhysicsCheck()
    {
        if (physicsCheck == null)
            return;

        var coll = GetComponent<CapsuleCollider2D>();
        if (coll == null)
            return;

        float halfHeight = coll.size.y * 0.5f + coll.offset.y;
        physicsCheck.bottomOffset = new Vector2(0f, -halfHeight + 0.1f);
    }

    protected override void OnEnable()
    {
        shotProbability = DefaultShotProbability;
        moveProbability = DefaultMoveProbability;
        lastAction = null;
        EvaluateCycle();
    }

    protected override bool ShouldRunTimeCounter() => false;

    protected override bool ShouldAutoMove() => false;

    /// <summary>
    /// 每轮循环入口：距离判断 → GetClose 或 Action 判定
    /// </summary>
    public void EvaluateCycle()
    {
        if (isDead)
            return;

        EnsurePlayerReference();

        float dist = GetHorizontalDistanceToPlayer();

        // #region agent log
        DebugAgentLog.Log("H6", "RangedEnemy.EvaluateCycle", "distance check",
            $"{{\"runId\":\"post-fix-v2\",\"dist\":{dist},\"shootRange\":{shootRange},\"state\":\"{DebugCurrentStateName}\",\"enemyX\":{transform.position.x},\"playerX\":{(player != null ? player.position.x : 0f)}}}");
        // #endregion

        if (dist > shootRange)
            SwitchState(NPCState.GetClose);
        else
            RollAndEnterAction();
    }

    void RollAndEnterAction()
    {
        var next = Random.value < shotProbability ? NPCState.Shot : NPCState.Move;

        // #region agent log
        DebugAgentLog.Log("H6", "RangedEnemy.RollAndEnterAction", "action roll",
            $"{{\"runId\":\"post-fix-v2\",\"next\":\"{next}\",\"shotProbability\":{shotProbability}}}");
        // #endregion

        SwitchState(next);
    }

    /// <summary>
    /// 进入 Shot / Move 时更新下次触发概率
    /// </summary>
    public void OnActionEntered(EnemyAction action)
    {
        if (lastAction.HasValue && lastAction.Value == action)
        {
            if (action == EnemyAction.Shot)
            {
                shotProbability = Mathf.Max(0f, shotProbability - ProbabilityStep);
                moveProbability = 1f - shotProbability;
            }
            else
            {
                moveProbability = Mathf.Max(0f, moveProbability - ProbabilityStep);
                shotProbability = 1f - moveProbability;
            }
        }
        else
        {
            shotProbability = DefaultShotProbability;
            moveProbability = DefaultMoveProbability;
        }

        lastAction = action;
    }

    /// <summary>
    /// 朝玩家水平移动
    /// </summary>
    public void MoveTowardPlayer()
    {
        if (player == null || isHurt || isDead || Rb == null)
        {
            // #region agent log
            if (Time.frameCount % 60 == 0)
                DebugAgentLog.Log("H3", "RangedEnemy.MoveTowardPlayer", "early return",
                    $"{{\"playerNull\":{(player == null).ToString().ToLower()},\"isHurt\":{isHurt.ToString().ToLower()},\"isDead\":{isDead.ToString().ToLower()},\"rbNull\":{(Rb == null).ToString().ToLower()},\"state\":\"{DebugCurrentStateName}\"}}");
            // #endregion
            return;
        }

        float dir = GetMoveDirTowardPlayer();
        ApplyHorizontalMove(dir);
        FacePlayer();
    }

    /// <summary>
    /// 沿指定水平方向移动
    /// </summary>
    public void MoveHorizontal(float direction)
    {
        if (isHurt || isDead || Rb == null)
            return;

        ApplyHorizontalMove(direction);
    }

    public float GetMoveDirTowardPlayer()
    {
        if (player == null)
            return faceDir.x;

        float dir = Mathf.Sign(player.position.x - transform.position.x);
        return dir == 0f ? faceDir.x : dir;
    }

    void ApplyHorizontalMove(float direction)
    {
        float delta = currentSpeed * direction * Time.fixedDeltaTime;
        Vector3 posBefore = transform.position;
        Vector3 newPos = posBefore;
        newPos.x += delta;
        transform.position = newPos;
        Vector3 posAfter = transform.position;

        // #region agent log
        if (Time.frameCount % 30 == 0)
        {
            DebugAgentLog.Log("H1-H5", "RangedEnemy.ApplyHorizontalMove", "move attempt",
                $"{{\"runId\":\"post-fix-v3\",\"state\":\"{DebugCurrentStateName}\",\"currentSpeed\":{currentSpeed},\"direction\":{direction},\"deltaX\":{delta},\"posBeforeX\":{posBefore.x},\"posAfterX\":{posAfter.x},\"transformMoved\":{(Mathf.Abs(posAfter.x - posBefore.x) > 0.0001f).ToString().ToLower()}}}");
        }
        // #endregion
    }

    /// <summary>
    /// 在 firePoint 发射一枚子弹
    /// </summary>
    public void FireProjectile()
    {
        if (projectilePrefab == null || player == null)
            return;

        Vector3 spawnPos = firePoint != null ? firePoint.position : transform.position;
        float dir = Mathf.Sign(player.position.x - transform.position.x);
        if (dir == 0f)
            dir = faceDir.x;

        var projectile = Instantiate(projectilePrefab, spawnPos, Quaternion.identity);
        projectile.Init(new Vector2(dir, 0f));
        FacePlayer();
    }

    /// <summary>
    /// 遇墙壁时转身，moveDir 为当前水平移动方向
    /// </summary>
    public bool TryFlipOnObstacle(float moveDir)
    {
        if (physicsCheck == null || !IsPhysicsCheckConfigured())
            return false;

        if ((physicsCheck.touchLeftWall && moveDir < 0f)
            || (physicsCheck.touchRightWall && moveDir > 0f))
        {
            transform.localScale = new Vector3(faceDir.x, 1, 1);
            return true;
        }

        return false;
    }

    bool IsPhysicsCheckConfigured()
    {
        return physicsCheck.checkRaduis > 0f && physicsCheck.groundLayer.value != 0;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(transform.position + Vector3.left * shootRange, transform.position + Vector3.right * shootRange);
    }
}
