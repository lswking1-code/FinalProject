using UnityEngine;

/// <summary>
/// 往复升降平台：开关 ON 时按固定单程时间与高度升降；OFF 时冻结在当前位置。
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
public class ReciprocatingPlatform : MonoBehaviour
{
    [Header("开关")]
    [SerializeField] ToggleSwitch activationSwitch;
    [SerializeField] bool listenToSwitch = true;

    [Header("运动")]
    [SerializeField] float travelHeight = 4f;
    [SerializeField] float travelDuration = 2f;
    [SerializeField] Vector2 moveDirection = Vector2.up;
    [SerializeField] bool startAtBottom = true;

    Rigidbody2D rb;
    Vector2 bottomPos;
    Vector2 topPos;
    Vector2 normalizedDirection;
    bool movingUp;
    bool isRunning;
    Vector2 platformVelocity;

    public Vector2 PlatformVelocity => isRunning ? platformVelocity : Vector2.zero;

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

        movingUp = startAtBottom;
        rb.position = startAtBottom ? bottomPos : topPos;
        if (!startAtBottom)
            movingUp = false;
    }

    void OnEnable()
    {
        if (listenToSwitch && activationSwitch != null)
            activationSwitch.onToggled.AddListener(SetRunning);
    }

    void Start()
    {
        if (listenToSwitch && activationSwitch != null && activationSwitch.IsOn)
            SetRunning(true);
    }

    void OnDisable()
    {
        if (activationSwitch != null)
            activationSwitch.onToggled.RemoveListener(SetRunning);
    }

    public void SetRunning(bool on)
    {
        isRunning = on;
        if (!isRunning)
            platformVelocity = Vector2.zero;
    }

    void FixedUpdate()
    {
        if (!isRunning || travelDuration <= 0f || travelHeight <= 0f)
        {
            platformVelocity = Vector2.zero;
            return;
        }

        Vector2 previousPos = rb.position;
        float speed = travelHeight / travelDuration;
        float step = speed * Time.fixedDeltaTime;
        Vector2 target = movingUp ? topPos : bottomPos;
        Vector2 toTarget = target - previousPos;
        float distance = toTarget.magnitude;

        if (distance <= step)
        {
            rb.MovePosition(target);
            movingUp = !movingUp;
        }
        else
        {
            rb.MovePosition(previousPos + toTarget / distance * step);
        }

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
