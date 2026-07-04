using UnityEngine;

/// <summary>
/// Gold Miner 风格钩爪视觉：伸出 → 抓取 → 收回。
/// 由 AllyRobot 在 TryStartPull 时启动，通过回调驱动玩家锁定与位移。
/// </summary>
public class AllyRobotPullVisual : MonoBehaviour
{
    public enum PullVisualPhase { Idle, Extending, Grabbed, Retracting }

    public enum RopeVisualType { LineRenderer, TiledSprite }

    [Header("引用")]
    [SerializeField] Transform hookMuzzle;
    [SerializeField] Transform hookHead;
    [SerializeField] SpriteRenderer hookHeadRenderer;

    [Header("钩爪精灵")]
    [SerializeField] Sprite hookHeadSpriteOpen;
    [SerializeField] Sprite hookHeadSpriteClosed;

    [Header("绳索")]
    [SerializeField] RopeVisualType ropeVisualType = RopeVisualType.LineRenderer;
    [SerializeField] LineRendererRope lineRendererRope;
    [SerializeField] TiledSpriteRope tiledSpriteRope;

    [Header("运动")]
    [SerializeField] float hookExtendSpeed = 12f;
    [SerializeField] float hookRetractSpeed = 8f;
    [SerializeField] float hookGrabRadius = 0.3f;
    [SerializeField] float hookArriveThreshold = 0.1f;
    [Tooltip("抓取目标相对玩家 Transform 中心向上的偏移（世界单位）")]
    [SerializeField] float grabTargetYOffset = 0.4f;

    AllyRobot robot;
    IRopeVisual rope;
    Transform owner;
    PullVisualPhase phase = PullVisualPhase.Idle;
    Vector2 hookWorldPos;
    float activeExtendSpeed;
    float activeRetractSpeed;
    float activeGrabRadius;
    float activeArriveThreshold;

    Transform hookHeadParent;
    Vector3 hookHeadBaseScale = Vector3.one;
    bool hookHeadDetached;

    public PullVisualPhase Phase => phase;
    public bool IsActive => phase != PullVisualPhase.Idle;

    void Awake()
    {
        if (hookMuzzle == null)
            hookMuzzle = transform;

        if (hookHead == null)
        {
            var head = transform.Find("HookHead");
            if (head != null)
                hookHead = head;
        }

        if (hookHeadRenderer == null && hookHead != null)
            hookHeadRenderer = hookHead.GetComponent<SpriteRenderer>();

        if (hookHeadSpriteOpen == null && hookHeadRenderer != null)
            hookHeadSpriteOpen = hookHeadRenderer.sprite;
        if (hookHeadSpriteClosed == null && hookHeadRenderer != null)
            hookHeadSpriteClosed = hookHeadRenderer.sprite;

        if (lineRendererRope == null)
            lineRendererRope = GetComponentInChildren<LineRendererRope>(true);

        if (tiledSpriteRope == null)
            tiledSpriteRope = GetComponentInChildren<TiledSpriteRope>(true);

        ResolveRopeVisual();
        SetVisualActive(false);
    }

    void ResolveRopeVisual()
    {
        rope = ropeVisualType == RopeVisualType.TiledSprite && tiledSpriteRope != null
            ? tiledSpriteRope
            : lineRendererRope;
    }

    public void Initialize(AllyRobot allyRobot) => robot = allyRobot;

    public void Begin(
        Transform player,
        float extendSpeed,
        float retractSpeed,
        float grabRadius,
        float arriveThreshold)
    {
        if (player == null || robot == null)
            return;

        owner = player;
        activeExtendSpeed = extendSpeed > 0f ? extendSpeed : hookExtendSpeed;
        activeRetractSpeed = retractSpeed > 0f ? retractSpeed : hookRetractSpeed;
        activeGrabRadius = grabRadius > 0f ? grabRadius : hookGrabRadius;
        activeArriveThreshold = arriveThreshold > 0f ? arriveThreshold : hookArriveThreshold;

        hookWorldPos = GetMuzzleWorldPosition();
        phase = PullVisualPhase.Extending;

        if (hookHeadSpriteOpen != null && hookHeadRenderer != null)
            hookHeadRenderer.sprite = hookHeadSpriteOpen;

        SetVisualActive(true);
        DetachHookHead();
        UpdateHookTransform(hookWorldPos);
        UpdateRope();
    }

    public void Cancel()
    {
        phase = PullVisualPhase.Idle;
        owner = null;
        SetVisualActive(false);
    }

    void OnDestroy() => ReattachHookHead();

    void FixedUpdate()
    {
        if (phase == PullVisualPhase.Idle || owner == null || robot == null)
            return;

        switch (phase)
        {
            case PullVisualPhase.Extending:
                TickExtending();
                break;
            case PullVisualPhase.Grabbed:
                TickGrabbed();
                break;
            case PullVisualPhase.Retracting:
                TickRetracting();
                break;
        }
    }

    void TickExtending()
    {
        Vector2 target = GetPlayerGrabTarget();
        hookWorldPos = Vector2.MoveTowards(hookWorldPos, target, activeExtendSpeed * Time.fixedDeltaTime);
        UpdateHookTransform(hookWorldPos);
        UpdateRope();

        if (Vector2.Distance(hookWorldPos, target) <= activeGrabRadius)
            EnterGrabbed();
    }

    void EnterGrabbed()
    {
        phase = PullVisualPhase.Grabbed;
        hookWorldPos = GetPlayerGrabTarget();

        if (hookHeadSpriteClosed != null && hookHeadRenderer != null)
            hookHeadRenderer.sprite = hookHeadSpriteClosed;

        UpdateHookTransform(hookWorldPos);
        UpdateRope();
        robot.OnHookGrabbed();
        robot.OnHookRetractStep(GetPlayerCenterFromHook(hookWorldPos));
    }

    void TickGrabbed()
    {
        hookWorldPos = GetPlayerGrabTarget();
        UpdateHookTransform(hookWorldPos);
        UpdateRope();
        phase = PullVisualPhase.Retracting;
    }

    void TickRetracting()
    {
        Vector2 target = robot.GetPullLandingPoint() + Vector2.up * grabTargetYOffset;
        hookWorldPos = Vector2.MoveTowards(hookWorldPos, target, activeRetractSpeed * Time.fixedDeltaTime);
        UpdateHookTransform(hookWorldPos);
        UpdateRope();
        robot.OnHookRetractStep(GetPlayerCenterFromHook(hookWorldPos));

        if (Vector2.Distance(hookWorldPos, target) <= activeArriveThreshold)
            CompleteRetract();
    }

    void CompleteRetract()
    {
        hookWorldPos = robot.GetPullLandingPoint();
        SetVisualActive(false);
        phase = PullVisualPhase.Idle;
        robot.OnHookRetractComplete();
        owner = null;
    }

    Vector2 GetPlayerGrabTarget()
    {
        if (owner == null)
            return Vector2.zero;

        return (Vector2)owner.position + Vector2.up * grabTargetYOffset;
    }

    Vector2 GetPlayerCenterFromHook(Vector2 hookPos) => hookPos - Vector2.up * grabTargetYOffset;

    void UpdateHookTransform(Vector2 position)
    {
        if (hookHead == null)
            return;

        hookHead.position = position;
        ApplyHookFacing(GetHookFaceTowardPlayer());
    }

    Vector2 GetHookFaceTowardPlayer()
    {
        if (owner == null)
            return Vector2.right;

        Vector2 towardPlayer = (Vector2)owner.position - hookWorldPos;
        if (towardPlayer.sqrMagnitude > 0.0001f)
            return towardPlayer;

        if (robot != null)
        {
            Vector2 robotToPlayer = (Vector2)owner.position - (Vector2)robot.transform.position;
            if (robotToPlayer.sqrMagnitude > 0.0001f)
                return robotToPlayer;
        }

        return Vector2.right;
    }

    /// <summary>
    /// 钩头默认贴图朝 +X。朝左时用 flipX 而非旋转 180°，避免非对称精灵看起来翻转。
    /// </summary>
    void ApplyHookFacing(Vector2 faceDir)
    {
        if (faceDir.sqrMagnitude < 0.0001f)
            return;

        Vector2 dir = faceDir.normalized;
        bool faceLeft = dir.x < 0f;

        if (hookHeadRenderer != null)
        {
            hookHeadRenderer.flipX = faceLeft;
            hookHeadRenderer.flipY = false;
        }

        float angle = Mathf.Atan2(dir.y, Mathf.Abs(dir.x)) * Mathf.Rad2Deg;
        hookHead.rotation = Quaternion.Euler(0f, 0f, angle);
    }

    void DetachHookHead()
    {
        if (hookHead == null || hookHeadDetached)
            return;

        hookHeadParent = hookHead.parent;
        hookHeadBaseScale = hookHead.localScale;
        hookHead.SetParent(null, true);
        hookHead.localScale = new Vector3(
            Mathf.Abs(hookHeadBaseScale.x),
            Mathf.Abs(hookHeadBaseScale.y),
            hookHeadBaseScale.z);
        hookHeadDetached = true;
    }

    void ReattachHookHead()
    {
        if (hookHead == null || !hookHeadDetached)
            return;

        if (hookHeadParent != null)
        {
            hookHead.SetParent(hookHeadParent, false);
            hookHead.localPosition = Vector3.zero;
            hookHead.localRotation = Quaternion.identity;
            hookHead.localScale = hookHeadBaseScale;
            if (hookHeadRenderer != null)
            {
                hookHeadRenderer.flipX = false;
                hookHeadRenderer.flipY = false;
            }
        }

        hookHeadDetached = false;
        hookHeadParent = null;
    }

    Vector2 GetMuzzleWorldPosition()
    {
        if (robot == null || hookMuzzle == null)
            return hookMuzzle != null ? hookMuzzle.position : (Vector2)transform.position;

        float face = Mathf.Sign(robot.transform.localScale.x);
        if (Mathf.Approximately(face, 0f))
            face = 1f;

        Vector3 localOffset = hookMuzzle.localPosition;
        Vector3 robotScale = robot.transform.lossyScale;
        Vector2 worldOffset = new Vector2(localOffset.x * face * Mathf.Abs(robotScale.x), localOffset.y * robotScale.y);
        return (Vector2)robot.transform.position + worldOffset;
    }

    void UpdateRope()
    {
        if (rope == null)
            return;

        rope.SetEndpoints(GetMuzzleWorldPosition(), hookWorldPos);
    }

    void SetVisualActive(bool active)
    {
        if (!active)
            ReattachHookHead();

        if (hookHead != null)
            hookHead.gameObject.SetActive(active);

        if (rope != null)
            rope.SetVisible(active);
    }
}
