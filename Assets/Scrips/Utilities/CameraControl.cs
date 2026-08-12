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
    }

    private void BindFollowTarget()
    {
        if (playerTransform == null)
        {
            var player = GameObject.FindGameObjectWithTag("Player");
            if (player != null)
                playerTransform = player.transform;
        }

        if (cinemachineCamera != null && playerTransform != null)
            cinemachineCamera.Target.TrackingTarget = playerTransform;
    }

    private void OnAfterSceneLoadEvent()
    {
        BindFollowTarget();
        GetNewCameraBounds(smooth: false);
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
        if (obj == null)
            return;

        var shape = obj.GetComponent<Collider2D>();
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
