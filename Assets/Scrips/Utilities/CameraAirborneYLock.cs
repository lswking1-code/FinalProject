using UnityEngine;

/// <summary>
/// 挂在 Cinemachine 相机上：上升弱跟、下落强跟并向下预瞄（Ori 式）；
/// 落地从触地瞬间真实锚点 Y 缓慢回到角色 Y，可选轻微回弹过冲。
/// </summary>
public class CameraAirborneYLock : MonoBehaviour
{
    [Header("开关")]
    [Tooltip("关闭后锚点始终完整跟随玩家（仍作为 TrackingTarget）")]
    [SerializeField] bool lockYWhileAirborne = true;

    [Header("上升 Y（弱跟）")]
    [Tooltip("相对离地高度，上升时跟随玩家竖直位移的比例")]
    [Range(0f, 1f)]
    [SerializeField] float riseYInfluence = 0.18f;
    [Tooltip("上升时锚点 Y 平滑时间（秒）")]
    [SerializeField] float riseYSmoothTime = 0.20f;

    [Header("下落 Y（强跟 + 预瞄）")]
    [Tooltip("相对离地高度，下落时跟随玩家竖直位移的比例")]
    [Range(0f, 1f)]
    [SerializeField] float fallYInfluence = 0.72f;
    [Tooltip("下落时锚点 Y 平滑时间（秒），越小越跟手")]
    [SerializeField] float fallYSmoothTime = 0.10f;
    [Tooltip("下落预瞄最大向下偏移（世界单位）")]
    [SerializeField] float fallLookAheadMax = 1.5f;
    [Tooltip("开始向下预瞄/切换下落强跟的下落速度阈值（正值，世界单位/秒）")]
    [SerializeField] float fallLookAheadMinSpeed = 2f;
    [Tooltip("预瞄与下落强跟达到满值时的下落速度（正值）")]
    [SerializeField] float fallLookAheadFullSpeed = 12f;

    [Header("落地回正")]
    [Tooltip("落地后从触地瞬间锚点 Y 缓慢回到角色 Y 的平滑时间（秒）")]
    [SerializeField] float landReturnSmoothTime = 0.35f;
    [Tooltip("回正结束判定：偏移与速度都小于此值")]
    [SerializeField] float landReturnSettleEpsilon = 0.015f;

    [Header("落地回弹")]
    [Tooltip("关闭后仅缓慢回正，不产生向上过冲回弹")]
    [SerializeField] bool enableLandRebound = true;
    [Tooltip("本次滞空峰值预瞄达到此值才叠加回弹冲量")]
    [SerializeField] float landReboundMinLookAhead = 0.08f;
    [Tooltip("落地瞬间向上回弹冲量倍率（相对当前下偏）")]
    [SerializeField] float landReboundKick = 2.5f;

    [Header("引用")]
    [Tooltip("留空则使用同物体上的 CameraControl")]
    [SerializeField] CameraControl cameraControl;
    [Tooltip("留空则从玩家解析 PhysicsCheck")]
    [SerializeField] PhysicsCheck physicsCheck;
    [Tooltip("留空则从玩家解析 Rigidbody2D")]
    [SerializeField] Rigidbody2D playerBody;
    [Tooltip("切场景后重绑；留空则尝试使用 CameraControl 上的同名事件")]
    public VoidEventSO afterSceneLoadEvent;

    Transform followAnchor;
    Transform playerTransform;
    bool ySoftTracking;
    bool wasSolidGround = true;
    bool hasGroundSample;
    float leaveGroundY;
    float airborneYVelocity;
    float activeLookAhead;
    float peakLookAheadThisAir;
    bool landSettling;
    float landOffsetY;
    float landOffsetVelocity;

    /// <summary>Cinemachine TrackingTarget 应绑定此锚点。</summary>
    public Transform FollowAnchor
    {
        get
        {
            EnsureFollowAnchor();
            return followAnchor;
        }
    }

    void Awake()
    {
        EnsureFollowAnchor();
        ResolveRefs();
    }

    void OnEnable()
    {
        ResolveRefs();
        if (afterSceneLoadEvent != null)
            afterSceneLoadEvent.OnEventRaised += OnAfterSceneLoad;

        SnapAnchorToPlayer(unlockY: true);
        hasGroundSample = false;
    }

    void OnDisable()
    {
        if (afterSceneLoadEvent != null)
            afterSceneLoadEvent.OnEventRaised -= OnAfterSceneLoad;
    }

    void OnDestroy()
    {
        if (followAnchor != null)
            Destroy(followAnchor.gameObject);
    }

    void OnAfterSceneLoad()
    {
        physicsCheck = null;
        playerBody = null;
        playerTransform = null;
        ResolveRefs();
        SnapAnchorToPlayer(unlockY: true);
        hasGroundSample = false;
    }

    void LateUpdate()
    {
        ResolveRefs();

        if (!lockYWhileAirborne)
        {
            ySoftTracking = false;
            airborneYVelocity = 0f;
            ClearLandSettle();
            FollowPlayerFully();
            return;
        }

        if (playerTransform == null)
            return;

        EnsureFollowAnchor();

        // 无 PhysicsCheck 时不限制 Y，避免误冻
        bool solid = physicsCheck == null || physicsCheck.isSolidGround;
        Vector3 playerPos = playerTransform.position;

        if (!hasGroundSample)
        {
            followAnchor.position = playerPos;
            leaveGroundY = playerPos.y;
            ySoftTracking = !solid;
            airborneYVelocity = 0f;
            ClearLandSettle();
            activeLookAhead = 0f;
            peakLookAheadThisAir = 0f;
            wasSolidGround = solid;
            hasGroundSample = true;
            return;
        }

        float y = followAnchor.position.y;

        if (solid)
        {
            // 刚触地：用当前真实锚点相对角色的偏移缓慢回正（预瞄只影响空中，不单独决定落地起点）
            if (!wasSolidGround)
            {
                landSettling = true;
                landOffsetY = y - playerPos.y;
                landOffsetVelocity = 0f;
                airborneYVelocity = 0f;

                if (enableLandRebound && peakLookAheadThisAir >= landReboundMinLookAhead)
                    landOffsetVelocity = Mathf.Max(0f, -landOffsetY) * Mathf.Max(0f, landReboundKick);
            }

            if (landSettling)
            {
                float smoothTime = Mathf.Max(0.01f, landReturnSmoothTime);
                landOffsetY = Mathf.SmoothDamp(
                    landOffsetY,
                    0f,
                    ref landOffsetVelocity,
                    smoothTime,
                    Mathf.Infinity,
                    Time.deltaTime);
                y = playerPos.y + landOffsetY;

                float eps = Mathf.Max(0.001f, landReturnSettleEpsilon);
                if (Mathf.Abs(landOffsetY) <= eps && Mathf.Abs(landOffsetVelocity) <= eps)
                {
                    ClearLandSettle();
                    y = playerPos.y;
                }
            }
            else
            {
                y = playerPos.y;
                airborneYVelocity = 0f;
            }

            ySoftTracking = false;
            leaveGroundY = playerPos.y;
            activeLookAhead = 0f;
            peakLookAheadThisAir = 0f;
        }
        else
        {
            ClearLandSettle();

            if (!ySoftTracking)
            {
                // 刚离地：记录离地高度；开局已在空中则用当前高度
                leaveGroundY = wasSolidGround ? followAnchor.position.y : playerPos.y;
                ySoftTracking = true;
                airborneYVelocity = 0f;
                peakLookAheadThisAir = 0f;
            }

            float vy = playerBody != null ? playerBody.linearVelocity.y : 0f;
            float fallBlend = ComputeFallBlend(vy);

            // MinSpeed 同时闸住「强跟」与「下瞄」：未达阈值时保持上升参数，避免一过顶点就像下瞄已触发
            float influence = Mathf.Lerp(
                Mathf.Clamp01(riseYInfluence),
                Mathf.Clamp01(fallYInfluence),
                fallBlend);
            float deltaY = playerPos.y - leaveGroundY;
            float lookAhead = Mathf.Max(0f, fallLookAheadMax) * fallBlend;
            activeLookAhead = lookAhead;
            if (lookAhead > peakLookAheadThisAir)
                peakLookAheadThisAir = lookAhead;
            float softTargetY = leaveGroundY + deltaY * influence - lookAhead;

            float smoothTime = Mathf.Max(
                0.01f,
                Mathf.Lerp(riseYSmoothTime, fallYSmoothTime, fallBlend));
            y = Mathf.SmoothDamp(y, softTargetY, ref airborneYVelocity, smoothTime, Mathf.Infinity, Time.deltaTime);
        }

        followAnchor.position = new Vector3(playerPos.x, y, playerPos.z);
        wasSolidGround = solid;
    }

    /// <summary>
    /// 0 = 未达下瞄阈值（含上升）；1 = 下落速度达到 FullSpeed。由 MinSpeed→FullSpeed 插值。
    /// </summary>
    float ComputeFallBlend(float velocityY)
    {
        if (velocityY >= 0f)
            return 0f;

        float fallSpeed = -velocityY;
        float minSpeed = Mathf.Max(0f, fallLookAheadMinSpeed);
        float fullSpeed = Mathf.Max(minSpeed + 0.01f, fallLookAheadFullSpeed);
        return Mathf.Clamp01(Mathf.InverseLerp(minSpeed, fullSpeed, fallSpeed));
    }

    void ClearLandSettle()
    {
        landSettling = false;
        landOffsetY = 0f;
        landOffsetVelocity = 0f;
    }

    void FollowPlayerFully()
    {
        if (playerTransform == null)
        {
            ResolveRefs();
            if (playerTransform == null)
                return;
        }

        EnsureFollowAnchor();
        followAnchor.position = playerTransform.position;
        leaveGroundY = playerTransform.position.y;
        activeLookAhead = 0f;
        peakLookAheadThisAir = 0f;
        ClearLandSettle();
        wasSolidGround = physicsCheck == null || physicsCheck.isSolidGround;
        hasGroundSample = true;
    }

    void SnapAnchorToPlayer(bool unlockY)
    {
        ResolveRefs();
        EnsureFollowAnchor();
        if (playerTransform == null)
            return;

        followAnchor.position = playerTransform.position;
        leaveGroundY = playerTransform.position.y;
        airborneYVelocity = 0f;
        activeLookAhead = 0f;
        peakLookAheadThisAir = 0f;
        ClearLandSettle();
        if (unlockY)
            ySoftTracking = false;
        wasSolidGround = physicsCheck == null || physicsCheck.isSolidGround;
    }

    void EnsureFollowAnchor()
    {
        if (followAnchor != null)
            return;

        var go = new GameObject("CameraFollowAnchor");
        followAnchor = go.transform;
        followAnchor.position = transform.position;
    }

    void ResolveRefs()
    {
        if (cameraControl == null)
            cameraControl = GetComponent<CameraControl>();

        if (afterSceneLoadEvent == null && cameraControl != null)
            afterSceneLoadEvent = cameraControl.afterSceneLoadEvent;

        Transform fromControl = cameraControl != null ? cameraControl.playerTransform : null;
        if (fromControl != null && fromControl != playerTransform)
        {
            playerTransform = fromControl;
            physicsCheck = null;
            playerBody = null;
            hasGroundSample = false;
        }

        if (playerTransform == null)
        {
            var go = GameObject.FindGameObjectWithTag("Player");
            if (go != null)
                playerTransform = go.transform;
        }

        if (physicsCheck == null && playerTransform != null)
            physicsCheck = playerTransform.GetComponent<PhysicsCheck>();

        if (playerBody == null && playerTransform != null)
            playerBody = playerTransform.GetComponent<Rigidbody2D>();
    }
}
