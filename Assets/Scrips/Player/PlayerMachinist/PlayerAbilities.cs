using UnityEngine;
using UnityEngine.InputSystem;

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
    [SerializeField] GameObject robotPrefab;
    [SerializeField] GameObject positionPreview;

    [Header("输入与放置")]
    [SerializeField] float longPressThreshold = 0.5f;
    [SerializeField] float previewMoveSpeed = 3f;
    [SerializeField] float maxPreviewDistance = 5f;

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

    RobotAbilityPhase phase = RobotAbilityPhase.Idle;
    InputSystem_Actions actions;
    PlayerMovement playerMovement;
    PlayerAnim playerAnim;
    Character character;
    PlayerWeaponController weaponController;
    SpecialMagazine specialMagazine;
    GameObject activeRobot;
    AllyRobot activeRobotController;

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
        actions.Player.Disable();
        ((ISaveable)this).UnregisterSaveData();
    }

    void OnDestroy()
    {
        if (newGameEvent != null)
            newGameEvent.OnEventRaised -= ResetForNewGame;
        actions?.Dispose();
    }

    void ResetForNewGame()
    {
        if (phase == RobotAbilityPhase.Aiming)
            ExitAimingMode();
        else
        {
            EndPlayerDispatch();
            phase = RobotAbilityPhase.Idle;
        }

        DestroyActiveRobot();
        specialMagazine?.Clear();
    }

    void Update()
    {
        if (robotGeneratePoint == null)
            return;

        UpdateRobotDrain();
        UpdateAbility1();

        switch (phase)
        {
            case RobotAbilityPhase.Idle:
                if (actions.Player.Ability2.WasPressedThisFrame())
                    BeginPress();
                break;

            case RobotAbilityPhase.Pressing:
                if (actions.Player.Ability2.IsPressed()
                    && Time.time - pressTime >= longPressThreshold)
                {
                    if (HasActiveRobot())
                    {
                        DestroyActiveRobot();
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
                            SpawnRobot(robotGeneratePoint.position);
                            // 短按：intro 播完（或已定格）后结束召唤动画
                            if (dispatchStartedThisPress)
                                playerAnim.SetDispatchAutoEnd(true);
                            dispatchStartedThisPress = false;
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
                        SpawnRobot(robotGeneratePoint.position);

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

        if (playerAnim.IsPlayingLoadBullet || playerAnim.IsPlayingMachinistComboShoot || playerAnim.IsDispatching)
            return;

        if (!actions.Player.Ability1.WasPressedThisFrame())
            return;

        if (!TryConvertAmmoToSpecial())
            return;

        playerAnim.TryPlayLoadBulletAnim();
    }

    bool HasActiveRobot() => activeRobot != null;

    bool CanSpawnRobot()
    {
        return !HasActiveRobot()
            && character != null
            && character.AbilityPower >= minAbilityPowerToSpawn;
    }

    void UpdateRobotDrain()
    {
        if (activeRobot != null && !activeRobot)
        {
            OnRobotRemoved();
            return;
        }

        if (!HasActiveRobot())
            return;

        character.DrainAbilityPower(robotDrainRate * Time.deltaTime);

        if (character.AbilityPower <= 0f)
            DestroyActiveRobot();
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

    void BeginPress()
    {
        if (playerMovement.IsActionLocked)
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

    void SpawnRobot(Vector3 worldPos)
    {
        if (robotPrefab == null)
        {
            Debug.LogWarning("PlayerAbilities: robotPrefab 未配置。", this);
            return;
        }

        if (!CanSpawnRobot())
            return;

        var robot = Instantiate(robotPrefab, worldPos, Quaternion.identity);
        robot.GetComponent<AllyRobot>()?.Initialize(transform);
        OnRobotSpawned(robot);
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

        DestroyActiveRobot();
    }
}
