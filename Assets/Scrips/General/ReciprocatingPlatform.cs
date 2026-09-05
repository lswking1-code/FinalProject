using UnityEngine;

/// <summary>
/// 升降平台：多开关/压力板组合控制。可往复循环，或单次开合（ON 到终点、OFF 回初始位置）。
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
    [Tooltip("勾选后：激活仅移动一次到终点并停下；关闭时移回初始位置。取消勾选则为往复循环，关闭时冻结当前位置。")]
    [SerializeField] bool oneShot;

    Rigidbody2D rb;
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
    void OnDrawGizmosSelected()
    {
        Vector2 dir = moveDirection.sqrMagnitude > 0.0001f ? moveDirection.normalized : Vector2.up;
        Vector2 bottom = Application.isPlaying ? bottomPos : (Vector2)transform.position;
        Vector2 top = bottom + dir * travelHeight;

        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(bottom, top);
        Gizmos.DrawWireSphere(bottom, 0.12f);
        Gizmos.DrawWireSphere(top, 0.12f);
    }
#endif
}
