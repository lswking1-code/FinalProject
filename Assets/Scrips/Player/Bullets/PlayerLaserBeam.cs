using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 镭射枪持续光束：跟随枪口、射线穿透普通敌人，截断于墙/平台/精英；松手播结束动画后销毁。
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
    [SerializeField] float maxRange = 12f;
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
    readonly RaycastHit2D[] hitBuffer = new RaycastHit2D[32];
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
            hitMask = LayerMask.GetMask("Ground", "Platform", "Enemy");

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

        int count = Physics2D.RaycastNonAlloc(origin, direction, hitBuffer, range, hitMask);
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
                TryTickDamage(character);

            if (dealDamage && hitCountable != null)
                TryTickHitCountable(hitCountable);

            if (isBlockSurface || isEliteBlock)
            {
                stopDistance = hit.distance;
                tip = hit.point;
                break;
            }
        }

        currentLength = stopDistance;
        ApplyVisualLength(stopDistance, tip);
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

    void TryTickDamage(Character target)
    {
        if (target == null || target.IsDead)
            return;

        if (nextHitTime.TryGetValue(target, out float next) && Time.time < next)
            return;

        attackSource.damage = damage;
        target.TakeDamage(attackSource);
        nextHitTime[target] = Time.time + Mathf.Max(0.01f, tickInterval);
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
    }
#endif
}
