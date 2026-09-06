using System.Collections;
using UnityEngine;

/// <summary>
/// 盾兵：有盾时举盾对峙；可选 enableShoot，到达 holdRange 后按权重射击。
/// 非巡逻生成时立刻靠近 holdRange，之后玩家离开再延迟追击。
/// 专注模式开启时有盾原地死守。isPatrol 只负责站岗索敌与脱战半径回位。
/// 破盾后行为与近战敌人一致。
/// </summary>
public class ShieldEnemy : MeleeEnemy
{
    [Header("盾兵参数")]
    [Tooltip("举盾停步的水平理想距离")]
    public float holdRange = 1.5f;
    [Tooltip("玩家离开理想距离后，持续多久才再次追击")]
    public float reapproachDelay = 1.5f;
    [Tooltip("再次追击的触发距离；应 ≥ holdRange，略大可避免贴边抖动；≤0 则等同 holdRange")]
    public float reapproachRange = 2.2f;
    [Tooltip("未面向玩家时，延迟多久后转身")]
    public float faceTurnDelay = 0.35f;

    [Header("专注模式")]
    [Tooltip("开启后有盾时原地举盾，不因玩家离开理想距离而追击。与 isPatrol 独立：isPatrol 只负责站岗索敌与脱战半径回位。")]
    public bool enableFocusMode;

    [Header("射击（有盾）")]
    [Tooltip("开启后，到达 holdRange 时按权重在举盾与射击间掷骰。关闭则只举盾。")]
    public bool enableShoot;
    [Tooltip("射程内选择射击的权重")]
    [Min(0f)] public float shootWeight = 0.3f;
    [Tooltip("射程内选择举盾的权重")]
    [Min(0f)] public float holdWeight = 0.7f;
    [Tooltip("举盾持续多久后再掷一次；enableShoot 关闭时举盾无限持续")]
    [Min(0.05f)] public float holdDuration = 1.5f;
    [Tooltip("射完后强制举盾、不可立刻再射的冷却")]
    [Min(0f)] public float shootCooldown = 1f;
    public EnemyProjectile projectilePrefab;
    public Transform firePoint;
    [Tooltip("射击动画 normalizedTime 达到此值时出弹（约第 4 帧）")]
    [Range(0f, 1f)] public float fireNormalizedTime = 0.375f;
    [Tooltip("Animator 中射击状态名，需与 clip 状态一致")]
    public string shootStateName = "enemy_shield_shooting";

    [Header("动画")]
    [Tooltip("破盾后切换到的近战 Animator Controller")]
    public RuntimeAnimatorController meleeAnimatorController;

    EnemyShieldAbsorb shieldAbsorb;
    ShieldDropVisual shieldDropVisual;
    SpriteRenderer shieldVisualRenderer;
    Color shieldVisualOriginalColor = Color.white;
    Coroutine shieldVisualFlashRoutine;
    float leaveIdealTimer;
    bool hasHeldAtIdealRange;
    float shootReadyTime;

    public bool HasShield => shieldAbsorb != null;

    /// <summary>射击动画期间盾牌撤开，正面伤害打到本体。</summary>
    public bool IsShieldWithdrawn { get; private set; }

    public bool IsShootOnCooldown => Time.time < shootReadyTime;

    /// <summary>离开理想距离后触发再追的水平距离。</summary>
    public float GetReapproachRange()
    {
        float range = reapproachRange > 0f ? reapproachRange : holdRange;
        return Mathf.Max(holdRange, range);
    }

    public override void ApplyEncounterFocusMode(bool enabled) => enableFocusMode = enabled;

    /// <summary>
    /// 玩家持续超出再追距离达到 reapproachDelay 后返回 true。
    /// 回到范围内则清零计时。
    /// </summary>
    public bool TickReapproachDelay()
    {
        float dist = GetHorizontalDistanceToPlayer();
        float range = GetSlottedRange(GetReapproachRange());

        if (dist <= range)
        {
            leaveIdealTimer = 0f;
            return false;
        }

        leaveIdealTimer += Time.deltaTime;
        if (leaveIdealTimer < reapproachDelay)
            return false;

        leaveIdealTimer = 0f;
        return true;
    }

    protected override void Awake()
    {
        base.Awake();
        skillState = new ShieldHoldState();
        shotState = new ShieldShootState();
        CacheShield();
        DisableShieldOverlaySprite();
        RecacheSpriteRendererFromChild("Sprite");
    }

    protected override void OnEnable()
    {
        hasHeldAtIdealRange = false;
        leaveIdealTimer = 0f;
        shootReadyTime = 0f;
        SetShieldWithdrawn(false);
        base.OnEnable();
    }

    protected override void OnDisable()
    {
        SetShieldWithdrawn(false);
        StopShieldVisualFlash(hideIfAttached: true);
        base.OnDisable();
    }

    void CacheShield()
    {
        if (firePoint == null)
            firePoint = transform.Find("FirePoint");

        shieldAbsorb = GetComponentInChildren<EnemyShieldAbsorb>(true);
        shieldDropVisual = GetComponentInChildren<ShieldDropVisual>(true);
        shieldVisualRenderer = shieldDropVisual != null
            ? shieldDropVisual.GetComponent<SpriteRenderer>()
            : null;
        if (shieldVisualRenderer != null)
            shieldVisualOriginalColor = shieldVisualRenderer.color;

        // 物体保持激活，Animator 才能绑定 enemy_shield_hurt 的 ShieldVisual 精灵轨。
        PrepareShieldVisualIdle();
    }

    void PrepareShieldVisualIdle()
    {
        if (shieldDropVisual == null || shieldDropVisual.HasDropped)
            return;

        shieldDropVisual.gameObject.SetActive(true);
        if (shieldVisualRenderer != null)
            shieldVisualRenderer.enabled = false;
    }

    void DisableShieldOverlaySprite()
    {
        if (shieldAbsorb == null)
            return;

        var overlay = shieldAbsorb.GetComponent<SpriteRenderer>();
        if (overlay != null)
            overlay.enabled = false;
    }

    /// <summary>护盾销毁时由 EnemyShieldAbsorb 调用。</summary>
    public void NotifyShieldBroken()
    {
        shieldAbsorb = null;
        SetShieldWithdrawn(false);
        leaveIdealTimer = 0f;
        if (physicsCheck != null)
            physicsCheck.RefreshLedgeColliders();
        StopShieldVisualFlash(hideIfAttached: false);
        shieldDropVisual?.Drop();
        SwitchToMeleeAnimator();
        if (!isDead)
            EvaluateCycle();
    }

    void SwitchToMeleeAnimator()
    {
        if (anim == null || meleeAnimatorController == null)
            return;

        if (anim.runtimeAnimatorController == meleeAnimatorController)
            return;

        anim.runtimeAnimatorController = meleeAnimatorController;
        anim.Rebind();
        anim.Update(0f);
        RecacheAnimBoolNames();
    }

    /// <summary>
    /// 盾牌受击：播 shieldHurt（Shurt），不进入 isHurt 硬直、不打断举盾 AI。
    /// 本体受击仍走 OnTakeDamage 的 hurt。
    /// </summary>
    public void PlayShieldHitAnim()
    {
        if (isDead || anim == null || IsShieldWithdrawn)
            return;

        anim.SetTrigger("shieldHurt");
        BeginShieldVisualFeedback();
    }

    /// <summary>开枪立刻打断盾受击动画与盾牌闪红，避免 ShieldHit 挡住射击。</summary>
    public void InterruptShieldHitForShoot()
    {
        if (anim == null)
            return;

        anim.ResetTrigger("shieldHurt");
        if (!string.IsNullOrEmpty(shootStateName))
        {
            anim.Play(shootStateName, 0, 0f);
            anim.Update(0f);
        }

        StopShieldVisualFlash(hideIfAttached: true);
    }

    public override void OnTakeDamage(Transform attackTrans)
    {
        if (HasShield && !IsShieldWithdrawn)
            BeginShieldVisualFeedback();

        base.OnTakeDamage(attackTrans);
    }

    void BeginShieldVisualFeedback()
    {
        if (isDead || !CanUseShieldVisual())
            return;

        ShowShieldVisualRenderer();

        if (shieldVisualFlashRoutine != null)
            StopCoroutine(shieldVisualFlashRoutine);

        RestoreShieldVisualColor();
        shieldVisualFlashRoutine = StartCoroutine(FlashShieldVisual());
    }

    IEnumerator FlashShieldVisual()
    {
        float duration = Mathf.Max(0.05f, hurtDuration);
        float elapsed = 0f;
        float flashTimer = 0f;
        bool flashOn = true;

        if (shieldVisualRenderer != null)
            shieldVisualRenderer.color = hurtFlashColor;

        while (elapsed < duration)
        {
            if (isDead || !CanUseShieldVisual())
                break;

            float dt = Time.deltaTime;
            elapsed += dt;
            flashTimer += dt;

            if (shieldVisualRenderer != null && flashTimer >= hurtFlashInterval)
            {
                flashTimer = 0f;
                flashOn = !flashOn;
                shieldVisualRenderer.color = flashOn ? hurtFlashColor : shieldVisualOriginalColor;
            }

            yield return null;
        }

        shieldVisualFlashRoutine = null;

        if (isDead)
        {
            StopShieldVisualFlash(hideIfAttached: true);
            yield break;
        }

        RestoreShieldVisualColor();
        HideShieldVisualRenderer();
    }

    void StopShieldVisualFlash(bool hideIfAttached)
    {
        if (shieldVisualFlashRoutine != null)
        {
            StopCoroutine(shieldVisualFlashRoutine);
            shieldVisualFlashRoutine = null;
        }

        RestoreShieldVisualColor();

        if (hideIfAttached)
            HideShieldVisualRenderer();
        else
            ShowShieldVisualRenderer();
    }

    void ShowShieldVisualRenderer()
    {
        if (!CanUseShieldVisual() || shieldVisualRenderer == null)
            return;

        shieldDropVisual.gameObject.SetActive(true);
        shieldVisualRenderer.enabled = true;
    }

    void HideShieldVisualRenderer()
    {
        if (!CanUseShieldVisual() || shieldVisualRenderer == null)
            return;

        shieldVisualRenderer.enabled = false;
    }

    void RestoreShieldVisualColor()
    {
        if (shieldVisualRenderer != null)
            shieldVisualRenderer.color = shieldVisualOriginalColor;
    }

    bool CanUseShieldVisual()
    {
        return shieldDropVisual != null
            && !shieldDropVisual.HasDropped
            && shieldDropVisual.transform.parent == transform;
    }

    const float ShieldedMoveSpeedScale = 0.5f;

    public override float GetApproachStopRange()
    {
        return HasShield ? holdRange : base.GetApproachStopRange();
    }

    public override float GetMoveSpeedScale()
    {
        if (isApproachingSpawnTarget || isReturning)
            return 1f;
        return HasShield ? ShieldedMoveSpeedScale : 1f;
    }

    /// <summary>
    /// 有盾：专注模式则原地举盾/射击。非巡逻首次接敌立刻靠近；到达 holdRange 后
    /// 举盾，enableShoot 时按权重射击。玩家再离开则等待延迟后追击。
    /// isPatrol 只做站岗/脱战闸门。无盾：走近战 EvaluateCycle。
    /// </summary>
    public override void EvaluateCycle()
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

        if (!HasShield)
        {
            base.EvaluateCycle();
            return;
        }

        if (CurrentState == shotState)
            return;

        if (enableFocusMode)
        {
            TryEnterHoldOrShoot();
            return;
        }

        if (GetHorizontalDistanceToPlayer() <= GetSlottedRange(holdRange))
        {
            hasHeldAtIdealRange = true;
            TryEnterHoldOrShoot();
            return;
        }

        if (CurrentState == getCloseState)
            return;

        if (!hasHeldAtIdealRange && !isPatrol)
        {
            SwitchToGetCloseIfNeeded();
            return;
        }

        TryEnterHoldOrShoot();
    }

    void TryEnterHoldOrShoot()
    {
        if (enableShoot && !IsShootOnCooldown)
        {
            RollShieldAction();
            return;
        }

        SwitchToSkillIfNeeded();
    }

    void RollShieldAction()
    {
        float shoot = Mathf.Max(0f, shootWeight);
        float hold = Mathf.Max(0f, holdWeight);
        float total = shoot + hold;
        if (total <= 0f)
        {
            SwitchToSkillIfNeeded();
            return;
        }

        if (Random.value * total < shoot)
        {
            SwitchState(NPCState.Shot);
            return;
        }

        if (CurrentState == skillState)
            return;

        SwitchToSkillIfNeeded();
    }

    public void BeginShootCooldown()
    {
        shootReadyTime = Time.time + Mathf.Max(0f, shootCooldown);
    }

    public void SetShieldWithdrawn(bool withdrawn)
    {
        IsShieldWithdrawn = withdrawn;
    }

    public void FireProjectile()
    {
        if (projectilePrefab == null || player == null)
            return;

        Vector3 spawnPos = firePoint != null ? firePoint.position : transform.position;
        float dir = Mathf.Sign(player.position.x - transform.position.x);
        if (dir == 0f)
            dir = faceDir.x;

        var projectile = Instantiate(projectilePrefab, spawnPos, Quaternion.identity);
        EnemySceneCleanup.PlaceInSourceScene(projectile.gameObject, this);
        projectile.Init(new Vector2(dir, 0f));
        FacePlayer();
    }

    void SwitchToSkillIfNeeded()
    {
        if (CurrentState == skillState)
            return;

        SwitchState(NPCState.Skill);
    }

    void SwitchToGetCloseIfNeeded()
    {
        if (CurrentState == getCloseState)
            return;

        SwitchState(NPCState.GetClose);
    }

    void OnDrawGizmosSelected()
    {
        DrawPatrolGizmos();

        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(
            transform.position + Vector3.left * holdRange,
            transform.position + Vector3.right * holdRange);

        float reapproach = GetReapproachRange();
        if (reapproach > holdRange + 0.01f)
        {
            Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.8f);
            Gizmos.DrawLine(
                transform.position + Vector3.left * reapproach,
                transform.position + Vector3.right * reapproach);
        }
    }
}
