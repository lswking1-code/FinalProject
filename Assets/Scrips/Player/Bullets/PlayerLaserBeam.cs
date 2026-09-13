using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 镭射枪持续光束：跟随枪口、带宽度判定穿透普通敌人，截断于墙/平台/精英；松手播结束动画后销毁。
/// </summary>
public class PlayerLaserBeam : MonoBehaviour
{
    const string HeadFireState = "laser_gun_beam_head";
    const string HeadEndState = "laser_gun_beam_head_end";
    const string BeamFireState = "laserbeam";
    const string BeamEndState = "laserbeam_end";

    [Header("伤害")]
    [SerializeField] int damage = 10;
    [SerializeField] float tickInterval = 0.1f;
    [SerializeField] float abilityPowerRestore = 5f;
    [SerializeField] float maxRange = 12f;
    [Tooltip("光束判定总宽度（世界单位）；0 使用原直线判定。")]
    [SerializeField, Min(0f)] float hitWidth = 0.5f;
    [SerializeField] LayerMask hitMask;

    [Header("视觉")]
    [SerializeField] Transform head;
    [SerializeField] Transform beam;
    [SerializeField] Transform blast;
    [SerializeField] Animator headAnimator;
    [SerializeField] Animator beamAnimator;
    [SerializeField] float endDuration = 0.33f;
    [SerializeField] float beamSpriteWorldLength = 1f;

    Attack attackSource;
    readonly Dictionary<Character, float> nextHitTime = new();
    readonly Dictionary<IHitCountable, float> nextHitCountableTime = new();
    readonly Dictionary<int, float> nextHitSfxTime = new();
    RaycastHit2D[] hitBuffer = new RaycastHit2D[32];
    readonly List<RaycastHit2D> sortedHits = new(32);

    Transform firePoint;
    FireDir fireDir;
    float faceY;
    Character owner;
    bool ending;
    float endAt = -1f;
    float currentLength;

    public bool IsEnding => ending;
    public bool IsAlive => !ending || Time.time < endAt;

    void Awake()
    {
        if (hitMask.value == 0)
            hitMask = LayerMask.GetMask("Ground", "Platform", "Enemy", "EliteEnemy", "Item", "Device");

        EnsureAttackSource();
    }

    void EnsureAttackSource()
    {
        if (attackSource != null)
            return;

        var go = new GameObject("LaserAttack");
        go.transform.SetParent(transform, false);
        attackSource = go.AddComponent<Attack>();
        attackSource.attackType = AttackType.Melee;
        attackSource.ignoreTag = "Player";
        attackSource.damage = damage;
        attackSource.chargesEnergyNode = true;
        attackSource.impactKind = MachinistImpactKind.Bullet;
        attackSource.enabled = false; // 仅作 TakeDamage 数据源，不做 Trigger 碰撞
    }

    public void Begin(Transform point, FireDir dir, float faceYaw, Character ownerCharacter)
    {
        firePoint = point;
        fireDir = dir;
        faceY = faceYaw;
        owner = ownerCharacter;
        ending = false;
        endAt = -1f;
        nextHitTime.Clear();
        nextHitCountableTime.Clear();
        nextHitSfxTime.Clear();

        attackSource.damage = damage;
        transform.rotation = PlayerProjectile.GetRotation(dir, faceYaw);

        if (firePoint != null)
            transform.position = firePoint.position;

        if (headAnimator != null)
            headAnimator.Play(HeadFireState, 0, 0f);
        if (beamAnimator != null)
            beamAnimator.Play(BeamFireState, 0, 0f);

        if (blast != null)
            blast.gameObject.SetActive(true);

        UpdateBeam(point, dir, faceYaw);
    }

    public void UpdateBeam(Transform point, FireDir dir, float faceYaw)
    {
        if (ending)
            return;

        firePoint = point;
        fireDir = dir;
        faceY = faceYaw;

        if (firePoint != null)
            transform.position = firePoint.position;

        transform.rotation = PlayerProjectile.GetRotation(dir, faceYaw);
        ApplyRaycastAndVisuals(dealDamage: true);
    }

    public void BeginEnd()
    {
        if (ending)
            return;

        ending = true;
        endAt = Time.time + Mathf.Max(0.05f, endDuration);

        if (headAnimator != null)
            headAnimator.Play(HeadEndState, 0, 0f);
        if (beamAnimator != null)
            beamAnimator.Play(BeamEndState, 0, 0f);
        if (blast != null)
            blast.gameObject.SetActive(false);
    }

    void LateUpdate()
    {
        if (firePoint != null)
            transform.position = firePoint.position;

        if (ending && Time.time >= endAt)
            Destroy(gameObject);
    }

    void ApplyRaycastAndVisuals(bool dealDamage)
    {
        Vector2 origin = transform.position;
        Vector2 direction = transform.right;
        float range = Mathf.Max(0.01f, maxRange);

        int count = CastBeam(origin, direction, range);
        sortedHits.Clear();
        for (int i = 0; i < count; i++)
        {
            if (hitBuffer[i].collider != null)
                sortedHits.Add(hitBuffer[i]);
        }

        sortedHits.Sort((a, b) => a.distance.CompareTo(b.distance));

        float stopDistance = range;
        Vector2 tip = origin + direction * range;

        for (int i = 0; i < sortedHits.Count; i++)
        {
            RaycastHit2D hit = sortedHits[i];
            Collider2D col = hit.collider;
            if (col == null)
                continue;

            if (ShouldIgnoreCollider(col))
                continue;

            Enemy enemy = col.GetComponentInParent<Enemy>();
            Character character = col.GetComponentInParent<Character>();
            IHitCountable hitCountable = col.GetComponentInParent<IHitCountable>();

            bool isBlockSurface = IsBlockSurface(col);
            bool isEliteBlock = enemy != null && enemy.blocksLaser;

            if (dealDamage && enemy != null && character != null && character != owner)
                TryTickDamage(character, col, hit.point, direction);

            if (dealDamage && hitCountable != null)
                TryTickHitCountable(hitCountable);

            if (isBlockSurface || isEliteBlock)
            {
                // 起点重叠时接触点不可靠，直接在枪口截断。
                stopDistance = hit.distance <= 0f ? 0f
                    : Mathf.Clamp(Vector2.Dot(hit.point - origin, direction), 0f, range);
                tip = origin + direction * stopDistance;
                if (dealDamage && (character == null || character == owner))
                    attackSource.ReportImpact(col, MachinistImpactKind.Surface, hit.point, direction);
                break;
            }
        }

        currentLength = stopDistance;
        ApplyVisualLength(stopDistance, tip);
    }

    int CastBeam(Vector2 origin, Vector2 direction, float range)
    {
        float width = Mathf.Max(0f, hitWidth);
        const float castDepth = 0.01f;
        // 矩形后沿从枪口开始，前沿最多到达 range，不额外延长射程。
        Vector2 castOrigin = origin + direction * (castDepth * 0.5f);
        float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
        var filter = new ContactFilter2D { useTriggers = Physics2D.queriesHitTriggers };
        filter.SetLayerMask(hitMask);
        while (true)
        {
            int count = width > 0f
                ? Physics2D.BoxCast(castOrigin, new Vector2(castDepth, width), angle,
                    direction, filter, hitBuffer, Mathf.Max(0f, range - castDepth))
                : Physics2D.RaycastNonAlloc(origin, direction, hitBuffer, range, hitMask);
            if (count < hitBuffer.Length)
                return count;

            // 加宽后可能同时覆盖更多碰撞体，不能因缓冲区满而漏掉阻挡物。
            System.Array.Resize(ref hitBuffer, hitBuffer.Length * 2);
        }
    }

    bool ShouldIgnoreCollider(Collider2D col)
    {
        if (col.isTrigger
            && col.GetComponentInParent<Enemy>() == null
            && col.GetComponentInParent<IHitCountable>() == null)
            return true;

        if (owner != null)
        {
            Transform ownerTf = owner.transform;
            if (col.transform == ownerTf || col.transform.IsChildOf(ownerTf))
                return true;
        }

        if (col.CompareTag("Player"))
            return true;

        return false;
    }

    bool IsBlockSurface(Collider2D col)
    {
        if (col == null)
            return false;

        // Ground 始终截断；Platform 中单向平台可被穿透
        string layerName = LayerMask.LayerToName(col.gameObject.layer);
        if (layerName == "Ground")
            return true;

        if (layerName == "Platform")
            return !FallingPlatform.IsOneWayPlatformCollider(col);

        return false;
    }

    void TryTickDamage(Character target, Collider2D hitCollider, Vector2 hitPoint, Vector2 direction)
    {
        if (target == null || !target.CanReceiveHits)
            return;

        if (nextHitTime.TryGetValue(target, out float next) && Time.time < next)
            return;

        attackSource.damage = damage;
        bool damaged = target.TakeDamage(attackSource);
        nextHitTime[target] = Time.time + Mathf.Max(0.01f, tickInterval);

        if (damaged)
        {
            attackSource.ReportImpact(hitCollider, MachinistImpactKind.Bullet, hitPoint, direction);
            if (owner != null && abilityPowerRestore > 0f)
                owner.RestoreAbilityPower(abilityPowerRestore);
            return;
        }

        if (!target.IsDead && target.invulnerable && !target.IsForcedInvulnerable)
        {
            attackSource.ReportImpact(hitCollider, MachinistImpactKind.Bullet, hitPoint, direction);
            target.GetComponent<Enemy>()?.PlayHitSfx(attackSource);
        }
    }

    internal void PlayHitSfx(int targetId, Vector3 position)
    {
        if (ending || (nextHitSfxTime.TryGetValue(targetId, out float next) && Time.time < next))
            return;

        nextHitSfxTime[targetId] = Time.time + Mathf.Max(0.01f, tickInterval);
        FmodAudio.Play(GunnerAudio.LaserHit, position);
    }

    void TryTickHitCountable(IHitCountable target)
    {
        if (target == null)
            return;

        if (nextHitCountableTime.TryGetValue(target, out float next) && Time.time < next)
            return;

        if (!target.RegisterHit(attackSource))
            return;

        nextHitCountableTime[target] = Time.time + Mathf.Max(0.01f, tickInterval);
    }

    void ApplyVisualLength(float length, Vector2 tipWorld)
    {
        float safeLen = Mathf.Max(0f, length);
        float natural = Mathf.Max(0.01f, beamSpriteWorldLength);

        if (beam != null)
        {
            Vector3 scale = beam.localScale;
            scale.x = safeLen / natural;
            beam.localScale = scale;
            beam.localPosition = Vector3.zero;
        }

        if (blast != null && blast.gameObject.activeSelf)
            blast.position = tipWorld;
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Vector3 origin = Application.isPlaying && firePoint != null ? firePoint.position : transform.position;
        Vector3 dir = transform.right;
        float len = Application.isPlaying ? currentLength : maxRange;
        Gizmos.DrawLine(origin, origin + dir * len);
        Vector3 halfWidth = Vector3.Cross(Vector3.forward, dir).normalized * (Mathf.Max(0f, hitWidth) * 0.5f);
        Vector3 end = origin + dir * len;
        Gizmos.DrawLine(origin + halfWidth, end + halfWidth);
        Gizmos.DrawLine(origin - halfWidth, end - halfWidth);
        Gizmos.DrawLine(origin - halfWidth, origin + halfWidth);
        Gizmos.DrawLine(end - halfWidth, end + halfWidth);
    }
#endif
}
