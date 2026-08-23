using FMODUnity;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Tilemaps;

[RequireComponent(typeof(PlayerMovement))]
[RequireComponent(typeof(Character))]
[RequireComponent(typeof(PlayerAnim))]
[RequireComponent(typeof(SpecialMagazine))]
[RequireComponent(typeof(DataDefination))]
public class PlayerAbilities : MonoBehaviour, ISaveable
{
    enum RobotAbilityPhase { Idle, Pressing, Aiming }

    [Header("Robot 生成")]
    [SerializeField] Transform robotGeneratePoint;
    [Tooltip("短按跟随模式的回归锚点；留空则由机器人按玩家面向后方偏移计算")]
    [SerializeField] Transform robotFollowPoint;
    [SerializeField] GameObject robotPrefab;
    [SerializeField] GameObject positionPreview;

    [Header("输入与放置")]
    [SerializeField] float longPressThreshold = 0.5f;
    [SerializeField] float previewMoveSpeed = 3f;
    [SerializeField] float maxPreviewDistance = 5f;
    [Tooltip("生成点落在该层碰撞体或 Ground Tilemap 格子内则取消本次生成。留空则使用 Ground")]
    [SerializeField] LayerMask spawnBlockMask;
    [Tooltip("描边 CompositeCollider 测不到内部，额外用该半径做边缘重叠检测")]
    [SerializeField] float spawnBlockProbeRadius = 0.2f;

    [Header("AbilityPower")]
    [SerializeField] float robotDrainRate = 5f;
    [SerializeField] float minAbilityPowerToSpawn = 1f;

    [Header("特殊弹装填")]
    [Tooltip("下标对应 WeaponId：0 忽略；1/2/3 为每次装填消耗的 BulletS/M/L 数量")]
    [SerializeField] int[] reloadAmmoCosts = { 0, 10, 5, 3 };
    [Tooltip("下标对应 WeaponId：0 忽略；1/2/3 为每次装填装入的特殊弹数量")]
    [SerializeField] int[] reloadLoadCounts = { 0, 1, 2, 3 };

    [Header("事件")]
    [SerializeField] VoidEventSO newGameEvent;

    [Header("音效")]
    [SerializeField] EventReference loadEvent;

    [Header("RobotCore 演出")]
    [SerializeField] GameObject robotCorePrefab;
    [Tooltip("收回时 Core 飞回玩家的速度（单位/秒）")]
    [SerializeField] float robotCoreFlySpeed = 12f;
    [Tooltip("判定已到达目标点的距离阈值")]
    [SerializeField] float robotCoreArriveThreshold = 0.2f;
    [Tooltip("飞回目标相对玩家 Transform 的偏移。玩家枢轴在脚底时，把 Y 调到身体中心")]
    [SerializeField] Vector3 robotCoreReturnOffset = new Vector3(0f, 1f, 0f);

    RobotAbilityPhase phase = RobotAbilityPhase.Idle;
    InputSystem_Actions actions;
    PlayerMovement playerMovement;
    PlayerAnim playerAnim;
    Character character;
    PlayerWeaponController weaponController;
    SpecialMagazine specialMagazine;
    GameObject activeRobot;
    AllyRobot activeRobotController;
    RobotCoreVisual returningCore;

    public bool HasRobot => HasActiveRobot();
    public float PullCooldownNormalized =>
        activeRobotController != null
            ? activeRobotController.PullCooldownNormalized
            : 0f;

    float pressTime;
    Vector3 defaultLocalPos;
    Vector3 aimOriginWorldPos;
    float aimFacing;
    float currentAimOffset;
    Transform generatePointParent;
    bool dispatchStartedThisPress;

    void Awake()
    {
        actions = new InputSystem_Actions();
        playerMovement = GetComponent<PlayerMovement>();
        playerAnim = GetComponent<PlayerAnim>();
        character = GetComponent<Character>();
        weaponController = GetComponent<PlayerWeaponController>();
        specialMagazine = GetComponent<SpecialMagazine>();

        if (newGameEvent != null)
            newGameEvent.OnEventRaised += ResetForNewGame;
    }

    void OnEnable()
    {
        actions.Player.Enable();
        ((ISaveable)this).RegisterSaveData();
    }

    void OnDisable()
    {
        if (phase == RobotAbilityPhase.Aiming)
            ExitAimingMode();
        else if (phase == RobotAbilityPhase.Pressing)
            phase = RobotAbilityPhase.Idle;

        EndPlayerDispatch();
        DestroyReturningCoreImmediate();
        actions.Player.Disable();
        ((ISaveable)this).UnregisterSaveData();
    }

    void OnDestroy()
    {
        if (newGameEvent != null)
            newGameEvent.OnEventRaised -= ResetForNewGame;
        DestroyReturningCoreImmediate();
        actions?.Dispose();
    }

    public void ResetForNewGame()
    {
        if (phase == RobotAbilityPhase.Aiming)
            ExitAimingMode();
        else
        {
            EndPlayerDispatch();
            phase = RobotAbilityPhase.Idle;
        }

        DestroyReturningCoreImmediate();
        DestroyActiveRobot();
        specialMagazine?.Clear();
    }

    void Update()
    {
        if (robotGeneratePoint == null)
            return;

        UpdateRobotDrain();
        UpdateAbility1();
        UpdateRobotManualMove();

        switch (phase)
        {
            case RobotAbilityPhase.Idle:
                if (actions.Player.Ability2.WasPressedThisFrame())
                {
                    if (TryReleasePullOnAbility2())
                        break;
                    BeginPress();
                }
                break;

            case RobotAbilityPhase.Pressing:
                if (actions.Player.Ability2.IsPressed()
                    && Time.time - pressTime >= longPressThreshold)
                {
                    if (HasActiveRobot())
                    {
                        BeginRecall(playPlayerAnim: true);
                        EndPlayerDispatch();
                        phase = RobotAbilityPhase.Idle;
                    }
                    else
                    {
                        EnterAimingMode();
                    }
                }

                if (actions.Player.Ability2.WasReleasedThisFrame())
                {
                    if (Time.time - pressTime < longPressThreshold)
                    {
                        if (!HasActiveRobot() && CanSpawnRobot())
                        {
                            if (TrySpawnRobot(robotGeneratePoint.position, RobotDeployMode.Follow))
                            {
                                // 短按：intro 播完（或已定格）后结束召唤动画
                                if (dispatchStartedThisPress)
                                    playerAnim.SetDispatchAutoEnd(true);
                                dispatchStartedThisPress = false;
                            }
                            else
                            {
                                EndPlayerDispatch();
                            }
                        }
                        else if (HasActiveRobot())
                        {
                            activeRobotController?.TryStartPull();
                            EndPlayerDispatch();
                        }
                        else
                        {
                            EndPlayerDispatch();
                        }
                    }
                    else
                    {
                        EndPlayerDispatch();
                    }

                    phase = RobotAbilityPhase.Idle;
                }
                break;

            case RobotAbilityPhase.Aiming:
                UpdateAimingDrift();

                if (actions.Player.Ability2.WasReleasedThisFrame())
                {
                    if (CanSpawnRobot())
                        TrySpawnRobot(robotGeneratePoint.position, RobotDeployMode.Stationed);

                    ExitAimingMode();
                }
                break;
        }
    }

    /// <summary>
    /// Ability1：特殊弹装填（不依赖机器人）。
    /// </summary>
    void UpdateAbility1()
    {
        if (playerMovement.IsActionLocked || phase != RobotAbilityPhase.Idle)
            return;

        if (playerAnim.IsPlayingLoadBullet
            || playerAnim.IsPlayingMachinistComboShoot
            || playerAnim.IsPlayingMachineShoot
            || playerAnim.IsPlayingMachinistChargeShoot
            || playerAnim.IsDispatching)
            return;

        if (!actions.Player.Ability1.WasPressedThisFrame())
            return;

        if (!TryConvertAmmoToSpecial())
            return;

        if (playerAnim.TryPlayLoadBulletAnim())
            FmodAudio.Play(loadEvent);
    }

    void UpdateRobotManualMove()
    {
        if (!HasActiveRobot() || activeRobotController == null)
            return;

        activeRobotController.SetManualMoveInput(actions.Player.RobotMove.ReadValue<Vector2>());
    }

    bool HasActiveRobot()
    {
        if (activeRobot)
            return true;

        // 机器人自销毁后 Unity 把引用当成 null，但 C# 包装还在，必须清掉暂停回复。
        if ((object)activeRobot != null || (object)activeRobotController != null)
            OnRobotRemoved();

        return false;
    }

    bool HasReturningCore()
    {
        if (returningCore == null)
            return false;

        if (!returningCore)
        {
            returningCore = null;
            return false;
        }

        return true;
    }

    bool IsRecallInProgress() =>
        HasReturningCore()
        || (activeRobotController != null && activeRobotController.IsRecalling);

    bool CanSpawnRobot()
    {
        return !HasActiveRobot()
            && !HasReturningCore()
            && character != null
            && character.AbilityPower >= minAbilityPowerToSpawn;
    }

    void UpdateRobotDrain()
    {
        if (!HasActiveRobot())
            return;

        character.DrainAbilityPower(robotDrainRate * Time.deltaTime);

        if (character.AbilityPower <= 0f
            && (activeRobotController == null || !activeRobotController.IsRecalling))
            BeginRecall(playPlayerAnim: false);
    }

    void OnRobotSpawned(GameObject robot)
    {
        activeRobot = robot;
        activeRobotController = robot.GetComponent<AllyRobot>();
        character.pauseAbilityPowerRecover = true;
    }

    void OnRobotRemoved()
    {
        activeRobot = null;
        activeRobotController = null;
        if (character != null)
            character.pauseAbilityPowerRecover = false;
    }

    void DestroyActiveRobot()
    {
        if (!HasActiveRobot())
            return;

        Destroy(activeRobot);
        OnRobotRemoved();
    }

    /// <summary>收回当前机器人（播 Recall + Core；读档/新游戏请用立刻销毁）。</summary>
    public void RecallRobot() => BeginRecall(playPlayerAnim: false);

    /// <summary>切场景时立刻清掉机器人和飞回中的 Core，不播收回动画。</summary>
    public void DismissRobotImmediate()
    {
        DestroyReturningCoreImmediate();
        DestroyActiveRobot();
    }

    public bool OwnsRobot(AllyRobot robot) =>
        robot != null && activeRobotController == robot;

    /// <summary>
    /// 若当前有机器人且不在任一激活遭遇区的 EncounterBounds 内，则收回。
    /// </summary>
    public void RecallRobotIfOutsideActiveEncounter()
    {
        if (!HasActiveRobot())
            return;

        Vector2 robotPos = activeRobot.transform.position;
        if (EncounterZone.IsPointInsideAnyActiveEncounter(robotPos))
            return;

        RecallRobot();
    }

    bool TryReleasePullOnAbility2()
    {
        if (activeRobotController == null || !activeRobotController.IsPlayerHooked)
            return false;

        return activeRobotController.TryReleasePulledPlayer();
    }

    void BeginPress()
    {
        if (IsRecallInProgress())
            return;

        // 钩锁收回会锁操作；仍允许短按进入 Pressing，以便松手走放下。
        if (playerMovement.IsActionLocked
            && (activeRobotController == null || !activeRobotController.IsPulling))
            return;

        phase = RobotAbilityPhase.Pressing;
        pressTime = Time.time;
        defaultLocalPos = robotGeneratePoint.localPosition;
        dispatchStartedThisPress = false;

        if (!HasActiveRobot() && CanSpawnRobot() && playerAnim.BeginDispatch())
            dispatchStartedThisPress = true;
    }

    void EnterAimingMode()
    {
        phase = RobotAbilityPhase.Aiming;
        aimOriginWorldPos = robotGeneratePoint.position;
        aimFacing = playerMovement.FaceDirection;
        currentAimOffset = 0f;
        generatePointParent = robotGeneratePoint.parent;
        robotGeneratePoint.SetParent(null, true);

        if (positionPreview != null)
            positionPreview.SetActive(true);

        if (dispatchStartedThisPress)
            playerAnim.SetDispatchHold(true);
        else if (!HasActiveRobot() && CanSpawnRobot() && playerAnim.BeginDispatch())
        {
            dispatchStartedThisPress = true;
            playerAnim.SetDispatchHold(true);
        }
    }

    void UpdateAimingDrift()
    {
        currentAimOffset = Mathf.Min(
            currentAimOffset + previewMoveSpeed * Time.deltaTime,
            maxPreviewDistance);
        robotGeneratePoint.position = aimOriginWorldPos
            + Vector3.right * aimFacing * currentAimOffset;
    }

    void ExitAimingMode()
    {
        if (positionPreview != null)
            positionPreview.SetActive(false);

        if (generatePointParent != null)
            robotGeneratePoint.SetParent(generatePointParent, false);

        robotGeneratePoint.localPosition = defaultLocalPos;
        generatePointParent = null;
        phase = RobotAbilityPhase.Idle;
        EndPlayerDispatch();
    }

    void EndPlayerDispatch()
    {
        if (!dispatchStartedThisPress && !playerAnim.IsDispatching)
            return;

        playerAnim.SetDispatchHold(false);
        playerAnim.EndDispatch();
        dispatchStartedThisPress = false;
    }

    bool TrySpawnRobot(Vector3 worldPos, RobotDeployMode mode)
    {
        if (robotPrefab == null)
        {
            Debug.LogWarning("PlayerAbilities: robotPrefab 未配置。", this);
            return false;
        }

        if (!CanSpawnRobot())
            return false;

        if (IsSpawnPositionInsideGround(worldPos))
            return false;

        var robot = Instantiate(robotPrefab, worldPos, Quaternion.identity);
        robot.GetComponent<AllyRobot>()?.Initialize(transform, mode, robotFollowPoint);
        OnRobotSpawned(robot);
        SpawnOpenCore(worldPos);
        return true;
    }

    LayerMask ResolveSpawnBlockMask()
    {
        return spawnBlockMask.value != 0
            ? spawnBlockMask
            : (LayerMask)LayerMask.GetMask("Ground");
    }

    bool IsSpawnPositionInsideGround(Vector3 worldPos)
    {
        LayerMask mask = ResolveSpawnBlockMask();
        Vector2 pos = worldPos;

        // BoxCollider 等实心碰撞体。
        if (Physics2D.OverlapPoint(pos, mask) != null)
            return true;

        // Tilemap + CompositeCollider 常用 Outlines：内部没有填充，OverlapPoint 会漏。
        if (spawnBlockProbeRadius > 0f && Physics2D.OverlapCircle(pos, spawnBlockProbeRadius, mask) != null)
            return true;

        return IsInsideGroundTile(pos, mask);
    }

    bool IsInsideGroundTile(Vector2 worldPos, LayerMask mask)
    {
        var tilemaps = FindObjectsByType<Tilemap>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < tilemaps.Length; i++)
        {
            var tilemap = tilemaps[i];
            if (tilemap == null)
                continue;
            if (((1 << tilemap.gameObject.layer) & mask) == 0)
                continue;
            if (tilemap.GetComponent<TilemapCollider2D>() == null)
                continue;
            if (tilemap.HasTile(tilemap.WorldToCell(worldPos)))
                return true;
        }

        return false;
    }

    void SpawnOpenCore(Vector3 worldPos)
    {
        RobotCoreVisual core = InstantiateCore(worldPos);
        core?.PlayOpenThenDestroy();
    }

    void BeginRecall(bool playPlayerAnim)
    {
        if (!HasActiveRobot() || IsRecallInProgress())
            return;

        if (playPlayerAnim)
            playerAnim.TryPlayRecallAnim();

        Vector3 corePos = activeRobot.transform.position;
        SpawnReturningCore(corePos);

        if (activeRobotController != null)
            activeRobotController.BeginRecall();
        else
            DestroyActiveRobot();
    }

    void SpawnReturningCore(Vector3 worldPos)
    {
        RobotCoreVisual core = InstantiateCore(worldPos);
        if (core == null)
            return;

        returningCore = core;
        core.PlayCloseThenReturn(
            transform,
            robotCoreReturnOffset,
            robotCoreFlySpeed,
            robotCoreArriveThreshold,
            ClearReturningCore);
    }

    RobotCoreVisual InstantiateCore(Vector3 worldPos)
    {
        if (robotCorePrefab == null)
        {
            Debug.LogWarning("PlayerAbilities: robotCorePrefab 未配置。", this);
            return null;
        }

        var go = Instantiate(robotCorePrefab, worldPos, Quaternion.identity);
        var visual = go.GetComponent<RobotCoreVisual>();
        if (visual == null)
            visual = go.AddComponent<RobotCoreVisual>();
        return visual;
    }

    void ClearReturningCore()
    {
        returningCore = null;
    }

    void DestroyReturningCoreImmediate()
    {
        if (returningCore != null)
            returningCore.CancelAndDestroy();
        returningCore = null;
    }

    /// <summary>
    /// 短按 Ability1：消耗当前 WeaponID 对应普通弹，装入特殊弹。
    /// 超容或弹药不足时整次取消。不依赖机器人是否存在。
    /// </summary>
    bool TryConvertAmmoToSpecial()
    {
        if (specialMagazine == null || character == null || weaponController == null)
            return false;

        int weaponId = weaponController.CurrentWeaponId;
        if (weaponId < 1 || weaponId > 3)
            return false;

        int cost = GetReloadAmmoCost(weaponId);
        int loadCount = GetReloadLoadCount(weaponId);
        if (loadCount <= 0)
            return false;

        if (specialMagazine.Count + loadCount > specialMagazine.Capacity)
            return false;

        AmmoType ammoType = weaponId switch
        {
            1 => AmmoType.S,
            2 => AmmoType.M,
            3 => AmmoType.L,
            _ => AmmoType.S,
        };

        SpecialAmmoType specialType = weaponId switch
        {
            1 => SpecialAmmoType.S,
            2 => SpecialAmmoType.M,
            3 => SpecialAmmoType.L,
            _ => SpecialAmmoType.S,
        };

        if (!character.TrySpendAmmo(ammoType, cost))
            return false;

        if (!specialMagazine.TryLoad(specialType, loadCount))
        {
            // 理论上容量已预检，不应失败；若失败则退回已扣弹药以保持原子性。
            character.AddAmmo(ammoType, cost);
            return false;
        }

        return true;
    }

    int GetReloadAmmoCost(int weaponId)
    {
        if (reloadAmmoCosts == null || weaponId < 0 || weaponId >= reloadAmmoCosts.Length)
            return 0;
        return reloadAmmoCosts[weaponId];
    }

    int GetReloadLoadCount(int weaponId)
    {
        if (reloadLoadCounts == null || weaponId < 0 || weaponId >= reloadLoadCounts.Length)
            return 0;
        return reloadLoadCounts[weaponId];
    }

    public DataDefination GetDataID() => GetComponent<DataDefination>();

    public void GetSaveData(Data data)
    {
        // 机器人不持久化；读档一律召回。
    }

    public void LoadSaveData(Data data)
    {
        if (phase == RobotAbilityPhase.Aiming)
            ExitAimingMode();
        else
        {
            EndPlayerDispatch();
            phase = RobotAbilityPhase.Idle;
        }

        DestroyReturningCoreImmediate();
        DestroyActiveRobot();
    }
}
