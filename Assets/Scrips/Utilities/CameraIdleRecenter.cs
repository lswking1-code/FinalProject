using System.Reflection;
using Unity.Cinemachine;
using UnityEngine;

/// <summary>
/// 挂在 Cinemachine 相机上：玩家速度接近 0 时仅对 X 轴关闭死区并略增阻尼，平滑横向回中。
/// Y 轴由 CameraAirborneYLock 负责，本组件不改动 Y 阻尼 / Y 死区。
/// </summary>
[RequireComponent(typeof(CinemachinePositionComposer))]
public class CameraIdleRecenter : MonoBehaviour
{
    static readonly FieldInfo PredictorField = typeof(CinemachinePositionComposer).GetField(
        "m_Predictor", BindingFlags.Instance | BindingFlags.NonPublic);

    [Header("引用")]
    [Tooltip("留空则使用同物体上的 CameraControl.playerTransform，或查找 Player 标签")]
    [SerializeField] CameraControl cameraControl;
    [Tooltip("留空则运行时自动解析")]
    [SerializeField] Rigidbody2D playerBody;
    [Tooltip("留空则使用同物体上的 PositionComposer")]
    [SerializeField] CinemachinePositionComposer positionComposer;
    [Tooltip("切场景后重新绑定玩家；留空则尝试使用 CameraControl 上的同名事件")]
    public VoidEventSO afterSceneLoadEvent;

    [Header("静止镜头回中（仅 X）")]
    [SerializeField] bool recenterCameraWhenIdle = true;
    [Tooltip("判定为静止的速度阈值（世界单位/秒）")]
    [SerializeField] float velocityIdleThreshold = 0.05f;
    [Tooltip("回中期间额外增大的 X 阻尼，越大越柔；不作用于 Y")]
    [SerializeField] float idleExtraDamping = 0.4f;
    [Tooltip("回中时暂时关闭 Lookahead，避免速度预测拖着镜头不回中")]
    [SerializeField] bool disableLookaheadWhileIdle = true;

    bool idleRecentering;
    bool wasMoving = true;

    Vector2 restingScreenPosition;
    Vector2 cachedDeadZoneSize;
    bool cachedDeadZoneEnabled;
    Vector3 cachedDamping;
    bool cachedLookaheadEnabled;
    float cachedLookaheadTime;
    float cachedLookaheadSmoothing;
    bool cachedLookaheadIgnoreY;
    bool hasComposerCache;

    void Awake()
    {
        ResolveRefs();
        CacheComposerDefaults();
    }

    void OnEnable()
    {
        ResolveRefs();
        if (afterSceneLoadEvent != null)
            afterSceneLoadEvent.OnEventRaised += OnAfterSceneLoad;

        CacheComposerDefaults();
        EndIdleRecenter(restoreImmediate: true);
        wasMoving = true;
    }

    void OnDisable()
    {
        if (afterSceneLoadEvent != null)
            afterSceneLoadEvent.OnEventRaised -= OnAfterSceneLoad;

        EndIdleRecenter(restoreImmediate: true);
    }

    void OnAfterSceneLoad()
    {
        playerBody = null;
        ResolveRefs();
        EndIdleRecenter(restoreImmediate: true);
        ResetLookaheadPredictor();
        wasMoving = true;
    }

    void LateUpdate()
    {
        if (playerBody == null)
        {
            ResolveRefs();
            if (playerBody == null)
                return;
        }

        bool moving = IsPlayerMoving();

        if (!recenterCameraWhenIdle)
        {
            if (idleRecentering)
                EndIdleRecenter(restoreImmediate: true);
            wasMoving = moving;
            return;
        }

        if (moving)
        {
            if (idleRecentering)
                EndIdleRecenter(restoreImmediate: true);
        }
        else if (wasMoving || !idleRecentering)
        {
            BeginIdleRecenter();
        }

        wasMoving = moving;
    }

    bool IsPlayerMoving()
    {
        float threshold = Mathf.Max(0f, velocityIdleThreshold);
        return playerBody.linearVelocity.sqrMagnitude > threshold * threshold;
    }

    void BeginIdleRecenter()
    {
        CacheComposerDefaults();
        idleRecentering = true;

        if (positionComposer == null || !hasComposerCache)
            return;

        var composition = positionComposer.Composition;
        // 仅压掉 X 死区以横向回中；Y 死区保持原样，交给 CameraAirborneYLock
        composition.ScreenPosition = new Vector2(restingScreenPosition.x, composition.ScreenPosition.y);

        var deadZone = composition.DeadZone;
        deadZone.Enabled = true;
        deadZone.Size = new Vector2(
            0f,
            cachedDeadZoneEnabled ? cachedDeadZoneSize.y : 0f);
        composition.DeadZone = deadZone;
        positionComposer.Composition = composition;

        positionComposer.Damping = new Vector3(
            cachedDamping.x + idleExtraDamping,
            cachedDamping.y,
            cachedDamping.z);

        if (disableLookaheadWhileIdle)
        {
            var lookahead = positionComposer.Lookahead;
            lookahead.Enabled = false;
            positionComposer.Lookahead = lookahead;
        }
    }

    void EndIdleRecenter(bool restoreImmediate)
    {
        idleRecentering = false;
        if (!restoreImmediate || positionComposer == null || !hasComposerCache)
            return;

        var composition = positionComposer.Composition;
        composition.ScreenPosition = new Vector2(restingScreenPosition.x, composition.ScreenPosition.y);

        var deadZone = composition.DeadZone;
        deadZone.Enabled = cachedDeadZoneEnabled;
        deadZone.Size = cachedDeadZoneSize;
        composition.DeadZone = deadZone;
        positionComposer.Composition = composition;

        positionComposer.Damping = new Vector3(cachedDamping.x, cachedDamping.y, cachedDamping.z);

        var lookahead = positionComposer.Lookahead;
        lookahead.Enabled = cachedLookaheadEnabled;
        lookahead.Time = cachedLookaheadTime;
        lookahead.Smoothing = cachedLookaheadSmoothing;
        lookahead.IgnoreY = cachedLookaheadIgnoreY;
        positionComposer.Lookahead = lookahead;

        if (disableLookaheadWhileIdle && cachedLookaheadEnabled)
            ResetLookaheadPredictor();
    }

    void ResetLookaheadPredictor()
    {
        if (positionComposer == null || PredictorField == null)
            return;

        var predictor = (PositionPredictor)PredictorField.GetValue(positionComposer);
        predictor.Reset();
        PredictorField.SetValue(positionComposer, predictor);
    }

    void ResolveRefs()
    {
        if (positionComposer == null)
            positionComposer = GetComponent<CinemachinePositionComposer>();

        if (cameraControl == null)
            cameraControl = GetComponent<CameraControl>();

        if (afterSceneLoadEvent == null && cameraControl != null)
            afterSceneLoadEvent = cameraControl.afterSceneLoadEvent;

        if (playerBody == null)
        {
            Transform player = cameraControl != null ? cameraControl.playerTransform : null;
            if (player == null)
            {
                var go = GameObject.FindGameObjectWithTag("Player");
                if (go != null)
                    player = go.transform;
            }

            if (player != null)
                playerBody = player.GetComponent<Rigidbody2D>();
        }
    }

    void CacheComposerDefaults()
    {
        if (positionComposer == null || hasComposerCache)
            return;

        restingScreenPosition = positionComposer.Composition.ScreenPosition;

        var deadZone = positionComposer.Composition.DeadZone;
        cachedDeadZoneSize = deadZone.Size;
        cachedDeadZoneEnabled = deadZone.Enabled;
        cachedDamping = positionComposer.Damping;

        var lookahead = positionComposer.Lookahead;
        cachedLookaheadEnabled = lookahead.Enabled;
        cachedLookaheadTime = lookahead.Time;
        cachedLookaheadSmoothing = lookahead.Smoothing;
        cachedLookaheadIgnoreY = lookahead.IgnoreY;

        hasComposerCache = true;
    }
}
