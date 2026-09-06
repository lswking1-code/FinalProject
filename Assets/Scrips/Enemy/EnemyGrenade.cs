using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(CircleCollider2D))]
[RequireComponent(typeof(Animator))]
public class EnemyGrenade : MonoBehaviour, IEnemyProjectileCancelable
{
    const string RollingStateName = "GrenadeRolling";
    const int PassThroughBufferSize = 16;

    [Tooltip("未由投掷者覆盖时的默认抛射角（度，相对水平向上）")]
    [SerializeField] float defaultThrowAngle = 35.5f;
    [Tooltip("未由投掷者覆盖时的默认抛射速度")]
    [SerializeField] float defaultThrowSpeed = 8.6f;
    [SerializeField] EnemyGrenadeExplosion explosionPrefab;
    [SerializeField, Range(0f, 1f)] float throwerHorizontalInherit = 0.5f;
    [SerializeField, Range(0f, 1f)] float throwerVerticalInherit = 0f;
    [SerializeField] float rollSpeedReference = 12f;
    [SerializeField] float minRollAnimSpeed = 0.6f;
    [SerializeField] float maxRollAnimSpeed = 1.8f;
    [Tooltip("空中视觉旋转角速度（度/秒），符号随水平飞行方向翻转")]
    [SerializeField] float spinDegreesPerSecond = 720f;
    [Tooltip("高抛：期间碰到玩家不爆炸并穿过，到期后碰到玩家会爆炸，落地引爆不受影响。滚雷：到期后自爆。")]
    [SerializeField] float fuseTime = 0.5f;
    [Tooltip("命中该层时引爆（地面）；滚雷落地不爆，只用于落地手感")]
    [SerializeField] LayerMask groundLayer;
    [SerializeField] float groundSnapRayDistance = 1.5f;
    [Tooltip("物理步进前预扫描并 Ignore 平台的额外半径，避免接触当帧被托住")]
    [SerializeField] float passThroughScanPadding = 1f;

    [Header("滚雷")]
    [Tooltip("预制体标记；InitRoll 会强制进入滚雷模式")]
    [SerializeField] bool isRollGrenade;
    [SerializeField] float horizontalSpeed = 2.8f;
    [SerializeField] float verticalSpeed = 0.55f;
    [SerializeField] float forwardImpulse = 0.3f;
    [Tooltip("滚雷重力倍率")]
    [SerializeField] float gravityScale = 1.5f;
    [Tooltip(">=0 时覆盖碰撞体摩擦；旋转锁定时摩擦过大会几乎不滚，宜偏低")]
    [SerializeField] float rollFriction = 0.03f;
    [Tooltip("落地材质弹力，略大于 0 更像弹跳滚动")]
    [SerializeField, Range(0f, 1f)] float rollBounciness = 0.12f;
    [Tooltip("首次落地时保留的水平速度比例")]
    [SerializeField, Range(0f, 1f)] float landHorizontalRetain = 0.92f;
    [Tooltip("首次落地时保留的向上反弹比例（抑制轻飘回弹）")]
    [SerializeField, Range(0f, 1f)] float landBounceRetain = 0.25f;

    Rigidbody2D rb;
    CircleCollider2D grenadeCollider;
    Animator animator;
    Transform visual;
    float spinDir = 1f;
    bool hasExploded;
    bool fuseExpired;
    bool rollModeActive;
    bool hasLanded;
    float fuseElapsed;
    Vector2 cachedVelocity;
    ContactFilter2D passThroughFilter;
    readonly Collider2D[] overlapBuffer = new Collider2D[PassThroughBufferSize];
    readonly RaycastHit2D[] castBuffer = new RaycastHit2D[PassThroughBufferSize];
    readonly HashSet<Collider2D> ignoredPassThroughs = new HashSet<Collider2D>();
    readonly HashSet<Collider2D> ignoredPlayers = new HashSet<Collider2D>();

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        grenadeCollider = GetComponent<CircleCollider2D>();
        animator = GetComponent<Animator>();
        visual = transform.Find("Sprite");
        rb.constraints = RigidbodyConstraints2D.FreezeRotation;
        SetupPassThroughFilter();
        ApplyPassThroughLayerFilter();
    }

    public void Init(float faceDir, Vector2 throwerVelocity, Collider2D throwerCollider)
    {
        Init(faceDir, throwerVelocity, throwerCollider, defaultThrowAngle, defaultThrowSpeed);
    }

    public void Init(
        float faceDir,
        Vector2 throwerVelocity,
        Collider2D throwerCollider,
        float throwAngleDegrees,
        float throwSpeed)
    {
        float dir = Mathf.Sign(faceDir);
        if (dir == 0f)
            dir = 1f;

        spinDir = dir;

        float rad = throwAngleDegrees * Mathf.Deg2Rad;
        float speed = Mathf.Max(0f, throwSpeed);
        var throwVelocity = new Vector2(dir * speed * Mathf.Cos(rad), speed * Mathf.Sin(rad));

        rb.gravityScale = 1f;
        var inherited = new Vector2(
            throwerVelocity.x * throwerHorizontalInherit,
            throwerVelocity.y * throwerVerticalInherit);
        rb.linearVelocity = inherited + throwVelocity;

        if (throwerCollider != null && grenadeCollider != null)
            Physics2D.IgnoreCollision(grenadeCollider, throwerCollider);

        cachedVelocity = rb.linearVelocity;
        rollModeActive = false;
        BeginPlayerPassThrough();
        IgnoreNearbyPassThroughs();

        if (animator != null)
            animator.Play(RollingStateName, 0, 0f);

        SyncRollAnimSpeed();
    }

    /// <summary>
    /// 贴地滚雷：低抛 + 前冲，落地弹跳滚动，碰玩家或引信到期爆炸。
    /// </summary>
    public void InitRoll(float faceDir, Vector2 throwerVelocity, Collider2D throwerCollider)
    {
        float dir = Mathf.Sign(faceDir);
        if (dir == 0f)
            dir = 1f;

        spinDir = dir;
        rollModeActive = true;
        hasLanded = false;
        fuseElapsed = 0f;
        fuseExpired = fuseTime <= 0f;

        ApplyRollFriction();
        rb.gravityScale = Mathf.Max(0.01f, gravityScale);
        var inherited = new Vector2(
            throwerVelocity.x * throwerHorizontalInherit,
            throwerVelocity.y * throwerVerticalInherit);
        rb.linearVelocity = inherited + new Vector2(dir * horizontalSpeed, verticalSpeed);
        if (forwardImpulse > 0f)
            rb.AddForce(new Vector2(dir * forwardImpulse, 0f), ForceMode2D.Impulse);

        if (throwerCollider != null && grenadeCollider != null)
            Physics2D.IgnoreCollision(grenadeCollider, throwerCollider);

        cachedVelocity = rb.linearVelocity;
        ApplyPassThroughLayerFilter();
        IgnoreNearbyPassThroughs();

        if (animator != null)
            animator.Play(RollingStateName, 0, 0f);

        SyncRollAnimSpeed();

        if (fuseExpired)
            Explode();
    }

    void FixedUpdate()
    {
        if (hasExploded)
            return;

        TickFuse();
        IgnoreNearbyPassThroughs();
        cachedVelocity = rb.linearVelocity;
        SyncRollAnimSpeed();
        SpinVisual();
    }

    bool IsFuseActive => !rollModeActive && !fuseExpired && fuseTime > 0f;

    /// <summary>高抛手雷穿过单向平台；滚雷应落在平台上滚动。</summary>
    bool ShouldPassThroughPlatforms => !isRollGrenade && !rollModeActive;

    void TickFuse()
    {
        if (rollModeActive)
        {
            TickRollFuse();
            return;
        }

        if (fuseExpired)
            return;

        if (fuseTime <= 0f)
        {
            EndPlayerPassThrough();
            return;
        }

        IgnoreNearbyPlayers();
        fuseElapsed += Time.fixedDeltaTime;
        if (fuseElapsed >= fuseTime)
            EndPlayerPassThrough();
    }

    void TickRollFuse()
    {
        if (fuseExpired)
            return;

        if (fuseTime <= 0f)
        {
            fuseExpired = true;
            Explode();
            return;
        }

        fuseElapsed += Time.fixedDeltaTime;
        if (fuseElapsed < fuseTime)
            return;

        fuseExpired = true;
        Explode();
    }

    void ApplyRollFriction()
    {
        if (rollFriction < 0f)
            return;

        var mat = new PhysicsMaterial2D("EnemyGrenadeRollFriction")
        {
            friction = Mathf.Max(0f, rollFriction),
            bounciness = Mathf.Clamp01(rollBounciness),
            frictionCombine = PhysicsMaterialCombine2D.Minimum,
            bounceCombine = PhysicsMaterialCombine2D.Maximum
        };
        rb.sharedMaterial = mat;
        if (grenadeCollider != null)
            grenadeCollider.sharedMaterial = mat;
    }

    void TryApplyLandingFeel(Collision2D collision)
    {
        if (hasLanded || !IsLandingSurface(collision.collider))
            return;

        bool landedOnTop = false;
        for (int i = 0; i < collision.contactCount; i++)
        {
            if (collision.GetContact(i).normal.y > 0.5f)
            {
                landedOnTop = true;
                break;
            }
        }

        if (!landedOnTop)
            return;

        hasLanded = true;
        var velocity = rb.linearVelocity;
        velocity.x *= landHorizontalRetain;
        if (velocity.y > 0f)
            velocity.y *= landBounceRetain;
        rb.linearVelocity = velocity;
    }

    void SyncRollAnimSpeed()
    {
        if (animator == null)
            return;

        float t = rollSpeedReference > 0f
            ? Mathf.Clamp01(Mathf.Abs(rb.linearVelocity.x) / rollSpeedReference)
            : 1f;
        animator.speed = Mathf.Lerp(minRollAnimSpeed, maxRollAnimSpeed, t);
    }

    void SpinVisual()
    {
        if (visual == null || spinDegreesPerSecond == 0f)
            return;

        float dir = Mathf.Abs(rb.linearVelocity.x) > 0.05f
            ? Mathf.Sign(rb.linearVelocity.x)
            : spinDir;
        visual.Rotate(0f, 0f, -dir * spinDegreesPerSecond * Time.fixedDeltaTime);
    }

    void OnCollisionEnter2D(Collision2D collision)
    {
        if (hasExploded)
            return;

        // 高抛：单向平台 / 机器人顶部穿过。滚雷：只穿过机器人顶部，平台落地滚动。
        if (TryPassThrough(collision.collider) || TryPassThroughPlayer(collision.collider))
        {
            RestoreFlightVelocity();
            return;
        }

        if (IsPlayerCollider(collision.collider))
        {
            Explode();
            return;
        }

        if (rollModeActive)
        {
            TryApplyLandingFeel(collision);
            return;
        }

        if (IsGroundCollider(collision.collider))
            Explode();
    }

    void OnCollisionStay2D(Collision2D collision)
    {
        if (hasExploded)
            return;

        if (TryPassThrough(collision.collider) || TryPassThroughPlayer(collision.collider))
            RestoreFlightVelocity();
    }

    static bool IsPlayerCollider(Collider2D collider)
    {
        if (collider == null || MeleeDetectZone.IsSensorCollider(collider))
            return false;

        if (collider.CompareTag("Player"))
            return true;

        var character = collider.GetComponentInParent<Character>();
        return character != null && character.CompareTag("Player");
    }

    void SetupPassThroughFilter()
    {
        LayerMask mask = LayerMask.GetMask("Platform", "RobotTop");
        if (groundLayer.value != 0)
            mask |= groundLayer;

        passThroughFilter = new ContactFilter2D
        {
            useLayerMask = true,
            layerMask = mask,
            useTriggers = false
        };
    }

    void ApplyPassThroughLayerFilter()
    {
        LayerMask platformMask = LayerMask.GetMask("Platform");
        LayerMask robotTopMask = LayerMask.GetMask("RobotTop");

        if (ShouldPassThroughPlatforms)
        {
            LayerMask excluded = platformMask | robotTopMask;
            if (grenadeCollider != null)
            {
                grenadeCollider.includeLayers &= ~platformMask;
                grenadeCollider.excludeLayers |= excluded;
            }

            if (rb != null)
            {
                rb.includeLayers &= ~platformMask;
                rb.excludeLayers |= excluded;
            }

            return;
        }

        // EnemyBullet 与 Platform 在碰撞矩阵中默认不相交，滚雷需显式 include。
        if (grenadeCollider != null)
        {
            grenadeCollider.includeLayers |= platformMask;
            grenadeCollider.excludeLayers |= robotTopMask;
            grenadeCollider.excludeLayers &= ~platformMask;
        }

        if (rb != null)
        {
            rb.includeLayers |= platformMask;
            rb.excludeLayers |= robotTopMask;
            rb.excludeLayers &= ~platformMask;
        }
    }

    void BeginPlayerPassThrough()
    {
        fuseElapsed = 0f;
        fuseExpired = fuseTime <= 0f;
        if (fuseExpired)
            return;

        SetPlayerLayerExcluded(true);
        IgnoreNearbyPlayers();
    }

    void EndPlayerPassThrough()
    {
        if (fuseExpired)
            return;

        fuseExpired = true;
        SetPlayerLayerExcluded(false);
        RestoreIgnoredPlayers();

        if (!hasExploded)
            ExplodeIfOverlappingPlayer();
    }

    void SetPlayerLayerExcluded(bool excluded)
    {
        LayerMask playerMask = GetPlayerLayerMask();
        if (playerMask.value == 0)
            return;

        if (grenadeCollider != null)
        {
            if (excluded)
                grenadeCollider.excludeLayers |= playerMask;
            else
                grenadeCollider.excludeLayers &= ~playerMask;
        }

        if (rb != null)
        {
            if (excluded)
                rb.excludeLayers |= playerMask;
            else
                rb.excludeLayers &= ~playerMask;
        }
    }

    static LayerMask GetPlayerLayerMask()
    {
        int playerLayer = LayerMask.NameToLayer("Player");
        return playerLayer >= 0 ? (LayerMask)(1 << playerLayer) : 0;
    }

    void IgnoreNearbyPlayers()
    {
        if (hasExploded || fuseExpired || grenadeCollider == null)
            return;

        float scale = Mathf.Max(Mathf.Abs(transform.lossyScale.x), Mathf.Abs(transform.lossyScale.y));
        float radius = grenadeCollider.radius * Mathf.Max(0.01f, scale);
        float travel = rb != null ? rb.linearVelocity.magnitude * Time.fixedDeltaTime : 0f;
        float probeRadius = radius + travel + Mathf.Max(0f, passThroughScanPadding);

        LayerMask playerMask = GetPlayerLayerMask();
        var filter = new ContactFilter2D
        {
            useTriggers = false,
            useLayerMask = playerMask.value != 0,
            layerMask = playerMask
        };
        int overlapCount = Physics2D.OverlapCircle(
            grenadeCollider.bounds.center,
            probeRadius,
            filter,
            overlapBuffer);
        for (int i = 0; i < overlapCount; i++)
            TryPassThroughPlayer(overlapBuffer[i]);
    }

    bool TryPassThroughPlayer(Collider2D collider)
    {
        if (!IsFuseActive || !IsPlayerCollider(collider) || grenadeCollider == null)
            return false;

        if (ignoredPlayers.Add(collider))
            Physics2D.IgnoreCollision(grenadeCollider, collider, true);

        if (rb != null)
            rb.WakeUp();
        return true;
    }

    void RestoreIgnoredPlayers()
    {
        if (grenadeCollider != null)
        {
            foreach (Collider2D collider in ignoredPlayers)
            {
                if (collider != null)
                    Physics2D.IgnoreCollision(grenadeCollider, collider, false);
            }
        }

        ignoredPlayers.Clear();
    }

    void ExplodeIfOverlappingPlayer()
    {
        if (hasExploded || grenadeCollider == null)
            return;

        float scale = Mathf.Max(Mathf.Abs(transform.lossyScale.x), Mathf.Abs(transform.lossyScale.y));
        float radius = grenadeCollider.radius * Mathf.Max(0.01f, scale);
        LayerMask playerMask = GetPlayerLayerMask();
        var filter = new ContactFilter2D
        {
            useTriggers = false,
            useLayerMask = playerMask.value != 0,
            layerMask = playerMask
        };
        int overlapCount = Physics2D.OverlapCircle(
            grenadeCollider.bounds.center,
            radius + 0.02f,
            filter,
            overlapBuffer);
        for (int i = 0; i < overlapCount; i++)
        {
            if (IsPlayerCollider(overlapBuffer[i]))
            {
                Explode();
                return;
            }
        }
    }

    void IgnoreNearbyPassThroughs()
    {
        if (hasExploded || grenadeCollider == null || rb == null)
            return;

        passThroughFilter.layerMask = LayerMask.GetMask("Platform", "RobotTop");
        if (groundLayer.value != 0)
            passThroughFilter.layerMask |= groundLayer;

        float scale = Mathf.Max(Mathf.Abs(transform.lossyScale.x), Mathf.Abs(transform.lossyScale.y));
        float radius = grenadeCollider.radius * Mathf.Max(0.01f, scale);
        float travel = rb.linearVelocity.magnitude * Time.fixedDeltaTime;
        float probeRadius = radius + travel + Mathf.Max(0f, passThroughScanPadding);

        int overlapCount = Physics2D.OverlapCircle(
            grenadeCollider.bounds.center,
            probeRadius,
            passThroughFilter,
            overlapBuffer);
        for (int i = 0; i < overlapCount; i++)
            TryPassThrough(overlapBuffer[i]);

        if (rb.linearVelocity.sqrMagnitude < 0.0001f)
            return;

        float castDistance = travel + Mathf.Max(0f, passThroughScanPadding);
        int hitCount = grenadeCollider.Cast(
            rb.linearVelocity.normalized,
            passThroughFilter,
            castBuffer,
            castDistance);
        for (int i = 0; i < hitCount; i++)
            TryPassThrough(castBuffer[i].collider);
    }

    void RestoreFlightVelocity()
    {
        if (rb != null)
            rb.linearVelocity = cachedVelocity;
    }

    bool TryPassThrough(Collider2D collider)
    {
        if (!IsPassThroughCollider(collider) || grenadeCollider == null)
            return false;

        if (ignoredPassThroughs.Add(collider))
            Physics2D.IgnoreCollision(grenadeCollider, collider, true);

        if (rb != null)
            rb.WakeUp();
        return true;
    }

    bool IsPassThroughCollider(Collider2D collider)
    {
        if (collider == null)
            return false;

        if (IsRobotTopCollider(collider))
            return true;

        if (!ShouldPassThroughPlatforms)
            return false;

        if (IsPlatformLayer(collider))
            return true;

        return FallingPlatform.IsOneWayPlatformCollider(collider);
    }

    static bool IsPlatformLayer(Collider2D collider)
    {
        int platformLayer = LayerMask.NameToLayer("Platform");
        return platformLayer >= 0 && collider.gameObject.layer == platformLayer;
    }

    static bool IsRobotTopCollider(Collider2D collider)
    {
        if (collider == null)
            return false;

        if (collider.GetComponent<RobotTopPlatform>() != null)
            return true;

        int robotTopLayer = LayerMask.NameToLayer("RobotTop");
        return robotTopLayer >= 0 && collider.gameObject.layer == robotTopLayer;
    }

    bool IsGroundCollider(Collider2D collider)
    {
        if (groundLayer.value == 0 || collider == null)
            return false;

        if (IsPassThroughCollider(collider))
            return false;

        return (groundLayer.value & (1 << collider.gameObject.layer)) != 0;
    }

    bool IsLandingSurface(Collider2D collider)
    {
        if (IsGroundCollider(collider))
            return true;

        if (!rollModeActive || collider == null || IsRobotTopCollider(collider))
            return false;

        return IsPlatformLayer(collider) || FallingPlatform.IsOneWayPlatformCollider(collider);
    }

    void Explode()
    {
        if (hasExploded)
            return;

        hasExploded = true;

        if (explosionPrefab != null)
        {
            var explosion = Instantiate(explosionPrefab, GetExplosionPosition(), Quaternion.identity);
            explosion.gameObject.name = "EnemyGrenadeExplosion";
            EnemySceneCleanup.PlaceInSourceScene(explosion.gameObject, this);
        }

        Destroy(gameObject);
    }

    public bool TryCancelByMelee(Attack attacker)
    {
        if (hasExploded || attacker == null)
            return false;

        // 抵销：直接销毁，不引爆
        hasExploded = true;
        Destroy(gameObject);
        return true;
    }

    Vector3 GetExplosionPosition()
    {
        float x = transform.position.x;
        float z = transform.position.z;
        float probeY = grenadeCollider != null
            ? grenadeCollider.bounds.max.y
            : transform.position.y + 0.1f;

        LayerMask snapMask = groundLayer;
        if (rollModeActive)
            snapMask |= LayerMask.GetMask("Platform");

        if (snapMask.value != 0)
        {
            RaycastHit2D[] hits = Physics2D.RaycastAll(
                new Vector2(x, probeY), Vector2.down, groundSnapRayDistance, snapMask);
            float bestDistance = float.PositiveInfinity;
            Vector2 bestPoint = Vector2.zero;
            bool found = false;
            for (int i = 0; i < hits.Length; i++)
            {
                Collider2D col = hits[i].collider;
                if (col == null || IsPassThroughCollider(col))
                    continue;

                if (hits[i].distance < bestDistance)
                {
                    bestDistance = hits[i].distance;
                    bestPoint = hits[i].point;
                    found = true;
                }
            }

            if (found)
                return new Vector3(x, bestPoint.y, z);
        }

        if (grenadeCollider != null)
            return new Vector3(x, grenadeCollider.bounds.min.y, z);

        return transform.position;
    }
}
