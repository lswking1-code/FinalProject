using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(PlayerMovement))]
[RequireComponent(typeof(Character))]
[RequireComponent(typeof(PlayerAnim))]
public class PlayerAbilities : MonoBehaviour
{
    enum Ability1Phase { Idle, Pressing, Aiming }

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

    Ability1Phase phase = Ability1Phase.Idle;
    InputSystem_Actions actions;
    PlayerMovement playerMovement;
    PlayerAnim playerAnim;
    Character character;
    GameObject activeRobot;

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
    }

    void OnEnable() => actions.Player.Enable();

    void OnDisable()
    {
        if (phase == Ability1Phase.Aiming)
            ExitAimingMode();
        else if (phase == Ability1Phase.Pressing)
            phase = Ability1Phase.Idle;

        EndPlayerDispatch();
        actions.Player.Disable();
    }

    void OnDestroy() => actions?.Dispose();

    void Update()
    {
        if (robotGeneratePoint == null)
            return;

        UpdateRobotDrain();

        switch (phase)
        {
            case Ability1Phase.Idle:
                if (actions.Player.Ability1.WasPressedThisFrame())
                    BeginPress();
                break;

            case Ability1Phase.Pressing:
                if (actions.Player.Ability1.IsPressed()
                    && Time.time - pressTime >= longPressThreshold)
                {
                    if (HasActiveRobot())
                    {
                        DestroyActiveRobot();
                        EndPlayerDispatch();
                        phase = Ability1Phase.Idle;
                    }
                    else
                    {
                        EnterAimingMode();
                    }
                }

                if (actions.Player.Ability1.WasReleasedThisFrame())
                {
                    if (Time.time - pressTime < longPressThreshold
                        && !HasActiveRobot() && CanSpawnRobot())
                    {
                        SpawnRobot(robotGeneratePoint.position);
                        // 短按：intro 播完（或已定格）后结束召唤动画
                        if (dispatchStartedThisPress)
                            playerAnim.SetDispatchAutoEnd(true);
                        dispatchStartedThisPress = false;
                    }
                    else
                    {
                        EndPlayerDispatch();
                    }

                    phase = Ability1Phase.Idle;
                }
                break;

            case Ability1Phase.Aiming:
                UpdateAimingDrift();

                if (actions.Player.Ability1.WasReleasedThisFrame())
                {
                    if (CanSpawnRobot())
                        SpawnRobot(robotGeneratePoint.position);

                    ExitAimingMode();
                }
                break;
        }

        UpdateAbility2();
    }

    void UpdateAbility2()
    {
        if (playerMovement.IsActionLocked || phase != Ability1Phase.Idle)
            return;

        if (!actions.Player.Ability2.WasPressedThisFrame() || !HasActiveRobot())
            return;

        activeRobot.GetComponent<AllyRobot>()?.TryStartPull();
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
        character.pauseAbilityPowerRecover = true;
    }

    void OnRobotRemoved()
    {
        activeRobot = null;
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

        phase = Ability1Phase.Pressing;
        pressTime = Time.time;
        defaultLocalPos = robotGeneratePoint.localPosition;
        dispatchStartedThisPress = false;

        if (!HasActiveRobot() && CanSpawnRobot() && playerAnim.BeginDispatch())
            dispatchStartedThisPress = true;
    }

    void EnterAimingMode()
    {
        phase = Ability1Phase.Aiming;
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
        phase = Ability1Phase.Idle;
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
}
