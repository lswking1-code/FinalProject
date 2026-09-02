using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// 飞行敌人：追入玩家头顶扇区后，先随机水平走位，再按相对位置选择向下或斜向单发射击，
/// 射击时静止，随后原地后摇，再重新进入循环。
/// 可选 isPatrol：原地站岗，索敌开战，超出驻守点脱战半径后回位。
/// </summary>
public class FlyingEnemy : Enemy
{
    [Header("飞行射击")]
    [Tooltip("斜向射击角（相对水平向下，度）")]
    public float shootAngle = 45f;
    [Tooltip("头顶扇区半角（相对竖直向下，度）")]
    public float overheadFanHalfAngle = 60f;
    [Tooltip("相对竖直夹角小于等于此值时向下射击，否则斜向射击")]
    public float downShotHalfAngle = 20f;
    [Tooltip("扇区 / 射击最大距离")]
    public float maxShootRange = 8f;
    [Tooltip("相对玩家的目标悬停高度")]
    public float preferredHoverHeight = 4f;
    [Tooltip("同侧飞行站位槽的竖直错开幅度")]
    public float flyingSlotYSpacing = 0.3f;
    [Tooltip("射击前走位时长")]
    public float actionDuration = 2f;
    [Tooltip("射击后原地后摇时长")]
    public float recoveryDuration = 1f;
    public EnemyProjectile projectilePrefab;
    [Tooltip("斜向射击开火点")]
    public Transform firePoint;
    [Tooltip("向下射击开火点；为空时回退 firePoint")]
    public Transform downFirePoint;

    [Header("悬停浮动")]
    [Tooltip("相对当前悬停高度的最大上下偏移")]
    public float bobAmplitude = 0.35f;
    [Tooltip("浮动角速度（弧度/秒）")]
    public float bobSpeed = 2f;
    [Tooltip("相对期望高度差的偏差超过此值后开始计时，之后才更新悬停高度")]
    public float hoverHeightErrorThreshold = 0.5f;
    [Tooltip("高度偏差持续超过该时长后才跟随玩家调整悬停高度（忽略短暂跳跃）")]
    public float hoverHeightHoldTime = 0.5f;

    [Header("单向平台避让")]
    [Tooltip("扫描单向平台的层；留空则默认 Platform")]
    [SerializeField] LayerMask oneWayPlatformLayer;
    [Tooltip("悬停中心需高于平台顶面的距离（约等于碰撞半径 + 余量）")]
    [SerializeField] float platformClearance = 1.1f;
    [Tooltip("IgnoreCollision 预扫描半径相对碰撞体的额外距离")]
    [SerializeField] float oneWayScanPadding = 3f;

    [HideInInspector] public float hoverBaseY;
    float bobPhase;
    float hoverHeightOutOfRangeTimer;

    protected override bool UseDeathDelay => false;
    protected override bool UseDeathVanishFlash => false;

    Collider2D bodyCollider;
    ContactFilter2D oneWayPlatformFilter;
    readonly Collider2D[] oneWayOverlapBuffer = new Collider2D[16];
    readonly HashSet<Collider2D> ignoredOneWayPlatforms = new HashSet<Collider2D>();
    // #region agent log
    float _dbgAnimLogTimer;
    string _dbgLastState;
    // #endregion

    protected override void Awake()
    {
        base.Awake();

        if (Rb != null)
            Rb.gravityScale = 0f;

        bodyCollider = GetComponent<Collider2D>();
        if (oneWayPlatformLayer.value == 0)
            oneWayPlatformLayer = LayerMask.GetMask("Platform");
        oneWayPlatformFilter = new ContactFilter2D
        {
            useLayerMask = true,
            layerMask = oneWayPlatformLayer,
            useTriggers = false,
        };

        patroState = new FlyingIdleGuardState();
        returnState = new FlyingReturnHomeState();
        getCloseState = new FlyingChaseState();
        moveState = new FlyingMoveState();
        shotState = new FlyingShotState();
        reloadState = new FlyingRecoveryState();

        if (normalSpeed <= 0f)
            normalSpeed = 2f;
        if (chaseSpeed <= 0f)
            chaseSpeed = 4f;

        hoverBaseY = homePosition.y;
        bobPhase = 0f;
        hoverHeightOutOfRangeTimer = 0f;

        // #region agent log
        AgentDebugLog("A", "FlyingEnemy.Awake", "flying enemy awake anim dump", CollectAnimDebugData("awake"));
        // #endregion
    }

    protected override void FixedUpdate()
    {
        UpdateOneWayPlatformIgnores();
        hoverBaseY = RaiseYAboveOneWayPlatforms(transform.position.x, hoverBaseY);
        base.FixedUpdate();
    }

    protected override void ApplyPostMoveSeparation()
    {
        if (Rb == null || !ShouldApplySeparation)
            return;

        Vector2 intended = Rb.linearVelocity;
        // 射击 / 后摇等会把速度清零；站定时不推
        if (intended.sqrMagnitude <= 0.0025f)
            return;

        Vector2 correction = EnemySeparation.ComputeFlyingCorrection(this) * GetSeparationScale();
        correction.x = Mathf.Clamp(correction.x, -maxSeparationSpeed, maxSeparationSpeed);
        correction.y = Mathf.Clamp(correction.y, -maxSeparationSpeed, maxSeparationSpeed);

        Vector2 vel = intended + correction;
        if (intended.x > 0.05f)
            vel.x = Mathf.Max(0f, vel.x);
        else if (intended.x < -0.05f)
            vel.x = Mathf.Min(0f, vel.x);

        if (IsInOverheadFan())
            vel = ClampVelocityInsideFan(vel);
        Rb.linearVelocity = vel;
    }

    Vector2 ClampVelocityInsideFan(Vector2 velocity)
    {
        EnsurePlayerReference();
        if (player == null)
            return velocity;

        float height = Mathf.Max(0.5f, preferredHoverHeight);
        float maxX = GetFanHalfWidthAtHeight(height);
        float minWorldX = player.position.x - maxX;
        float maxWorldX = player.position.x + maxX;
        float nextX = transform.position.x + velocity.x * Time.fixedDeltaTime;

        if (nextX < minWorldX && velocity.x < 0f)
            velocity.x = 0f;
        else if (nextX > maxWorldX && velocity.x > 0f)
            velocity.x = 0f;

        return velocity;
    }

    protected override void OnEnable()
    {
        RegisterSeparation();
        CacheHome();
        hoverBaseY = RaiseYAboveOneWayPlatforms(homePosition.x, homePosition.y);
        bobPhase = 0f;
        hoverHeightOutOfRangeTimer = 0f;
        isReturning = false;

        if (Rb != null)
            Rb.gravityScale = 0f;

        if (isPatrol)
        {
            isAggro = false;
            SwitchState(NPCState.Patrol);
        }
        else
        {
            isAggro = true;
            if (ShouldStartCombatOnEnable())
                EvaluateCycle();
        }
    }

    /// <summary>
    /// 直升机等需要等遭遇条目写入召唤编制后再开战时，可关掉首次 OnEnable 循环。
    /// </summary>
    protected virtual bool ShouldStartCombatOnEnable() => true;

    protected override void Update()
    {
        base.Update();

        // #region agent log
        _dbgAnimLogTimer += Time.deltaTime;
        if (_dbgAnimLogTimer >= 0.5f)
        {
            _dbgAnimLogTimer = 0f;
            string stateName = "none";
            if (anim != null && anim.runtimeAnimatorController != null)
            {
                var info = anim.GetCurrentAnimatorStateInfo(0);
                stateName = info.IsName("Idle") ? "Idle"
                    : info.IsName("Fly") ? "Fly"
                    : info.IsName("Shoot") ? "Shoot"
                    : info.IsName("ShootDown") ? "ShootDown"
                    : info.IsName("Hit") ? "Hit"
                    : info.IsName("Die") ? "Die"
                    : $"hash:{info.shortNameHash}";
            }

            if (stateName != _dbgLastState || Time.frameCount % 60 < 2)
            {
                _dbgLastState = stateName;
                AgentDebugLog("B", "FlyingEnemy.Update", "animator runtime snapshot", CollectAnimDebugData(stateName));
            }
        }
        // #endregion
    }

    // #region agent log
    string CollectAnimDebugData(string note)
    {
        var sb = new StringBuilder(512);
        sb.Append("{\"note\":\"").Append(note).Append("\"");
        sb.Append(",\"animNull\":").Append(anim == null ? "true" : "false");
        if (anim != null)
        {
            var ctrl = anim.runtimeAnimatorController;
            sb.Append(",\"controller\":\"").Append(ctrl != null ? ctrl.name : "null").Append("\"");
            sb.Append(",\"enabled\":").Append(anim.enabled ? "true" : "false");
            sb.Append(",\"speed\":").Append(anim.speed.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (ctrl != null)
            {
                sb.Append(",\"walk\":").Append(anim.GetBool("walk") ? "true" : "false");
                sb.Append(",\"shoot\":").Append(anim.GetBool("shoot") ? "true" : "false");
                sb.Append(",\"shootDown\":").Append(anim.GetBool("shootDown") ? "true" : "false");
                sb.Append(",\"dead\":").Append(anim.GetBool("dead") ? "true" : "false");
                var clips = anim.GetCurrentAnimatorClipInfo(0);
                sb.Append(",\"clipCount\":").Append(clips != null ? clips.Length : 0);
                if (clips != null && clips.Length > 0 && clips[0].clip != null)
                {
                    sb.Append(",\"playingClip\":\"").Append(clips[0].clip.name).Append("\"");
                    sb.Append(",\"clipLength\":").Append(clips[0].clip.length.ToString(System.Globalization.CultureInfo.InvariantCulture));
                }
                else
                {
                    sb.Append(",\"playingClip\":\"none\"");
                }
            }
        }

        var spriteTf = transform.Find("Sprite");
        var squareTf = transform.Find("Square");
        var weaponTf = transform.Find("weapon");
        var rootSr = GetComponent<SpriteRenderer>();
        var spriteSr = spriteTf != null ? spriteTf.GetComponent<SpriteRenderer>() : null;
        sb.Append(",\"rootHasSpriteRenderer\":").Append(rootSr != null ? "true" : "false");
        sb.Append(",\"spriteChildActive\":").Append(spriteTf != null && spriteTf.gameObject.activeInHierarchy ? "true" : "false");
        sb.Append(",\"squareActive\":").Append(squareTf != null && squareTf.gameObject.activeInHierarchy ? "true" : "false");
        sb.Append(",\"weaponActive\":").Append(weaponTf != null && weaponTf.gameObject.activeInHierarchy ? "true" : "false");
        if (spriteSr != null && spriteSr.sprite != null)
            sb.Append(",\"visibleSprite\":\"").Append(spriteSr.sprite.name).Append("\"");
        else
            sb.Append(",\"visibleSprite\":\"null\"");
        sb.Append("}");
        return sb.ToString();
    }

    void AgentDebugLog(string hypothesisId, string location, string message, string dataJson)
    {
        try
        {
            long ts = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            string line = "{\"sessionId\":\"960d0c\",\"runId\":\"post-fix\",\"hypothesisId\":\"" + hypothesisId
                + "\",\"location\":\"" + location + "\",\"message\":\"" + message
                + "\",\"data\":" + dataJson + ",\"timestamp\":" + ts + "}\n";
            string path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "debug-960d0c.log"));
            File.AppendAllText(path, line);
        }
        catch { /* ignore debug IO */ }
    }
    // #endregion

    protected override bool ShouldRunTimeCounter() => false;

    protected override bool ShouldAutoMove() => false;

    protected override void StartCombatCycle() => EvaluateCycle();

    protected override void OnPatrolAggroFromDamage()
    {
        if (isReturning || isApproachingSpawnTarget)
            return;

        EnterPatrolCombat();
        EvaluateCycle();
    }

    public override void BeginReturnHome()
    {
        if (isDead || isReturning || isApproachingSpawnTarget)
            return;

        ApplyReturnHomeStart(clearVerticalVelocity: true);

        if (returnState != null)
            SwitchState(NPCState.Return);
        else
            FinishPatrolReset();
    }

    public override void FinishPatrolReset()
    {
        ApplyReturnHomeEnd();
        transform.position = homePosition;
        hoverBaseY = RaiseYAboveOneWayPlatforms(homePosition.x, homePosition.y);
        var raisedPos = transform.position;
        raisedPos.y = RaiseYAboveOneWayPlatforms(raisedPos.x, raisedPos.y);
        transform.position = raisedPos;
        bobPhase = 0f;
        hoverHeightOutOfRangeTimer = 0f;

        if (Rb != null)
            Rb.linearVelocity = Vector2.zero;

        if (character != null)
            character.RestoreFullHealth();

        SwitchState(NPCState.Patrol);
    }

    /// <summary>
    /// 每轮循环入口：巡逻闸门 → 不在扇区则追入，在扇区则先走位。
    /// </summary>
    public virtual void EvaluateCycle()
    {
        if (isDead || isApproachingSpawnTarget)
            return;

        EnsurePlayerReference();

        if (isPatrol)
        {
            if (isReturning)
                return;

            if (!isAggro)
            {
                SwitchState(NPCState.Patrol);
                return;
            }

            if (ShouldBeginPatrolReturn())
            {
                BeginReturnHome();
                return;
            }
        }

        if (!IsInOverheadFan())
            SwitchState(NPCState.GetClose);
        else
            SwitchState(NPCState.Move);
    }

    public override bool IsPlayerInCombatRange() => IsInOverheadFan();

    /// <summary>
    /// 是否在玩家头顶扇区内。
    /// </summary>
    public bool IsInOverheadFan()
    {
        EnsurePlayerReference();
        if (player == null)
            return false;

        Vector2 toPlayer = player.position - transform.position;
        if (toPlayer.y >= 0f)
            return false;

        float distance = toPlayer.magnitude;
        if (distance <= 0.001f || distance > maxShootRange)
            return false;

        float angleFromDown = Vector2.Angle(Vector2.down, toPlayer);
        return angleFromDown <= overheadFanHalfAngle;
    }

    /// <summary>
    /// 相对竖直夹角是否小到应向下射击。
    /// </summary>
    public bool ShouldFireDown()
    {
        EnsurePlayerReference();
        if (player == null)
            return false;

        Vector2 toPlayer = player.position - transform.position;
        if (toPlayer.y >= 0f || toPlayer.sqrMagnitude <= 0.0001f)
            return false;

        return Vector2.Angle(Vector2.down, toPlayer) <= downShotHalfAngle;
    }

    /// <summary>
    /// 玩家上方目标悬停点（扇区中心高度参考）。
    /// </summary>
    public Vector2 GetOverheadHoverPoint()
    {
        EnsurePlayerReference();
        if (player == null)
            return transform.position;

        float height = Mathf.Max(0.5f, preferredHoverHeight);
        float maxX = GetFanHalfWidthAtHeight(height);
        Vector2 slotOffset = enableSeparation
            ? EnemySeparation.GetFlyingSlotOffset(this)
            : Vector2.zero;
        float x = Mathf.Clamp(
            player.position.x + slotOffset.x,
            player.position.x - maxX,
            player.position.x + maxX);
        float y = player.position.y + height + slotOffset.y;
        return new Vector2(x, y);
    }

    /// <summary>
    /// 在指定悬停高度上，扇区允许的最大水平半宽。
    /// </summary>
    public float GetFanHalfWidthAtHeight(float height)
    {
        float h = Mathf.Max(0.1f, height);
        float halfAngle = Mathf.Clamp(overheadFanHalfAngle, 1f, 89f) * Mathf.Deg2Rad;
        float widthFromAngle = h * Mathf.Tan(halfAngle);
        float widthFromRange = Mathf.Sqrt(Mathf.Max(0f, maxShootRange * maxShootRange - h * h));
        return Mathf.Min(widthFromAngle, widthFromRange);
    }

    /// <summary>
    /// 斜向射击方向（朝向玩家一侧、相对水平向下 shootAngle）。
    /// </summary>
    public Vector2 GetDiagonalShootDirection()
    {
        EnsurePlayerReference();
        float dirX = 0f;
        if (player != null)
            dirX = Mathf.Sign(player.position.x - transform.position.x);
        if (dirX == 0f)
            dirX = faceDir.x != 0f ? Mathf.Sign(faceDir.x) : -1f;

        float rad = shootAngle * Mathf.Deg2Rad;
        return new Vector2(dirX * Mathf.Cos(rad), -Mathf.Sin(rad)).normalized;
    }

    public void FireSelectedProjectile()
    {
        if (ShouldFireDown())
            FireDownProjectile();
        else
            FireDiagonalProjectile();
    }

    public void FireDownProjectile()
    {
        if (projectilePrefab == null)
            return;

        Transform point = downFirePoint != null ? downFirePoint : firePoint;
        Vector3 spawnPos = point != null ? point.position : transform.position;
        var projectile = Instantiate(projectilePrefab, spawnPos, Quaternion.identity);
        EnemySceneCleanup.PlaceInSourceScene(projectile.gameObject, this);
        projectile.Init(Vector2.down);
        FacePlayer();
    }

    public void FireDiagonalProjectile()
    {
        if (projectilePrefab == null || player == null)
            return;

        Vector3 spawnPos = firePoint != null ? firePoint.position : transform.position;
        Vector2 dir = GetDiagonalShootDirection();

        var projectile = Instantiate(projectilePrefab, spawnPos, Quaternion.identity);
        EnemySceneCleanup.PlaceInSourceScene(projectile.gameObject, this);
        projectile.Init(dir);
        FacePlayer();
    }

    /// <summary>
    /// 水平移动并做有界 Y 浮动。水平与竖直合成为恒定速率，避免 hoverBaseY 突变时 Y 轴瞬移。
    /// </summary>
    public void ApplyHoverBob(float moveDirX, float speed)
    {
        if (isHurt || isDead || Rb == null)
            return;

        bobPhase += bobSpeed * Time.fixedDeltaTime;
        float targetY = hoverBaseY + Mathf.Sin(bobPhase) * bobAmplitude;
        targetY = Mathf.Clamp(targetY, hoverBaseY - bobAmplitude, hoverBaseY + bobAmplitude);
        targetY = RaiseYAboveOneWayPlatforms(transform.position.x, targetY);

        float dy = targetY - transform.position.y;
        bool hasHorizontal = Mathf.Abs(moveDirX) > 0.001f && speed > 0.001f;

        // 站岗（speed=0）时只用足以跟上浮动的竖直速率；战斗移动用传入 speed
        float moveSpeed = hasHorizontal
            ? speed
            : Mathf.Max(bobAmplitude * Mathf.Abs(bobSpeed), 0.5f);

        Vector2 dir;
        if (hasHorizontal)
        {
            dir = new Vector2(Mathf.Sign(moveDirX), dy);
            // 已接近目标高度时退化为纯水平，保持水平速率稳定
            if (Mathf.Abs(dy) <= 0.05f)
                dir.y = 0f;
        }
        else
        {
            if (Mathf.Abs(dy) <= 0.02f)
            {
                Rb.linearVelocity = Vector2.zero;
                return;
            }

            dir = new Vector2(0f, Mathf.Sign(dy));
        }

        if (dir.sqrMagnitude < 0.0001f)
        {
            Rb.linearVelocity = Vector2.zero;
            return;
        }

        Rb.linearVelocity = dir.normalized * moveSpeed;
    }

    public void ApplyHoverBobInPlace()
    {
        ApplyHoverBob(0f, 0f);
    }

    /// <summary>
    /// 更新悬停高度为相对玩家的目标高度。
    /// 默认仅当偏差超过阈值并持续一段时间后才跟随，避免玩家短暂起跳时立刻抬升。
    /// </summary>
    public void SyncHoverBaseToPlayer(bool forceImmediate = false)
    {
        EnsurePlayerReference();
        if (player == null)
            return;

        float desired = player.position.y + Mathf.Max(0.5f, preferredHoverHeight);
        float raiseX = player.position.x;
        if (forceImmediate)
        {
            hoverBaseY = RaiseYAboveOneWayPlatforms(raiseX, desired);
            hoverHeightOutOfRangeTimer = 0f;
            return;
        }

        float error = Mathf.Abs(desired - hoverBaseY);
        if (error <= Mathf.Max(0f, hoverHeightErrorThreshold))
        {
            hoverHeightOutOfRangeTimer = 0f;
            hoverBaseY = RaiseYAboveOneWayPlatforms(raiseX, hoverBaseY);
            return;
        }

        hoverHeightOutOfRangeTimer += Time.deltaTime;
        if (hoverHeightOutOfRangeTimer < Mathf.Max(0f, hoverHeightHoldTime))
            return;

        hoverBaseY = RaiseYAboveOneWayPlatforms(raiseX, desired);
        hoverHeightOutOfRangeTimer = 0f;
    }

    /// <summary>
    /// 扇区内水平走位：越界则返回应掉头的方向，否则返回原方向。
    /// </summary>
    public float ClampMoveDirInsideFan(float moveDir)
    {
        EnsurePlayerReference();
        if (player == null)
            return moveDir;

        float halfWidth = GetFanHalfWidthAtHeight(Mathf.Max(0.5f, preferredHoverHeight));
        float minX = player.position.x - halfWidth;
        float maxX = player.position.x + halfWidth;
        float x = transform.position.x;

        if (x <= minX && moveDir < 0f)
            return 1f;
        if (x >= maxX && moveDir > 0f)
            return -1f;
        return moveDir;
    }

    /// <summary>
    /// 飞向头顶扇区内的悬停点（恒定速率，不瞬移）。Y 使用延迟同步后的 hoverBaseY。
    /// </summary>
    public void MoveTowardOverheadFan(float speed)
    {
        if (isHurt || isDead || Rb == null || player == null)
            return;

        SyncHoverBaseToPlayer();

        float height = Mathf.Max(0.5f, preferredHoverHeight);
        float maxX = GetFanHalfWidthAtHeight(height);
        Vector2 slotOffset = enableSeparation
            ? EnemySeparation.GetFlyingSlotOffset(this)
            : Vector2.zero;
        float x = Mathf.Clamp(
            player.position.x + slotOffset.x,
            player.position.x - maxX,
            player.position.x + maxX);
        float idealY = RaiseYAboveOneWayPlatforms(x, hoverBaseY + slotOffset.y);
        Vector2 ideal = new Vector2(x, idealY);

        bobPhase += bobSpeed * Time.fixedDeltaTime;
        float bobOffset = Mathf.Sin(bobPhase) * bobAmplitude;
        float targetY = Mathf.Clamp(ideal.y + bobOffset, ideal.y - bobAmplitude, ideal.y + bobAmplitude);
        targetY = RaiseYAboveOneWayPlatforms(ideal.x, targetY);
        Vector2 target = new Vector2(ideal.x, targetY);

        Vector2 toTarget = target - (Vector2)transform.position;
        float dist = toTarget.magnitude;
        if (dist <= 0.05f)
        {
            Rb.linearVelocity = Vector2.zero;
            FacePlayer();
            return;
        }

        float step = Mathf.Max(0f, speed);
        Rb.linearVelocity = toTarget / dist * step;
        FacePlayer();

        if (Mathf.Abs(Rb.linearVelocity.x) > 0.01f)
            TryFlipOnObstacle(Mathf.Sign(Rb.linearVelocity.x));
    }

    public void StopHorizontalMotion()
    {
        if (Rb == null)
            return;

        Rb.linearVelocity = Vector2.zero;
    }

    public void MoveTowardHome(float speed)
    {
        if (isHurt || isDead || Rb == null)
            return;

        Vector2 homeTarget = homePosition;
        homeTarget.y = RaiseYAboveOneWayPlatforms(homeTarget.x, homeTarget.y);
        Vector2 toHome = homeTarget - (Vector2)transform.position;
        float dist = toHome.magnitude;
        if (dist <= returnArriveDistance)
        {
            Rb.linearVelocity = Vector2.zero;
            return;
        }

        Vector2 dir = toHome / dist;
        Rb.linearVelocity = dir * speed;

        if (dir.x > 0f)
            transform.localScale = new Vector3(-1f, 1f, 1f);
        else if (dir.x < 0f)
            transform.localScale = new Vector3(1f, 1f, 1f);
    }

    public float GetDistanceToHome()
    {
        return Vector2.Distance(transform.position, homePosition);
    }

    public override bool HasReachedSpawnTarget()
    {
        return Vector2.Distance(transform.position, spawnTargetPosition) <= returnArriveDistance;
    }

    public override void MoveTowardSpawnTarget()
    {
        if (isHurt || isDead || Rb == null)
            return;

        currentSpeed = GetSpawnApproachSpeed();
        Vector2 target = spawnTargetPosition;
        target.y = RaiseYAboveOneWayPlatforms(target.x, target.y);
        Vector2 toTarget = target - (Vector2)transform.position;
        float dist = toTarget.magnitude;
        if (dist <= returnArriveDistance)
        {
            Rb.linearVelocity = Vector2.zero;
            return;
        }

        Rb.linearVelocity = toTarget / dist * currentSpeed;

        if (toTarget.x > 0f)
            transform.localScale = new Vector3(-1f, 1f, 1f);
        else if (toTarget.x < 0f)
            transform.localScale = new Vector3(1f, 1f, 1f);
    }

    protected override void SnapToSpawnTarget()
    {
        Vector3 pos = spawnTargetPosition;
        pos.y = RaiseYAboveOneWayPlatforms(pos.x, pos.y);
        transform.position = pos;
        if (Rb != null)
            Rb.linearVelocity = Vector2.zero;
    }

    protected override void OnSpawnApproachFinished()
    {
        hoverBaseY = RaiseYAboveOneWayPlatforms(homePosition.x, homePosition.y);
        bobPhase = 0f;
        hoverHeightOutOfRangeTimer = 0f;
    }

    void UpdateOneWayPlatformIgnores()
    {
        if (bodyCollider == null)
            bodyCollider = GetComponent<Collider2D>();
        if (bodyCollider == null)
            return;

        if (oneWayPlatformLayer.value == 0)
            oneWayPlatformLayer = LayerMask.GetMask("Platform");

        oneWayPlatformFilter.layerMask = oneWayPlatformLayer;

        float probeRadius = GetBodyProbeRadius() + Mathf.Max(0f, oneWayScanPadding);
        int count = Physics2D.OverlapCircle(
            bodyCollider.bounds.center,
            probeRadius,
            oneWayPlatformFilter,
            oneWayOverlapBuffer);

        for (int i = 0; i < count; i++)
        {
            Collider2D platform = oneWayOverlapBuffer[i];
            if (platform == null || platform == bodyCollider)
                continue;
            if (!IsOneWayPlatform(platform))
                continue;
            if (!ignoredOneWayPlatforms.Add(platform))
                continue;

            Physics2D.IgnoreCollision(bodyCollider, platform, true);
        }
    }

    /// <summary>
    /// 若 (x,y) 处身体探测圆与单向板重叠，将 Y 抬到板顶 + clearance。
    /// </summary>
    public float RaiseYAboveOneWayPlatforms(float x, float y)
    {
        if (oneWayPlatformLayer.value == 0)
            oneWayPlatformLayer = LayerMask.GetMask("Platform");

        oneWayPlatformFilter.layerMask = oneWayPlatformLayer;

        float probeRadius = GetBodyProbeRadius();
        int count = Physics2D.OverlapCircle(
            new Vector2(x, y),
            probeRadius,
            oneWayPlatformFilter,
            oneWayOverlapBuffer);

        float result = y;
        for (int i = 0; i < count; i++)
        {
            Collider2D platform = oneWayOverlapBuffer[i];
            if (platform == null || platform == bodyCollider)
                continue;
            if (!IsOneWayPlatform(platform))
                continue;

            float raised = platform.bounds.max.y + Mathf.Max(0.05f, platformClearance);
            if (raised > result)
                result = raised;
        }

        return result;
    }

    float GetBodyProbeRadius()
    {
        if (bodyCollider == null)
            bodyCollider = GetComponent<Collider2D>();

        if (bodyCollider is CircleCollider2D circle)
        {
            float scale = Mathf.Max(Mathf.Abs(transform.lossyScale.x), Mathf.Abs(transform.lossyScale.y));
            return Mathf.Max(0.1f, circle.radius * scale);
        }

        if (bodyCollider != null)
        {
            Vector2 extents = bodyCollider.bounds.extents;
            return Mathf.Max(0.1f, Mathf.Max(extents.x, extents.y));
        }

        return Mathf.Max(0.1f, platformClearance);
    }

    static bool IsOneWayPlatform(Collider2D col)
    {
        var effector = col.GetComponent<PlatformEffector2D>();
        return effector != null && effector.useOneWay;
    }

    void OnDrawGizmosSelected()
    {
        Vector3 origin = transform.position;

        if (bobAmplitude > 0f)
        {
            float baseY = Application.isPlaying ? hoverBaseY : origin.y;
            Gizmos.color = new Color(0.3f, 0.9f, 1f, 0.5f);
            Gizmos.DrawLine(
                new Vector3(origin.x - 0.5f, baseY + bobAmplitude, 0f),
                new Vector3(origin.x + 0.5f, baseY + bobAmplitude, 0f));
            Gizmos.DrawLine(
                new Vector3(origin.x - 0.5f, baseY - bobAmplitude, 0f),
                new Vector3(origin.x + 0.5f, baseY - bobAmplitude, 0f));
        }

        // 头顶扇区边界（相对敌人当前朝下）
        float fanRad = overheadFanHalfAngle * Mathf.Deg2Rad;
        Vector3 fanLeft = Rotate2D(Vector3.down, fanRad);
        Vector3 fanRight = Rotate2D(Vector3.down, -fanRad);
        Gizmos.color = new Color(1f, 0.6f, 0f, 0.8f);
        Gizmos.DrawRay(origin, fanLeft * maxShootRange);
        Gizmos.DrawRay(origin, fanRight * maxShootRange);
        Gizmos.DrawRay(origin, Vector3.down * maxShootRange);

        // 向下 / 斜向分界
        float downRad = downShotHalfAngle * Mathf.Deg2Rad;
        Vector3 downLeft = Rotate2D(Vector3.down, downRad);
        Vector3 downRight = Rotate2D(Vector3.down, -downRad);
        Gizmos.color = Color.yellow;
        Gizmos.DrawRay(origin, downLeft * maxShootRange);
        Gizmos.DrawRay(origin, downRight * maxShootRange);

        float diagRad = shootAngle * Mathf.Deg2Rad;
        Vector3 diagL = new Vector3(-Mathf.Cos(diagRad), -Mathf.Sin(diagRad), 0f);
        Vector3 diagR = new Vector3(Mathf.Cos(diagRad), -Mathf.Sin(diagRad), 0f);
        Gizmos.color = Color.cyan;
        Gizmos.DrawRay(origin, diagL * maxShootRange);
        Gizmos.DrawRay(origin, diagR * maxShootRange);

        if (Application.isPlaying)
            EnsurePlayerReference();
        if (player != null)
        {
            Vector3 hover = GetOverheadHoverPoint();
            Gizmos.color = Color.magenta;
            Gizmos.DrawWireSphere(hover, 0.2f);
            Gizmos.DrawLine(player.position, hover);
        }

        DrawPatrolGizmos();
    }

    static Vector3 Rotate2D(Vector3 v, float radians)
    {
        float c = Mathf.Cos(radians);
        float s = Mathf.Sin(radians);
        return new Vector3(v.x * c - v.y * s, v.x * s + v.y * c, 0f);
    }
}
