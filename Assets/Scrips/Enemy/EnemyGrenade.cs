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
    [Tooltip("投出后的引信时间。期间碰到玩家不爆炸并穿过；到期后碰到玩家会爆炸。落地引爆不受影响。")]
    [SerializeField] float fuseTime = 0.5f;
    [Tooltip("命中该层时引爆（地面）")]
    [SerializeField] LayerMask groundLayer;
    [SerializeField] float groundSnapRayDistance = 1.5f;
    [Tooltip("物理步进前预扫描并 Ignore 平台的额外半径，避免接触当帧被托住")]
    [SerializeField] float passThroughScanPadding = 1f;

    Rigidbody2D rb;
    CircleCollider2D grenadeCollider;
    Animator animator;
    Transform visual;
    float spinDir = 1f;
    bool hasExploded;
    bool fuseExpired;
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
        ExcludePassThroughLayers();
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
        BeginPlayerPassThrough();
        IgnoreNearbyPassThroughs();

        if (animator != null)
            animator.Play(RollingStateName, 0, 0f);

        SyncRollAnimSpeed();
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

    bool IsFuseActive => !fuseExpired && fuseTime > 0f;

    void TickFuse()
    {
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

        // 单向平台 / 机器人顶部仅给角色站立，手雷应穿过、不引爆、不吸附
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

    void ExcludePassThroughLayers()
    {
        LayerMask excluded = LayerMask.GetMask("Platform", "RobotTop");
        if (grenadeCollider != null)
            grenadeCollider.excludeLayers |= excluded;
        if (rb != null)
            rb.excludeLayers |= excluded;
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

    static bool IsPassThroughCollider(Collider2D collider)
    {
        if (collider == null)
            return false;

        if (IsRobotTopCollider(collider) || IsPlatformLayer(collider))
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

        if (groundLayer.value != 0)
        {
            RaycastHit2D[] hits = Physics2D.RaycastAll(
                new Vector2(x, probeY), Vector2.down, groundSnapRayDistance, groundLayer);
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
