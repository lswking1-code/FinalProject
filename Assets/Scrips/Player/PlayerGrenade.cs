using System.Collections.Generic;
using FMODUnity;
using UnityEngine;
using UnityEngine.SceneManagement;

[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(CircleCollider2D))]
[RequireComponent(typeof(Animator))]
public class PlayerGrenade : MonoBehaviour
{
    const string AirEnemyTag = "AirEnemy";
    const string EnemyTag = "Enemy";
    const string RollingStateName = "GrenadeRolling";

    [SerializeField] float horizontalSpeed = 6.5f;
    [SerializeField] float verticalSpeed = 6.5f;
    [Tooltip("投掷后的重力倍率，越大下落越干脆")]
    [SerializeField] float gravityScale = 1.6f;
    [Tooltip("生成时沿面向施加的冲量；0 表示不加力")]
    [SerializeField] float forwardImpulse = 0f;
    [Tooltip(">=0 时覆盖碰撞体摩擦；旋转锁定时摩擦过大会几乎不滚，宜偏低")]
    [SerializeField] float rollFriction = 0.04f;
    [Tooltip("落地材质弹力，略大于 0 更像弹跳滚动")]
    [SerializeField, Range(0f, 1f)] float rollBounciness = 0.18f;
    [Tooltip("首次落地时保留的水平速度比例")]
    [SerializeField, Range(0f, 1f)] float landHorizontalRetain = 0.92f;
    [Tooltip("首次落地时保留的向上反弹比例（抑制轻飘回弹）")]
    [SerializeField, Range(0f, 1f)] float landBounceRetain = 0.25f;
    [SerializeField] float fuseTime = 2.5f;
    [SerializeField] GrenadeExplosion explosionPrefab;
    [Header("音效")]
    [SerializeField] EventReference explodeEvent;
    [SerializeField, Range(0f, 1f)] float playerHorizontalInherit = 0.5f;
    [SerializeField, Range(0f, 1f)] float playerVerticalInherit = 0f;
    [SerializeField] float rollSpeedReference = 12f;
    [SerializeField] float minRollAnimSpeed = 0.6f;
    [SerializeField] float maxRollAnimSpeed = 1.8f;
    [SerializeField] LayerMask groundLayer;
    [SerializeField] float groundSnapRayDistance = 1.5f;
    [Header("单向平台")]
    [SerializeField, Min(0f)] float platformSurfaceMargin = 0.02f;

    readonly List<Collider2D> nearbyPlatforms = new List<Collider2D>(16);
    readonly HashSet<Collider2D> trackedPlatforms = new HashSet<Collider2D>();
    readonly HashSet<Collider2D> ignoredPlatforms = new HashSet<Collider2D>();
    readonly List<Collider2D> platformsToRemove = new List<Collider2D>(16);
    ContactFilter2D platformFilter;

    Rigidbody2D rb;
    CircleCollider2D grenadeCollider;
    Animator animator;
    bool hasExploded;
    bool hasLanded;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        grenadeCollider = GetComponent<CircleCollider2D>();
        animator = GetComponent<Animator>();
        rb.constraints = RigidbodyConstraints2D.FreezeRotation;
        platformFilter = new ContactFilter2D
        {
            useLayerMask = true,
            layerMask = groundLayer.value | LayerMask.GetMask("Ground", "Platform"),
            useTriggers = false
        };
        ApplyRollFriction();
    }

    void ApplyRollFriction()
    {
        if (rollFriction < 0f)
            return;

        var mat = new PhysicsMaterial2D("GrenadeRollFriction")
        {
            friction = Mathf.Max(0f, rollFriction),
            bounciness = Mathf.Clamp01(rollBounciness),
            // 取较小摩擦，避免地面材质把滚动直接刹死
            frictionCombine = PhysicsMaterialCombine2D.Minimum,
            bounceCombine = PhysicsMaterialCombine2D.Maximum
        };
        rb.sharedMaterial = mat;
        if (grenadeCollider != null)
            grenadeCollider.sharedMaterial = mat;
    }

    public void Init(float faceDir, Vector2 playerVelocity, Collider2D playerCollider)
    {
        float dir = Mathf.Sign(faceDir);
        if (dir == 0f)
            dir = 1f;

        rb.gravityScale = Mathf.Max(0.01f, gravityScale);
        var inherited = new Vector2(
            playerVelocity.x * playerHorizontalInherit,
            playerVelocity.y * playerVerticalInherit);
        rb.linearVelocity = inherited + new Vector2(dir * horizontalSpeed, verticalSpeed);
        if (forwardImpulse > 0f)
            rb.AddForce(new Vector2(dir * forwardImpulse, 0f), ForceMode2D.Impulse);

        if (playerCollider != null && grenadeCollider != null)
            Physics2D.IgnoreCollision(grenadeCollider, playerCollider);

        if (animator != null)
            animator.Play(RollingStateName, 0, 0f);

        rb.SetRotation(0f);
        UpdatePlatformCollisions();
        SyncRollAnimSpeed();
        Invoke(nameof(Explode), fuseTime);
    }

    void FixedUpdate()
    {
        if (!hasExploded)
        {
            UpdatePlatformCollisions();
            SyncRollAnimSpeed();
        }
    }

    void UpdatePlatformCollisions()
    {
        Bounds bounds = grenadeCollider.bounds;
        float dt = Time.fixedDeltaTime;
        Vector2 step = (rb.linearVelocity + Physics2D.gravity * rb.gravityScale * dt) * dt;
        float padding = Mathf.Max(0.1f, platformSurfaceMargin * 2f);
        Vector2 scanSize = (Vector2)bounds.size
            + new Vector2(Mathf.Abs(step.x), Mathf.Abs(step.y)) + Vector2.one * (padding * 2f);
        gameObject.scene.GetPhysicsScene2D().OverlapBox((Vector2)bounds.center + step * 0.5f, scanSize, 0f,
            platformFilter, nearbyPlatforms);

        for (int i = 0; i < nearbyPlatforms.Count; i++)
        {
            Collider2D platform = nearbyPlatforms[i];
            if (!IsOneWayPlatform(platform))
                continue;

            float distance = GetDistanceAbovePlatform(platform, bounds);
            // 新接近或穿越中的手雷必须完全越过顶面才能恢复碰撞。
            // 已由顶部承托的碰撞对允许少量物理穿入，避免滚动时掉板。
            bool wasColliding = trackedPlatforms.Contains(platform) && !ignoredPlatforms.Contains(platform);
            float threshold = wasColliding ? -platformSurfaceMargin : platformSurfaceMargin;
            SetPlatformIgnored(platform, distance < threshold);
            trackedPlatforms.Add(platform);
        }

        platformsToRemove.Clear();
        foreach (Collider2D platform in trackedPlatforms)
        {
            if (platform != null && IsOneWayPlatform(platform) && nearbyPlatforms.Contains(platform))
                continue;

            SetPlatformIgnored(platform, false);
            platformsToRemove.Add(platform);
        }
        foreach (Collider2D platform in platformsToRemove)
            trackedPlatforms.Remove(platform);
    }

    static bool IsOneWayPlatform(Collider2D platform) =>
        platform != null && platform.enabled && !platform.isTrigger && platform.usedByEffector
        && FallingPlatform.IsOneWayPlatformCollider(platform);

    static float GetDistanceAbovePlatform(Collider2D platform, Bounds grenadeBounds)
    {
        Vector2 center = grenadeBounds.center;
        // CircleCollider2D 的世界半径；沿坡面法线计算圆的最低支持点。
        float radius = Mathf.Max(grenadeBounds.extents.x, grenadeBounds.extents.y);
        var pathSlope = platform.GetComponentInParent<SlopePathSegment>();
        if (pathSlope != null)
            return pathSlope.GetSignedDistanceToSurface(center) - radius;

        var slope = platform.GetComponentInParent<SlopeOneWayPlatform>();
        if (slope != null)
            return slope.GetSignedDistanceToSurface(center) - radius;

        // 普通旋转平台也使用实际顶边，而不是旋转后 AABB 的最高点。
        if (platform is BoxCollider2D box)
        {
            Vector2 normal = box.transform.up;
            Vector2 top = box.transform.TransformPoint(box.offset + Vector2.up * (box.size.y * 0.5f));
            return Vector2.Dot(center - top, normal) - radius;
        }

        return grenadeBounds.min.y - platform.bounds.max.y;
    }

    void SetPlatformIgnored(Collider2D platform, bool ignore)
    {
        bool changed = ignore ? ignoredPlatforms.Add(platform) : ignoredPlatforms.Remove(platform);
        if (changed && platform != null && grenadeCollider != null)
            Physics2D.IgnoreCollision(grenadeCollider, platform, ignore);
    }

    void OnDisable()
    {
        foreach (Collider2D platform in trackedPlatforms)
            SetPlatformIgnored(platform, false);
        trackedPlatforms.Clear();
        ignoredPlatforms.Clear();
        nearbyPlatforms.Clear();
        platformsToRemove.Clear();
    }

    void LateUpdate()
    {
        if (!hasExploded)
            SyncHalfTurnRotation();
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

    // 16 帧美术只覆盖 180°：clip 前半 0°，后半同一套帧 + 转 180°，拼成完整 360° 循环
    void SyncHalfTurnRotation()
    {
        if (animator == null || rb == null)
            return;

        var info = animator.GetCurrentAnimatorStateInfo(0);
        if (!info.IsName(RollingStateName))
            return;

        float cycleT = info.normalizedTime - Mathf.Floor(info.normalizedTime);
        float z = cycleT < 0.5f ? 0f : 180f;
        if (!Mathf.Approximately(rb.rotation, z))
            rb.SetRotation(z);
    }

    void OnCollisionEnter2D(Collision2D collision)
    {
        if (hasExploded)
            return;

        if (IsEnemyCollider(collision.collider))
        {
            Explode();
            return;
        }

        TryApplyLandingFeel(collision);
    }

    void TryApplyLandingFeel(Collision2D collision)
    {
        if (hasLanded)
            return;

        int layerBit = 1 << collision.collider.gameObject.layer;
        if ((groundLayer.value & layerBit) == 0 && !IsOneWayPlatform(collision.collider))
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

    static bool IsEnemyCollider(Collider2D collider)
    {
        if (collider.CompareTag(EnemyTag) || collider.CompareTag(AirEnemyTag))
            return true;

        var character = collider.GetComponentInParent<Character>();
        return character != null
            && (character.CompareTag(EnemyTag) || character.CompareTag(AirEnemyTag));
    }

    void Explode()
    {
        if (hasExploded)
            return;

        hasExploded = true;
        CancelInvoke(nameof(Explode));

        Vector3 explodePos = GetExplosionPosition();
        if (!explodeEvent.IsNull)
            FmodAudio.Play(explodeEvent, explodePos);

        if (explosionPrefab != null)
            Instantiate(explosionPrefab, explodePos, Quaternion.identity);

        Destroy(gameObject);
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
            var hit = Physics2D.Raycast(new Vector2(x, probeY), Vector2.down, groundSnapRayDistance, groundLayer);
            if (hit.collider != null)
                return new Vector3(x, hit.point.y, z);
        }

        if (grenadeCollider != null)
            return new Vector3(x, grenadeCollider.bounds.min.y, z);

        return transform.position;
    }
}
