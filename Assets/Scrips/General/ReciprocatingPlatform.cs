using UnityEngine;

/// <summary>
/// 升降平台：多开关/压力板组合控制。
/// 不勾选 oneShot 为持续移动（ON 往复、OFF 停在当前位置）；勾选为单次开合（ON 到终点、OFF 回初始位置）。
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
public class ReciprocatingPlatform : MonoBehaviour
{
    public enum CombineMode
    {
        And,
        Or,
    }

    [Header("开关")]
    [SerializeField] ToggleSwitch[] activationSwitches;
    [SerializeField] PressurePlate[] activationPlates;
    [SerializeField] CombineMode combineMode = CombineMode.Or;
    [SerializeField] bool listenToSwitch = true;

    [Header("运动")]
    [SerializeField] float travelHeight = 4f;
    [SerializeField] float travelDuration = 2f;
    [SerializeField] Vector2 moveDirection = Vector2.up;
    [SerializeField] bool startAtBottom = true;
    [Tooltip("不勾选 = 持续移动：ON 往复循环，OFF 停在当前位置，不回落。勾选 = 单次开合：ON 到终点，OFF 移回初始位置。")]
    [SerializeField] bool oneShot;

    [Header("单向平台")]
    [Tooltip("开启后同单向平台：从上压下穿过玩家/敌人，可上穿与主动下穿")]
    [SerializeField] bool oneWay = true;
    [Tooltip("PlatformEffector2D 表面弧角（度）；180 为常见单向平台顶部")]
    [SerializeField, Range(1f, 360f)] float surfaceArc = 180f;

    Rigidbody2D rb;
    PlatformEffector2D platformEffector;
    Vector2 bottomPos;
    Vector2 topPos;
    Vector2 homePos;
    Vector2 awayPos;
    Vector2 normalizedDirection;
    bool movingUp;
    bool isActivated;
    Vector2 platformVelocity;

    public Vector2 PlatformVelocity => platformVelocity;
    public bool IsRunning => isActivated;

    void Awake()
    {
        ApplyOneWayMode();

        rb = GetComponent<Rigidbody2D>();
        rb.bodyType = RigidbodyType2D.Kinematic;
        rb.gravityScale = 0f;
        rb.constraints = RigidbodyConstraints2D.FreezeRotation;

        normalizedDirection = moveDirection.sqrMagnitude > 0.0001f
            ? moveDirection.normalized
            : Vector2.up;

        bottomPos = rb.position;
        topPos = bottomPos + normalizedDirection * travelHeight;

        homePos = startAtBottom ? bottomPos : topPos;
        awayPos = startAtBottom ? topPos : bottomPos;
        rb.position = homePos;

        // 往复模式：从初始端朝另一端出发
        movingUp = startAtBottom;
    }

    void OnEnable()
    {
        if (!listenToSwitch)
            return;

        SubscribeInputs();
    }

    void Start()
    {
        if (!listenToSwitch)
            return;

        SetRunning(EvaluateInputs());
    }

    void OnDisable()
    {
        UnsubscribeInputs();
    }

    void OnInputChanged(bool _)
    {
        SetRunning(EvaluateInputs());
    }

    public void SetRunning(bool on)
    {
        isActivated = on;
        if (!oneShot && !isActivated)
            platformVelocity = Vector2.zero;
    }

    bool EvaluateInputs()
    {
        bool hasInput = false;
        bool anyOn = false;
        bool allOn = true;

        if (activationSwitches != null)
        {
            for (int i = 0; i < activationSwitches.Length; i++)
            {
                ToggleSwitch sw = activationSwitches[i];
                if (sw == null)
                    continue;

                hasInput = true;
                if (sw.IsOn)
                    anyOn = true;
                else
                    allOn = false;
            }
        }

        if (activationPlates != null)
        {
            for (int i = 0; i < activationPlates.Length; i++)
            {
                PressurePlate plate = activationPlates[i];
                if (plate == null)
                    continue;

                hasInput = true;
                if (plate.IsOn)
                    anyOn = true;
                else
                    allOn = false;
            }
        }

        if (!hasInput)
            return false;

        return combineMode == CombineMode.And ? allOn : anyOn;
    }

    void SubscribeInputs()
    {
        if (activationSwitches != null)
        {
            for (int i = 0; i < activationSwitches.Length; i++)
            {
                if (activationSwitches[i] != null)
                    activationSwitches[i].onToggled.AddListener(OnInputChanged);
            }
        }

        if (activationPlates != null)
        {
            for (int i = 0; i < activationPlates.Length; i++)
            {
                if (activationPlates[i] != null)
                    activationPlates[i].onToggled.AddListener(OnInputChanged);
            }
        }
    }

    void UnsubscribeInputs()
    {
        if (activationSwitches != null)
        {
            for (int i = 0; i < activationSwitches.Length; i++)
            {
                if (activationSwitches[i] != null)
                    activationSwitches[i].onToggled.RemoveListener(OnInputChanged);
            }
        }

        if (activationPlates != null)
        {
            for (int i = 0; i < activationPlates.Length; i++)
            {
                if (activationPlates[i] != null)
                    activationPlates[i].onToggled.RemoveListener(OnInputChanged);
            }
        }
    }

    void FixedUpdate()
    {
        if (travelDuration <= 0f || travelHeight <= 0f)
        {
            platformVelocity = Vector2.zero;
            return;
        }

        if (oneShot)
            UpdateOneShot();
        else
            UpdateContinuous();
    }

    void UpdateOneShot()
    {
        Vector2 target = isActivated ? awayPos : homePos;
        MoveToward(target, reverseOnArrive: false);
    }

    void UpdateContinuous()
    {
        if (!isActivated)
        {
            platformVelocity = Vector2.zero;
            return;
        }

        Vector2 target = movingUp ? topPos : bottomPos;
        MoveToward(target, reverseOnArrive: true);
    }

    void MoveToward(Vector2 target, bool reverseOnArrive)
    {
        Vector2 previousPos = rb.position;
        float speed = travelHeight / travelDuration;
        float step = speed * Time.fixedDeltaTime;
        Vector2 toTarget = target - previousPos;
        float distance = toTarget.magnitude;

        if (distance <= step)
        {
            rb.MovePosition(target);
            if (reverseOnArrive)
                movingUp = !movingUp;
            platformVelocity = Vector2.zero;
            return;
        }

        rb.MovePosition(previousPos + toTarget / distance * step);
        platformVelocity = (rb.position - previousPos) / Time.fixedDeltaTime;
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        ApplyOneWayMode();
    }
#endif

    void ApplyOneWayMode()
    {
        platformEffector = GetComponent<PlatformEffector2D>();
        if (platformEffector == null)
        {
            // 仅运行时自动补组件；编辑器请在 Prefab 上预挂 PlatformEffector2D
            if (!Application.isPlaying || !oneWay)
            {
                SetRootCollidersUsedByEffector(false);
                return;
            }

            platformEffector = gameObject.AddComponent<PlatformEffector2D>();
        }

        if (oneWay)
        {
            platformEffector.enabled = true;
            platformEffector.useOneWay = true;
            platformEffector.surfaceArc = surfaceArc;
            platformEffector.useOneWayGrouping = false;
            SetRootCollidersUsedByEffector(true);
        }
        else
        {
            platformEffector.useOneWay = false;
            platformEffector.enabled = false;
            SetRootCollidersUsedByEffector(false);
        }
    }

    void SetRootCollidersUsedByEffector(bool used)
    {
        var rootColliders = GetComponents<Collider2D>();
        for (int i = 0; i < rootColliders.Length; i++)
        {
            if (rootColliders[i] != null)
                rootColliders[i].usedByEffector = used;
        }
    }

#if UNITY_EDITOR
    void OnDrawGizmos()
    {
        DrawTravelPreview(selected: false);
    }

    void OnDrawGizmosSelected()
    {
        DrawTravelPreview(selected: true);
    }

    void DrawTravelPreview(bool selected)
    {
        if (travelHeight <= 0f)
            return;

        GetPreviewEnds(out Vector2 home, out Vector2 away);
        Vector3 home3 = home;
        Vector3 away3 = away;
        Vector3 mid = (home3 + away3) * 0.5f;
        Color path = selected ? new Color(0.2f, 0.95f, 1f, 1f) : new Color(0.2f, 0.85f, 1f, 0.7f);
        Color ghost = selected ? new Color(0.2f, 0.95f, 1f, 0.22f) : new Color(0.2f, 0.85f, 1f, 0.12f);

        Gizmos.color = path;
        Gizmos.DrawLine(home3, away3);
        Gizmos.DrawWireSphere(home3, selected ? 0.14f : 0.1f);
        Gizmos.DrawWireSphere(away3, selected ? 0.14f : 0.1f);

        Vector2 travel = away - home;
        if (travel.sqrMagnitude > 0.0001f)
        {
            Vector2 n = travel.normalized;
            Vector2 perp = new Vector2(-n.y, n.x);
            float head = selected ? 0.28f : 0.22f;
            Vector3 tip = away3;
            Gizmos.DrawLine(tip, tip - (Vector3)(n * head + perp * head * 0.45f));
            Gizmos.DrawLine(tip, tip - (Vector3)(n * head - perp * head * 0.45f));
        }

        GetPreviewCollider(out Vector3 homeCenter, out Vector3 awayCenter, out Vector3 colliderSize);
        Gizmos.color = ghost;
        Gizmos.DrawCube(awayCenter, colliderSize);
        Gizmos.color = path;
        Gizmos.DrawWireCube(homeCenter, colliderSize);
        Gizmos.DrawWireCube(awayCenter, colliderSize);

        var style = new GUIStyle(UnityEditor.EditorStyles.boldLabel)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = selected ? 12 : 11
        };
        style.normal.textColor = path;
        UnityEditor.Handles.Label(mid + Vector3.right * 0.2f, $"{travelHeight:0.##}", style);
    }

    void GetPreviewEnds(out Vector2 home, out Vector2 away)
    {
        if (Application.isPlaying)
        {
            home = homePos;
            away = awayPos;
            return;
        }

        Vector2 dir = moveDirection.sqrMagnitude > 0.0001f ? moveDirection.normalized : Vector2.up;
        Vector2 origin = transform.position;
        home = origin;
        away = origin + dir * travelHeight;
    }

    void GetPreviewCollider(out Vector3 homeCenter, out Vector3 awayCenter, out Vector3 size)
    {
        GetPreviewEnds(out Vector2 home, out Vector2 away);
        var box = GetComponent<BoxCollider2D>();
        Vector3 currentCenter = transform.position;
        size = Vector3.one * 0.4f;

        if (box != null)
        {
            currentCenter = transform.TransformPoint(box.offset);
            size = new Vector3(
                Mathf.Abs(box.size.x * transform.lossyScale.x),
                Mathf.Abs(box.size.y * transform.lossyScale.y),
                0.05f);
        }

        Vector3 deltaHome = (Vector3)home - transform.position;
        Vector3 deltaAway = (Vector3)away - transform.position;
        homeCenter = currentCenter + deltaHome;
        awayCenter = currentCenter + deltaAway;
    }
#endif
}
