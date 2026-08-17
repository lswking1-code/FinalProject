using System.Collections;
using Unity.Cinemachine;
using UnityEngine;

public class CameraControl : MonoBehaviour
{
    [Header("跟随目标")]
    [Tooltip("留空则自动查找 Player 标签对象")]
    public Transform playerTransform;

    [Header("事件监听")]
    public VoidEventSO afterSceneLoadEvent;

    [Header("Bounds 切换平滑")]
    [Tooltip("切换相机边界时的过渡时长（秒）")]
    [SerializeField] float boundsTransitionDuration = 0.85f;
    [Tooltip("过渡期间 Confiner 阻尼，越大拉得越慢、越柔")]
    [SerializeField] float boundsTransitionDamping = 2f;
    [Tooltip("过渡期间 Confiner 减速距离")]
    [SerializeField] float boundsTransitionSlowingDistance = 8f;

    private CinemachineCamera cinemachineCamera;
    private CinemachineConfiner2D confiner2D;
    private CinemachinePositionComposer positionComposer;
    private CameraAirborneYLock airborneYLock;
    public CinemachineImpulseSource impulseSource;
    public FloatEventSO cameraShakeEvent;

    [Header("横向命中震屏（独立通道）")]
    [Tooltip("仅左右抖；方向由 horizontalImpulseSource.DefaultVelocity 决定")]
    public CinemachineImpulseSource horizontalImpulseSource;
    public FloatEventSO cameraHorizontalShakeEvent;

    [Header("Recoil Impulse Shape 震屏（独立通道）")]
    [Tooltip("默认 ImpulseShape = Recoil，可在 ImpulseSource 上调整")]
    public CinemachineImpulseSource recoilImpulseSource;
    public FloatEventSO cameraRecoilShakeEvent;

    Coroutine boundsTransitionRoutine;
    float cachedConfinerDamping;
    float cachedConfinerSlowingDistance;
    Vector3 cachedComposerDamping;
    bool hasCachedConfinerSettings;

    private void Awake()
    {
        cinemachineCamera = GetComponent<CinemachineCamera>();
        confiner2D = GetComponent<CinemachineConfiner2D>();
        EnsurePositionComposer();
        CacheConfinerDefaults();
    }

    private void OnEnable()
    {
        if (cameraShakeEvent != null)
            cameraShakeEvent.OnEventRaised += OnCameraShakeEvent;
        if (cameraHorizontalShakeEvent != null)
            cameraHorizontalShakeEvent.OnEventRaised += OnCameraHorizontalShakeEvent;
        if (cameraRecoilShakeEvent != null)
            cameraRecoilShakeEvent.OnEventRaised += OnCameraRecoilShakeEvent;
        if (afterSceneLoadEvent != null)
            afterSceneLoadEvent.OnEventRaised += OnAfterSceneLoadEvent;

        BindFollowTarget();
    }

    private void OnDisable()
    {
        if (cameraShakeEvent != null)
            cameraShakeEvent.OnEventRaised -= OnCameraShakeEvent;
        if (cameraHorizontalShakeEvent != null)
            cameraHorizontalShakeEvent.OnEventRaised -= OnCameraHorizontalShakeEvent;
        if (cameraRecoilShakeEvent != null)
            cameraRecoilShakeEvent.OnEventRaised -= OnCameraRecoilShakeEvent;
        if (afterSceneLoadEvent != null)
            afterSceneLoadEvent.OnEventRaised -= OnAfterSceneLoadEvent;

        StopBoundsTransition(restoreSettings: true);
    }

    private void EnsurePositionComposer()
    {
        positionComposer = GetComponent<CinemachinePositionComposer>();
        if (positionComposer != null)
            return;

        positionComposer = gameObject.AddComponent<CinemachinePositionComposer>();
        positionComposer.CameraDistance = 0f;
        positionComposer.Damping = new Vector3(0.1f, 0.1f, 0f);
    }

    void CacheConfinerDefaults()
    {
        if (confiner2D == null)
            return;

        cachedConfinerDamping = confiner2D.Damping;
        cachedConfinerSlowingDistance = confiner2D.SlowingDistance;
        if (positionComposer != null)
            cachedComposerDamping = positionComposer.Damping;
        hasCachedConfinerSettings = true;
    }

    public void SetFollowTarget(Transform target)
    {
        playerTransform = target;
        BindFollowTarget();
        SnapCameraToFollowTarget();
    }

    private void BindFollowTarget()
    {
        if (playerTransform == null)
        {
            var player = GameObject.FindGameObjectWithTag("Player");
            if (player != null)
                playerTransform = player.transform;
        }

        if (cinemachineCamera == null || playerTransform == null)
            return;

        if (airborneYLock == null)
            airborneYLock = GetComponent<CameraAirborneYLock>();

        Transform trackingTarget = playerTransform;
        if (airborneYLock != null && airborneYLock.isActiveAndEnabled)
        {
            Transform anchor = airborneYLock.FollowAnchor;
            if (anchor != null)
                trackingTarget = anchor;
        }

        cinemachineCamera.Target.TrackingTarget = trackingTarget;
    }

    /// <summary>
    /// 取消阻尼/预瞄插值，让相机立刻对齐当前跟随目标（切场景重绑时使用）。
    /// </summary>
    public void SnapCameraToFollowTarget()
    {
        if (airborneYLock == null)
            airborneYLock = GetComponent<CameraAirborneYLock>();

        if (airborneYLock != null && airborneYLock.isActiveAndEnabled)
            airborneYLock.SnapToPlayerImmediate();

        BindFollowTarget();

        if (cinemachineCamera == null)
            return;

        CinemachineCore.ResetCameraState();

        Vector3 worldUp = Vector3.up;
        CinemachineBrain brain = null;
        if (CinemachineBrain.ActiveBrainCount > 0)
        {
            brain = CinemachineBrain.GetActiveBrain(0);
            if (brain != null)
                worldUp = brain.DefaultWorldUp;
        }

        // deltaTime < 0 会跳过阻尼，且不受“本帧已更新”限制
        cinemachineCamera.UpdateCameraState(worldUp, -1f);

        if (brain != null && brain.OutputCamera != null)
        {
            var state = cinemachineCamera.State;
            brain.OutputCamera.transform.SetPositionAndRotation(
                state.GetFinalPosition(),
                state.GetFinalOrientation());
        }

        // 视差在场景 OnEnable 时可能已相对旧相机采基线；Snap 后再校准
        ParallaxLayer.RecalibrateAllToCamera();
    }

    private void OnAfterSceneLoadEvent()
    {
        BindFollowTarget();
        GetNewCameraBounds(smooth: false);
        SnapCameraToFollowTarget();
    }

    private void OnCameraShakeEvent(float force)
    {
        if (impulseSource == null || force <= 0f)
            return;

        impulseSource.GenerateImpulseWithForce(force);
    }

    private void OnCameraHorizontalShakeEvent(float force)
    {
        if (horizontalImpulseSource == null || force <= 0f)
            return;

        horizontalImpulseSource.GenerateImpulseWithForce(force);
    }

    private void OnCameraRecoilShakeEvent(float force)
    {
        if (recoilImpulseSource == null || force <= 0f)
            return;

        recoilImpulseSource.GenerateImpulseWithForce(force);
    }

    /// <summary>
    /// 将相机限制切换到指定碰撞体形状（如遭遇战区域 Bounds），默认平滑过渡。
    /// </summary>
    public void SetCameraBounds(Collider2D shape)
    {
        SetCameraBounds(shape, smooth: true);
    }

    public void SetCameraBounds(Collider2D shape, bool smooth)
    {
        if (confiner2D == null || shape == null)
            return;

        if (!smooth)
        {
            StopBoundsTransition(restoreSettings: true);
            ApplyBoundingShape(shape);
            return;
        }

        StartBoundsTransition(shape);
    }

    /// <summary>
    /// 恢复为场景级 tag 为 Bounds 的相机边界，默认平滑过渡。
    /// </summary>
    public void RestoreCameraBounds()
    {
        RestoreCameraBounds(smooth: true);
    }

    public void RestoreCameraBounds(bool smooth)
    {
        GetNewCameraBounds(smooth);
    }

    private void GetNewCameraBounds(bool smooth)
    {
        if (confiner2D == null)
            return;

        var obj = GameObject.FindGameObjectWithTag("Bounds");
        var shape = obj != null ? obj.GetComponent<Collider2D>() : null;
        // #region agent log
        string currentShape = confiner2D.BoundingShape2D != null ? confiner2D.BoundingShape2D.name : "null";
        AgentDebugLog.Write914("C", "CameraControl.cs:GetNewCameraBounds", "restore bounds lookup",
            "{\"sessionId\":\"914a21\",\"foundTag\":" + (obj != null ? "true" : "false") + ",\"hasCollider\":" + (shape != null ? "true" : "false") + ",\"currentShape\":\"" + currentShape + "\",\"smooth\":" + (smooth ? "true" : "false") + "}");
        // #endregion
        if (obj == null)
            return;

        if (shape == null)
            return;

        SetCameraBounds(shape, smooth);
    }

    void StartBoundsTransition(Collider2D shape)
    {
        StopBoundsTransition(restoreSettings: false);
        boundsTransitionRoutine = StartCoroutine(BoundsTransitionRoutine(shape));
    }

    void StopBoundsTransition(bool restoreSettings)
    {
        if (boundsTransitionRoutine != null)
        {
            StopCoroutine(boundsTransitionRoutine);
            boundsTransitionRoutine = null;
        }

        if (restoreSettings)
            RestoreTransitionSettings();
    }

    IEnumerator BoundsTransitionRoutine(Collider2D shape)
    {
        if (!hasCachedConfinerSettings)
            CacheConfinerDefaults();

        ApplyTransitionSettings();
        ApplyBoundingShape(shape);

        float duration = Mathf.Max(0.01f, boundsTransitionDuration);
        yield return new WaitForSeconds(duration);

        RestoreTransitionSettings();
        boundsTransitionRoutine = null;
    }

    void ApplyBoundingShape(Collider2D shape)
    {
        confiner2D.BoundingShape2D = shape;
        confiner2D.InvalidateBoundingShapeCache();
    }

    void ApplyTransitionSettings()
    {
        if (confiner2D != null)
        {
            confiner2D.Damping = Mathf.Max(cachedConfinerDamping, boundsTransitionDamping);
            confiner2D.SlowingDistance = Mathf.Max(cachedConfinerSlowingDistance, boundsTransitionSlowingDistance);
        }

        if (positionComposer != null)
        {
            positionComposer.Damping = new Vector3(
                Mathf.Max(cachedComposerDamping.x, 0.45f),
                Mathf.Max(cachedComposerDamping.y, 0.45f),
                cachedComposerDamping.z);
        }
    }

    void RestoreTransitionSettings()
    {
        if (!hasCachedConfinerSettings)
            return;

        if (confiner2D != null)
        {
            confiner2D.Damping = cachedConfinerDamping;
            confiner2D.SlowingDistance = cachedConfinerSlowingDistance;
        }

        if (positionComposer != null)
            positionComposer.Damping = cachedComposerDamping;
    }
}
