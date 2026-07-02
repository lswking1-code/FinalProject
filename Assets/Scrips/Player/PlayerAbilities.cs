using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(PlayerMovement))]
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

    Ability1Phase phase = Ability1Phase.Idle;
    InputSystem_Actions actions;
    PlayerMovement playerMovement;

    float pressTime;
    Vector3 defaultLocalPos;
    Vector3 aimOriginWorldPos;
    float aimFacing;
    float currentAimOffset;
    Transform generatePointParent;

    void Awake()
    {
        actions = new InputSystem_Actions();
        playerMovement = GetComponent<PlayerMovement>();
    }

    void OnEnable() => actions.Player.Enable();

    void OnDisable()
    {
        if (phase == Ability1Phase.Aiming)
            ExitAimingMode();
        else if (phase == Ability1Phase.Pressing)
            phase = Ability1Phase.Idle;

        actions.Player.Disable();
    }

    void OnDestroy() => actions?.Dispose();

    void Update()
    {
        if (robotGeneratePoint == null)
            return;

        switch (phase)
        {
            case Ability1Phase.Idle:
                if (actions.Player.Ability1.WasPressedThisFrame())
                    BeginPress();
                break;

            case Ability1Phase.Pressing:
                if (actions.Player.Ability1.IsPressed()
                    && Time.time - pressTime >= longPressThreshold)
                    EnterAimingMode();

                if (actions.Player.Ability1.WasReleasedThisFrame())
                {
                    SpawnRobot(robotGeneratePoint.position);
                    phase = Ability1Phase.Idle;
                }
                break;

            case Ability1Phase.Aiming:
                UpdateAimingDrift();

                if (actions.Player.Ability1.WasReleasedThisFrame())
                {
                    SpawnRobot(robotGeneratePoint.position);
                    ExitAimingMode();
                }
                break;
        }
    }

    void BeginPress()
    {
        phase = Ability1Phase.Pressing;
        pressTime = Time.time;
        defaultLocalPos = robotGeneratePoint.localPosition;
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
    }

    void SpawnRobot(Vector3 worldPos)
    {
        if (robotPrefab == null)
        {
            Debug.LogWarning("PlayerAbilities: robotPrefab 未配置。", this);
            return;
        }

        Instantiate(robotPrefab, worldPos, Quaternion.identity);
    }
}
