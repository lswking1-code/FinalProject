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
    bool summonProfileApplied;
    bool hasStarted;
    bool subscribedDie;

    protected override void Awake()
    {
        base.Awake();
        shotState = new HelicopterSpawnState();
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
        if (hasStarted && !isPatrol && !isApproachingSpawnTarget && !isDead)
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
        if (isDead || isApproachingSpawnTarget || isPatrol)
            return;
        if (CurrentState != null)
            return;

        isAggro = true;
        EvaluateCycle();
    }

    protected override bool ShouldStartCombatOnEnable() => false;

    public override void ApplyEncounterFocusMode(bool enabled) => enableFocusMode = enabled;

    public void ApplySummonProfile(HelicopterSummonProfile profile)
    {
        summonProfileApplied = true;
        summonGenerator?.ApplySummonProfile(profile);
    }

    public override void EvaluateCycle()
    {
        if (enableFocusMode)
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

                if (!IsPlayerInsideHomeBounds())
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

    public bool IsSummonFinished => summonGenerator == null || !summonGenerator.IsSummoning;

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

    void OnHelicopterDied()
    {
        StopSummonAttack();
    }
}
