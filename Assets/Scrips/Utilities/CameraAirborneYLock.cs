using UnityEngine;

/// <summary>
/// 挂在 Cinemachine 相机上：离地时锚点 Y 仅轻微跟随玩家，落地后完整跟随并由 Composer 阻尼平滑追上。
/// </summary>
public class CameraAirborneYLock : MonoBehaviour
{
    [Header("开关")]
    [Tooltip("关闭后锚点始终完整跟随玩家（仍作为 TrackingTarget）")]
    [SerializeField] bool lockYWhileAirborne = true;

    [Header("空中 Y 轻微跟随")]
    [Tooltip("相对离地高度，跟随玩家竖直位移的比例。0=完全冻结，1=完整跟随")]
    [Range(0f, 1f)]
    [SerializeField] float airborneYInfluence = 0.22f;
    [Tooltip("空中锚点 Y 追上软目标的平滑时间（秒），越大越柔")]
    [SerializeField] float airborneYSmoothTime = 0.18f;

    [Header("引用")]
    [Tooltip("留空则使用同物体上的 CameraControl")]
    [SerializeField] CameraControl cameraControl;
    [Tooltip("留空则从玩家解析 PhysicsCheck")]
    [SerializeField] PhysicsCheck physicsCheck;
    [Tooltip("切场景后重绑；留空则尝试使用 CameraControl 上的同名事件")]
    public VoidEventSO afterSceneLoadEvent;

    Transform followAnchor;
    Transform playerTransform;
    bool ySoftTracking;
    bool wasSolidGround = true;
    bool hasGroundSample;
    float leaveGroundY;
    float airborneYVelocity;

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
            wasSolidGround = solid;
            hasGroundSample = true;
            return;
        }

        float y = followAnchor.position.y;

        if (solid)
        {
            y = playerPos.y;
            ySoftTracking = false;
            airborneYVelocity = 0f;
            leaveGroundY = playerPos.y;
        }
        else
        {
            if (!ySoftTracking)
            {
                // 刚离地：记录离地高度；开局已在空中则用当前高度
                leaveGroundY = wasSolidGround ? followAnchor.position.y : playerPos.y;
                ySoftTracking = true;
                airborneYVelocity = 0f;
            }

            float influence = Mathf.Clamp01(airborneYInfluence);
            float softTargetY = leaveGroundY + (playerPos.y - leaveGroundY) * influence;
            float smoothTime = Mathf.Max(0.01f, airborneYSmoothTime);
            y = Mathf.SmoothDamp(y, softTargetY, ref airborneYVelocity, smoothTime, Mathf.Infinity, Time.deltaTime);
        }

        followAnchor.position = new Vector3(playerPos.x, y, playerPos.z);
        wasSolidGround = solid;
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
    }
}
