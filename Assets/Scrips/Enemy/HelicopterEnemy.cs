using UnityEngine;

/// <summary>
/// 直升机：飞行循环与无人机相同，攻击改为按 HelicopterSummonProfile 召唤小兵。
/// 专注模式不追击、不走位，只原地循环召唤。
/// </summary>
[RequireComponent(typeof(EnemyGenerate))]
[RequireComponent(typeof(Character))]
public class HelicopterEnemy : FlyingEnemy
{
    [Header("直升机")]
    [Tooltip("专注模式：不追玩家、不扇区走位，只原地召唤")]
    public bool enableFocusMode;
    [Tooltip("场景预置 / 未覆盖遭遇条目时使用的默认召唤编制")]
    public HelicopterSummonProfile defaultSummonProfile;

    EnemyGenerate summonGenerator;
    HelicopterSummonProfile currentProfile;
    bool summonProfileApplied;
    bool hasStarted;
    bool subscribedDie;
    bool isDeparting;
    Collider2D[] departDisabledColliders;

    protected override void Awake()
    {
        base.Awake();
        shotState = new HelicopterSpawnState();
        departState = new HelicopterDepartState();
        RecacheSpriteRendererFromChild("Sprite");
        if (GetComponent<SpriteRenderer>() == null)
            RecacheSpriteRendererFromChild("Visual");

        summonGenerator = GetComponent<EnemyGenerate>();
        var generatePoint = transform.Find("GeneratePoint");
        summonGenerator?.SetFallbackSpawnPoint(generatePoint);
        EnsureSummonProfileApplied();
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        SubscribeDie();
        if (hasStarted && !isPatrol && !isApproachingSpawnTarget && !isDead && !isDeparting)
        {
            isAggro = true;
            EvaluateCycle();
        }
    }

    protected override void OnDisable()
    {
        UnsubscribeDie();
        StopSummonAttack();
        base.OnDisable();
    }

    protected override void Update()
    {
        if (CurrentState == null)
            return;
        base.Update();
    }

    protected override void FixedUpdate()
    {
        if (CurrentState == null)
            return;
        base.FixedUpdate();
    }

    void Start()
    {
        hasStarted = true;
        EnsureSummonProfileApplied();
        if (isDead || isApproachingSpawnTarget || isPatrol || isDeparting)
            return;
        if (CurrentState != null)
            return;

        isAggro = true;
        EvaluateCycle();
    }

    protected override bool ShouldStartCombatOnEnable() => false;

    public override void ApplyEncounterFocusMode(bool enabled) => enableFocusMode = enabled;

    public override void ApplyEncounterSuicideBomb(bool enabled)
    {
    }

    public void ApplySummonProfile(HelicopterSummonProfile profile)
    {
        summonProfileApplied = true;
        currentProfile = profile;
        summonGenerator?.ApplySummonProfile(profile);
    }

    public bool ShouldDepartAfterSummon =>
        currentProfile != null
        && currentProfile.leaveAfterSpawn
        && !currentProfile.infiniteRefresh
        && (summonGenerator == null || !summonGenerator.HasInfiniteRefresh);

    public float DepartDestroyDelay =>
        currentProfile != null ? Mathf.Max(0.1f, currentProfile.leaveDestroyDelay) : 3f;

    public override void BeginReturnHome()
    {
        if (isDeparting)
            return;
        base.BeginReturnHome();
    }

    public override void EvaluateCycle()
    {
        if (isDeparting || isDead)
            return;

        if (enableFocusMode)
        {
            if (isApproachingSpawnTarget)
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

            SwitchState(NPCState.Shot);
            return;
        }

        base.EvaluateCycle();
    }

    /// <summary>
    /// 开始本轮召唤。未配置编制时返回 false，状态机应立刻进入后摇。
    /// </summary>
    public bool StartSummonAttack()
    {
        EnsureSummonProfileApplied();
        if (summonGenerator == null)
            return false;
        return summonGenerator.StartSummon();
    }

    public bool IsSummonFinished => summonGenerator == null || !summonGenerator.IsSummonAttackBusy;

    public void StopSummonAttack()
    {
        summonGenerator?.StopSummon();
    }

    void EnsureSummonProfileApplied()
    {
        if (summonProfileApplied)
            return;
        ApplySummonProfile(defaultSummonProfile);
    }

    void SubscribeDie()
    {
        if (subscribedDie || character == null)
            return;

        character.OnDie.AddListener(OnHelicopterDied);
        subscribedDie = true;
    }

    void UnsubscribeDie()
    {
        if (!subscribedDie || character == null)
            return;

        character.OnDie.RemoveListener(OnHelicopterDied);
        subscribedDie = false;
    }

    public void BeginDepart()
    {
        isDeparting = true;
        blockSeparation = true;
        SetDepartCollidersEnabled(false);
    }

    public void ApplyDepartAscent()
    {
        if (Rb == null)
            return;

        float speed = chaseSpeed > 0f ? chaseSpeed : Mathf.Max(normalSpeed, 2f);
        Rb.linearVelocity = new Vector2(0f, speed);
    }

    public void FinishDepartDestroy()
    {
        if (isDead)
            return;

        Destroy(gameObject);
    }

    public void EndDepart()
    {
        blockSeparation = false;
        SetDepartCollidersEnabled(true);
        if (Rb != null && !isDead)
            Rb.linearVelocity = Vector2.zero;
    }

    void SetDepartCollidersEnabled(bool enabled)
    {
        if (!enabled)
        {
            var all = GetComponentsInChildren<Collider2D>(true);
            int count = 0;
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != null && all[i].enabled && !all[i].isTrigger)
                    count++;
            }

            departDisabledColliders = count > 0 ? new Collider2D[count] : null;
            int write = 0;
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] == null || !all[i].enabled || all[i].isTrigger)
                    continue;
                departDisabledColliders[write++] = all[i];
                all[i].enabled = false;
            }

            return;
        }

        if (departDisabledColliders == null)
            return;

        for (int i = 0; i < departDisabledColliders.Length; i++)
        {
            if (departDisabledColliders[i] != null)
                departDisabledColliders[i].enabled = true;
        }

        departDisabledColliders = null;
    }

    void OnHelicopterDied()
    {
        isDeparting = false;
        StopSummonAttack();
    }
}
