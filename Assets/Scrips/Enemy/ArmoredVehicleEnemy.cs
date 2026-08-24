using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 装甲车精英：无射程限制，权重循环 Shot / Missile / Ram / Move。
/// 车体永不转向；受击只闪红，无硬直、无击退。
/// 机枪仅在瞄准阶段转炮塔，连射沿锁定方向；冲撞按玩家前后前进或倒车。
/// </summary>
public class ArmoredVehicleEnemy : Enemy
{
    const float ProbabilityStep = 0.1f;

    protected override bool SpriteFacesRight => false;
    protected override bool CanChangeFacing => false;
    protected override bool UseHurtStun => false;

    [Header("朝向")]
    [Tooltip("车头世界 X 方向：+1 朝右，-1 朝左。占位 Prefab 机枪在左侧，默认 -1")]
    public float forwardSign = -1f;

    [Header("行为权重（初始）")]
    [Min(0f)] public float shotWeight = 0.35f;
    [Min(0f)] public float missileWeight = 0.25f;
    [Min(0f)] public float ramWeight = 0.2f;
    [Min(0f)] public float moveWeight = 0.2f;

    [Header("机枪")]
    public Transform gunBase;
    public Transform firePoint;
    public EnemyProjectile projectilePrefab;
    [Tooltip("瞄准阶段时长（秒），期间炮塔转向玩家")]
    public float mgAimDuration = 1.2f;
    [Tooltip("每次瞄准后连续发射的子弹数")]
    public int mgBurstCount = 5;
    public float mgFireInterval = 0.18f;
    [Tooltip("一次机枪行动内瞄准→射击循环次数下限")]
    public int mgCycleMin = 1;
    [Tooltip("一次机枪行动内瞄准→射击循环次数上限")]
    public int mgCycleMax = 3;

    [Header("导弹")]
    public EnemyHomingMissile missilePrefab;
    public Transform missileFirePoint1;
    public Transform missileFirePoint2;
    public int missileCountMin = 2;
    public int missileCountMax = 4;
    public float missileFireInterval = 0.25f;
    [Tooltip("导弹先向上飞行的时长（秒）")]
    public float missileAscentDuration = 0.45f;
    [Tooltip("开仓动画名；无 Animator 时使用 missileBayDuration")]
    public string missileBayStateName = "Missile";
    public float missileBayDuration = 0.8f;

    [Header("冲撞")]
    public float ramSpeed = 8f;
    public float ramRecoveryDuration = 1.2f;
    public float ramMaxDuration = 2.5f;
    public string ramWindupStateName = "RamWindup";
    public float ramWindupDuration = 0.6f;

    [Header("移动")]
    public float moveSpeed = 1.2f;
    public float actionDuration = 2.5f;
    [Tooltip("每个行动结束后的 Idle 后摇（秒），结束后才进入下一动作")]
    public float actionRecoveryDuration = 1f;

    [Header("车头/车尾碰撞")]
    public GameObject bumperLeft;
    public GameObject bumperRight;
    [Tooltip("冲撞/移动时对玩家造成的伤害（写入 bumper Attack）")]
    public int bumperDamage = 20;
    public float bumperKnockbackForce = 8f;

    [HideInInspector] public Dictionary<EnemyAction, float> actionProbabilities = new();
    [HideInInspector] public EnemyAction? lastAction;
    [HideInInspector] public Vector2 lockedFireDir = Vector2.left;

    Attack bumperLeftAttack;
    Attack bumperRightAttack;
    Quaternion aimStartRotation;

    protected override void Awake()
    {
        base.Awake();
        patroState = new ArmoredVehicleIdleGuardState();
        returnState = new ArmoredVehicleReturnHomeState();
        shotState = new ArmoredVehicleGunState();
        jumpState = new ArmoredVehicleMissileState();
        skillState = new ArmoredVehicleRamState();
        moveState = new ArmoredVehicleMoveState();
        reloadState = new ArmoredVehicleActionRecoveryState();

        if (normalSpeed <= 0f)
            normalSpeed = moveSpeed > 0f ? moveSpeed : 1.2f;
        if (chaseSpeed <= 0f)
            chaseSpeed = ramSpeed > 0f ? ramSpeed : 8f;

        if (Mathf.Approximately(forwardSign, 0f))
            forwardSign = -1f;
        else
            forwardSign = Mathf.Sign(forwardSign);

        CacheBumpers();
        ConfigureBumperAttacks();
        SetBumpersActive(false);
    }

    protected override void StartCombatCycle() => EvaluateCycle();

    void Start()
    {
        ConfigurePhysicsCheck();
    }

    void CacheBumpers()
    {
        if (bumperLeft != null)
            bumperLeftAttack = bumperLeft.GetComponent<Attack>();
        if (bumperRight != null)
            bumperRightAttack = bumperRight.GetComponent<Attack>();
    }

    void ConfigureBumperAttacks()
    {
        ConfigureBumper(bumperLeftAttack, -1f);
        ConfigureBumper(bumperRightAttack, 1f);
    }

    void ConfigureBumper(Attack attack, float outwardX)
    {
        if (attack == null)
            return;

        attack.damage = bumperDamage;
        attack.attackType = AttackType.Melee;
        attack.requireTag = "Player";
        attack.enableKnockback = true;
        if (attack.knockbackForce <= 0f)
            attack.knockbackForce = bumperKnockbackForce;

        float z = outwardX < 0f ? 180f : 0f;
        attack.transform.localRotation = Quaternion.Euler(0f, 0f, z);
    }

    void ConfigurePhysicsCheck()
    {
        if (physicsCheck == null)
            return;

        var coll = GetComponent<CapsuleCollider2D>();
        if (coll == null)
            return;

        float bottom = coll.offset.y - coll.size.y * 0.5f;
        physicsCheck.bottomOffset = new Vector2(0f, bottom + 0.1f);
        if (physicsCheck.checkRaduis <= 0f)
            physicsCheck.checkRaduis = 0.2f;
    }

    protected override void OnEnable()
    {
        RegisterSeparation();
        ResetActionProbabilities();
        lastAction = null;
        CacheHome();
        isReturning = false;
        SetBumpersActive(false);

        if (isPatrol)
        {
            isAggro = false;
            SwitchState(NPCState.Patrol);
        }
        else
        {
            isAggro = true;
            EvaluateCycle();
        }
    }

    protected override void Update()
    {
        if (isPatrol && isAggro && !isDead && !isReturning && !IsPlayerInsideHomeBounds())
            BeginReturnHome();

        base.Update();
    }

    protected override void OnPatrolAggroFromDamage()
    {
        if (isApproachingSpawnTarget)
            return;

        if (isReturning)
            isReturning = false;

        EnterPatrolCombat();
        EvaluateCycle();
    }

    protected override bool ShouldRunTimeCounter() => false;

    protected override bool ShouldAutoMove() => false;

    void ResetActionProbabilities()
    {
        actionProbabilities.Clear();

        var weights = new List<(EnemyAction action, float weight)>
        {
            (EnemyAction.Shot, shotWeight),
            (EnemyAction.Missile, missileWeight),
            (EnemyAction.Ram, ramWeight),
            (EnemyAction.Move, moveWeight)
        };

        float total = 0f;
        foreach (var (_, weight) in weights)
            total += Mathf.Max(0f, weight);

        if (total <= 0f)
        {
            actionProbabilities[EnemyAction.Shot] = 0.25f;
            actionProbabilities[EnemyAction.Missile] = 0.25f;
            actionProbabilities[EnemyAction.Ram] = 0.25f;
            actionProbabilities[EnemyAction.Move] = 0.25f;
            return;
        }

        foreach (var (action, weight) in weights)
            actionProbabilities[action] = Mathf.Max(0f, weight) / total;
    }

    public void EvaluateCycle()
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

        RollAndEnterAction();
    }

    /// <summary>
    /// 行动结束后进入 Idle 后摇，结束后才掷骰下一动作。
    /// </summary>
    public void FinishActionAndRecover()
    {
        if (isDead)
            return;

        if (actionRecoveryDuration <= 0f)
        {
            EvaluateCycle();
            return;
        }

        SwitchState(NPCState.Reload);
    }

    public void EnterIdlePose()
    {
        SetBumpersActive(false);
        StopHorizontalMotion();
        currentSpeed = 0f;
        SetAnimBool("walk", false);
        SetAnimBool("shoot", false);
        SetAnimBool("missile", false);
        SetAnimBool("ram", false);
        SetAnimBool("ramWindup", false);
        SetAnimBool("reload", false);
    }

    void RollAndEnterAction()
    {
        if (actionProbabilities == null || actionProbabilities.Count == 0)
            ResetActionProbabilities();

        float roll = Random.value;
        float cumulative = 0f;
        EnemyAction selected = EnemyAction.Move;

        foreach (var pair in actionProbabilities)
        {
            selected = pair.Key;
            cumulative += pair.Value;
            if (roll <= cumulative)
                break;
        }

        SwitchState(ActionToState(selected));
    }

    static NPCState ActionToState(EnemyAction action) => action switch
    {
        EnemyAction.Shot => NPCState.Shot,
        EnemyAction.Missile => NPCState.Jump,
        EnemyAction.Ram => NPCState.Ram,
        _ => NPCState.Move
    };

    public void OnActionEntered(EnemyAction action)
    {
        if (lastAction.HasValue && lastAction.Value == action)
        {
            if (actionProbabilities.TryGetValue(action, out float current))
            {
                actionProbabilities[action] = Mathf.Max(0f, current - ProbabilityStep);
                NormalizeProbabilities();
            }
        }
        else
            ResetActionProbabilities();

        lastAction = action;
    }

    void NormalizeProbabilities()
    {
        float total = 0f;
        foreach (var value in actionProbabilities.Values)
            total += value;

        if (total <= 0f)
        {
            ResetActionProbabilities();
            return;
        }

        var keys = new List<EnemyAction>(actionProbabilities.Keys);
        foreach (var key in keys)
            actionProbabilities[key] /= total;
    }

    public int RollGunCycleCount()
    {
        int min = Mathf.Max(1, mgCycleMin);
        int max = Mathf.Max(min, mgCycleMax);
        return Random.Range(min, max + 1);
    }

    public int RollMissileCount()
    {
        int min = Mathf.Max(1, missileCountMin);
        int max = Mathf.Max(min, missileCountMax);
        return Random.Range(min, max + 1);
    }

    public float GetForwardSign()
    {
        return Mathf.Approximately(forwardSign, 0f) ? -1f : Mathf.Sign(forwardSign);
    }

    /// <summary>冲撞方向：始终朝玩家所在水平侧，不转身。</summary>
    public float GetRamDashSign()
    {
        EnsurePlayerReference();
        if (player == null)
            return GetForwardSign();

        float dx = player.position.x - transform.position.x;
        if (Mathf.Approximately(dx, 0f))
            return GetForwardSign();

        return Mathf.Sign(dx);
    }

    public bool IsWallInDirection(float moveDir)
    {
        if (physicsCheck == null || Mathf.Approximately(moveDir, 0f))
            return false;

        return (moveDir < 0f && physicsCheck.touchLeftWall)
            || (moveDir > 0f && physicsCheck.touchRightWall);
    }

    public void StopHorizontalMotion()
    {
        if (Rb == null)
            return;

        Rb.linearVelocity = new Vector2(0f, Rb.linearVelocity.y);
    }

    public override void MoveTowardSpawnTarget()
    {
        float dx = spawnTargetPosition.x - transform.position.x;
        if (Mathf.Abs(dx) <= returnArriveDistance)
            return;

        float dir = Mathf.Sign(dx);
        if (dir == 0f)
            return;

        currentSpeed = GetSpawnApproachSpeed();
        if (IsWallInDirection(dir) || IsLedgeBlocking(dir))
        {
            StopHorizontalMotion();
            return;
        }

        MoveHorizontal(dir);
    }

    public bool HasAnimatorController =>
        anim != null && anim.runtimeAnimatorController != null;

    public void BeginGunAim()
    {
        if (gunBase != null)
            aimStartRotation = gunBase.rotation;
    }

    public void AimGunAtPlayer(float t)
    {
        if (gunBase == null)
            return;

        Quaternion target = ComputeAimRotation();
        float clamped = Mathf.Clamp01(t);
        gunBase.rotation = Quaternion.Slerp(aimStartRotation, target, clamped);
    }

    public void SnapGunToPlayer()
    {
        if (gunBase == null)
            return;

        gunBase.rotation = ComputeAimRotation();
    }

    public void LockFireDirection()
    {
        Vector2 dir = GetCurrentBarrelDirection();
        if (dir.sqrMagnitude < 0.0001f)
        {
            EnsurePlayerReference();
            if (player != null && firePoint != null)
                dir = (Vector2)player.position - (Vector2)firePoint.position;
            else
                dir = new Vector2(GetForwardSign(), 0f);
        }

        if (dir.sqrMagnitude < 0.0001f)
            dir = Vector2.left;

        lockedFireDir = dir.normalized;
    }

    Vector2 GetCurrentBarrelDirection()
    {
        if (gunBase == null)
            return Vector2.zero;

        if (firePoint != null)
        {
            Vector2 delta = (Vector2)firePoint.position - (Vector2)gunBase.position;
            if (delta.sqrMagnitude > 0.0001f)
                return delta;
        }

        return gunBase.right;
    }

    Quaternion ComputeAimRotation()
    {
        if (gunBase == null)
            return Quaternion.identity;

        EnsurePlayerReference();
        Vector2 origin = gunBase.position;
        Vector2 targetPos = player != null ? (Vector2)player.position : origin + Vector2.left;
        Vector2 toTarget = targetPos - origin;
        if (toTarget.sqrMagnitude < 0.0001f)
            return gunBase.rotation;

        Vector2 localBarrel = GetLocalBarrelOffset();
        float barrelAngle = Mathf.Atan2(localBarrel.y, localBarrel.x) * Mathf.Rad2Deg;
        float targetAngle = Mathf.Atan2(toTarget.y, toTarget.x) * Mathf.Rad2Deg;
        return Quaternion.Euler(0f, 0f, targetAngle - barrelAngle);
    }

    Vector2 GetLocalBarrelOffset()
    {
        if (gunBase == null || firePoint == null)
            return Vector2.left;

        if (firePoint.parent == gunBase)
        {
            Vector3 local = firePoint.localPosition;
            if (local.sqrMagnitude > 0.0001f)
                return local;
        }

        Vector3 world = gunBase.InverseTransformPoint(firePoint.position);
        if (world.sqrMagnitude > 0.0001f)
            return world;

        return Vector2.left;
    }

    public void FireLockedProjectile()
    {
        if (projectilePrefab == null)
            return;

        Vector3 spawnPos = firePoint != null ? firePoint.position : transform.position;
        Vector2 dir = lockedFireDir.sqrMagnitude > 0.0001f
            ? lockedFireDir.normalized
            : GetCurrentBarrelDirection().normalized;

        if (dir.sqrMagnitude < 0.0001f)
            dir = new Vector2(GetForwardSign(), 0f);

        var projectile = Instantiate(projectilePrefab, spawnPos, Quaternion.identity);
        EnemySceneCleanup.PlaceInSourceScene(projectile.gameObject, this);
        projectile.Init(dir);

        float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        projectile.transform.rotation = Quaternion.Euler(0f, 0f, angle);

        IgnoreSelfCollision(projectile.GetComponent<Collider2D>());
    }

    public void FireHomingMissile(int shotIndex)
    {
        if (missilePrefab == null)
            return;

        Transform point = (shotIndex % 2 == 0) ? missileFirePoint1 : missileFirePoint2;
        if (point == null)
            point = (missileFirePoint1 != null) ? missileFirePoint1 : missileFirePoint2;

        Vector3 spawnPos = point != null ? point.position : transform.position + Vector3.up;
        var missile = Instantiate(missilePrefab, spawnPos, Quaternion.identity);
        EnemySceneCleanup.PlaceInSourceScene(missile.gameObject, this);

        EnsurePlayerReference();
        missile.Init(GetComponent<Collider2D>(), player, missileAscentDuration);
    }

    void IgnoreSelfCollision(Collider2D other)
    {
        if (other == null)
            return;

        var selfCols = GetComponentsInChildren<Collider2D>(true);
        for (int i = 0; i < selfCols.Length; i++)
        {
            if (selfCols[i] != null && selfCols[i] != other)
                Physics2D.IgnoreCollision(other, selfCols[i], true);
        }
    }

    public void SetBumpersActive(bool active)
    {
        if (bumperLeft != null && bumperLeft.activeSelf != active)
            bumperLeft.SetActive(active);
        if (bumperRight != null && bumperRight.activeSelf != active)
            bumperRight.SetActive(active);
    }

    public void SubscribeBumperHits(System.Action<Character, int> handler)
    {
        if (bumperLeftAttack != null)
            bumperLeftAttack.CharacterDamaged += handler;
        if (bumperRightAttack != null)
            bumperRightAttack.CharacterDamaged += handler;
    }

    public void UnsubscribeBumperHits(System.Action<Character, int> handler)
    {
        if (bumperLeftAttack != null)
            bumperLeftAttack.CharacterDamaged -= handler;
        if (bumperRightAttack != null)
            bumperRightAttack.CharacterDamaged -= handler;
    }
}
