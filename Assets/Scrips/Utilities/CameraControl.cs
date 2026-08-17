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

    [Header("镜头过渡")]
    [Tooltip("切换相机边界 / Orthographic Size 时的过渡时长（秒）")]
    [SerializeField] float boundsTransitionDuration = 1f;
    [Tooltip("过渡缓动，默认 EaseInOut")]
    [SerializeField] AnimationCurve boundsTransitionCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
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
    Coroutine parallaxAlignRoutine;
    float cachedConfinerDamping;
    float cachedConfinerSlowingDistance;
    Vector3 cachedComposerDamping;
    bool hasCachedConfinerSettings;
    float defaultOrthographicSize;
    bool hasCachedDefaultOrthographicSize;
    BoxCollider2D transitionBoundsCollider;

    private void Awake()
    {
        cinemachineCamera = GetComponent<CinemachineCamera>();
        confiner2D = GetComponent<CinemachineConfiner2D>();
        EnsurePositionComposer();
        CacheConfinerDefaults();
        CacheDefaultOrthographicSize();
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
        StopParallaxAlignRoutine();
    }

    void OnDestroy()
    {
        if (transitionBoundsCollider != null)
            Destroy(transitionBoundsCollider.gameObject);
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
        SnapCameraToFollowTargetImmediate();
        ScheduleParallaxAlignAfterCameraSettles();
    }

    void SnapCameraToFollowTargetImmediate(bool recalibrateParallax = false)
    {
        if (airborneYLock == null)
            airborneYLock = GetComponent<CameraAirborneYLock>();

        if (airborneYLock != null && airborneYLock.isActiveAndEnabled)
            airborneYLock.SnapToPlayerImmediate();

        BindFollowTarget();

        if (cinemachineCamera == null)
            return;

        float prevConfinerDamping = 0f;
        float prevConfinerSlowing = 0f;
        Vector3 prevComposerDamping = Vector3.zero;
        bool restoreConfiner = false;
        bool restoreComposer = false;

        // 遭遇战读档时 Confiner 可能仍带着过渡阻尼，不先清零就 Snap 不准
        if (confiner2D != null)
        {
            prevConfinerDamping = confiner2D.Damping;
            prevConfinerSlowing = confiner2D.SlowingDistance;
            confiner2D.Damping = 0f;
            confiner2D.SlowingDistance = 0f;
            confiner2D.InvalidateBoundingShapeCache();
            restoreConfiner = true;
        }

        if (positionComposer != null)
        {
            prevComposerDamping = positionComposer.Damping;
            positionComposer.Damping = Vector3.zero;
            restoreComposer = true;
        }

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

        var snappedState = cinemachineCamera.State;
        Vector3 snappedPos = snappedState.GetFinalPosition();
        Quaternion snappedRot = snappedState.GetFinalOrientation();
        // 把 Composer 内部上一帧位置一并拽过来，避免随后 LateUpdate 从坑底阻尼回来
        cinemachineCamera.ForceCameraPosition(snappedPos, snappedRot);

        if (brain != null && brain.OutputCamera != null)
        {
            brain.OutputCamera.transform.SetPositionAndRotation(snappedPos, snappedRot);
        }

        if (restoreConfiner)
        {
            confiner2D.Damping = prevConfinerDamping;
            confiner2D.SlowingDistance = prevConfinerSlowing;
        }

        if (restoreComposer)
            positionComposer.Damping = prevComposerDamping;

        if (recalibrateParallax)
            ParallaxLayer.RecalibrateAllToCamera();
    }

    void ScheduleParallaxAlignAfterCameraSettles()
    {
        if (!isActiveAndEnabled)
            return;

        StopParallaxAlignRoutine();
        parallaxAlignRoutine = StartCoroutine(ParallaxAlignAfterCameraSettlesRoutine());
    }

    void StopParallaxAlignRoutine()
    {
        if (parallaxAlignRoutine == null)
            return;

        StopCoroutine(parallaxAlignRoutine);
        parallaxAlignRoutine = null;
    }

    IEnumerator ParallaxAlignAfterCameraSettlesRoutine()
    {
        // 先只把镜头钉住，不要每帧 Recalibrate：那会把背景锁在原点，视差彻底停住。
        const int snapFrames = 8;
        for (int i = 0; i < snapFrames; i++)
        {
            SnapCameraToFollowTargetImmediate(recalibrateParallax: false);
            yield return null;
        }

        SnapCameraToFollowTargetImmediate(recalibrateParallax: true);
        parallaxAlignRoutine = null;
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
    /// 不覆盖 Orthographic Size 时插值回默认 Size。
    /// </summary>
    public void SetCameraBounds(Collider2D shape)
    {
        SetCameraBounds(shape, smooth: true);
    }

    public void SetCameraBounds(Collider2D shape, bool smooth)
    {
        SetCameraBounds(shape, smooth, overrideOrthographicSize: false, orthographicSize: 0f);
    }

    public void SetCameraBounds(Collider2D shape, bool smooth, bool overrideOrthographicSize, float orthographicSize)
    {
        if (confiner2D == null || shape == null)
            return;

        if (!hasCachedDefaultOrthographicSize)
            CacheDefaultOrthographicSize();

        float targetSize = overrideOrthographicSize
            ? Mathf.Max(0.01f, orthographicSize)
            : GetDefaultOrthographicSize();

        if (!smooth)
        {
            ApplyCameraBoundsImmediate(shape, targetSize);
            return;
        }

        StartBoundsTransition(shape, targetSize);
    }

    /// <summary>
    /// 恢复为场景级 tag 为 Bounds 的相机边界，并回到默认 Orthographic Size。
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
        var obj = GameObject.FindGameObjectWithTag("Bounds");
        var shape = obj != null ? obj.GetComponent<Collider2D>() : null;
        if (shape == null)
        {
            if (!smooth)
                SetOrthographicSize(GetDefaultOrthographicSize());
            return;
        }

        SetCameraBounds(shape, smooth, overrideOrthographicSize: false, orthographicSize: 0f);
    }

    void StartBoundsTransition(Collider2D shape, float targetSize)
    {
        StopBoundsTransition(restoreSettings: false);
        boundsTransitionRoutine = StartCoroutine(BoundsTransitionRoutine(shape, targetSize));
    }

    void StopBoundsTransition(bool restoreSettings)
    {
        if (boundsTransitionRoutine != null)
        {
            StopCoroutine(boundsTransitionRoutine);
            boundsTransitionRoutine = null;
        }

        if (restoreSettings)
        {
            RestoreTransitionSettings();
            DisableTransitionBoundsCollider();
        }
    }

    void ApplyCameraBoundsImmediate(Collider2D shape, float targetSize)
    {
        StopBoundsTransition(restoreSettings: true);
        SetOrthographicSize(targetSize);
        ApplyBoundingShape(shape);
    }

    IEnumerator BoundsTransitionRoutine(Collider2D shape, float targetSize)
    {
        if (!hasCachedConfinerSettings)
            CacheConfinerDefaults();

        Bounds startBounds = GetTransitionStartBounds();
        Bounds endBounds = shape.bounds;
        float startSize = GetCurrentOrthographicSize();

        ApplyTransitionSettings();
        ApplyTransitionBounds(startBounds);
        ApplyBoundingShape(transitionBoundsCollider);
        SetOrthographicSize(startSize);

        float duration = Mathf.Max(0.01f, boundsTransitionDuration);
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = EvaluateTransition(elapsed / duration);
            ApplyTransitionBounds(LerpBounds(startBounds, endBounds, t));
            SetOrthographicSize(Mathf.Lerp(startSize, targetSize, t));
            if (confiner2D != null)
                confiner2D.InvalidateBoundingShapeCache();
            yield return null;
        }

        SetOrthographicSize(targetSize);
        ApplyBoundingShape(shape);
        DisableTransitionBoundsCollider();
        RestoreTransitionSettings();
        boundsTransitionRoutine = null;
    }

    void ApplyBoundingShape(Collider2D shape)
    {
        if (confiner2D == null || shape == null)
            return;

        confiner2D.BoundingShape2D = shape;
        confiner2D.InvalidateBoundingShapeCache();
    }

    void CacheDefaultOrthographicSize()
    {
        if (cinemachineCamera == null)
            return;

        defaultOrthographicSize = Mathf.Max(0.01f, cinemachineCamera.Lens.OrthographicSize);
        hasCachedDefaultOrthographicSize = true;
    }

    float GetDefaultOrthographicSize()
    {
        if (!hasCachedDefaultOrthographicSize)
            CacheDefaultOrthographicSize();
        return hasCachedDefaultOrthographicSize
            ? defaultOrthographicSize
            : GetCurrentOrthographicSize();
    }

    float GetCurrentOrthographicSize()
    {
        if (cinemachineCamera != null)
            return cinemachineCamera.Lens.OrthographicSize;

        Camera output = GetOutputCamera();
        if (output != null)
            return output.orthographicSize;

        return hasCachedDefaultOrthographicSize ? defaultOrthographicSize : 1f;
    }

    void SetOrthographicSize(float size)
    {
        size = Mathf.Max(0.01f, size);

        if (cinemachineCamera != null)
        {
            var lens = cinemachineCamera.Lens;
            lens.OrthographicSize = size;
            cinemachineCamera.Lens = lens;
        }

        Camera output = GetOutputCamera();
        if (output != null)
            output.orthographicSize = size;
    }

    Camera GetOutputCamera()
    {
        if (CinemachineBrain.ActiveBrainCount <= 0)
            return null;

        CinemachineBrain brain = CinemachineBrain.GetActiveBrain(0);
        return brain != null ? brain.OutputCamera : null;
    }

    Bounds GetTransitionStartBounds()
    {
        if (IsUsingTransitionBounds())
            return transitionBoundsCollider.bounds;

        return GetCurrentCameraWorldBounds();
    }

    bool IsUsingTransitionBounds()
    {
        return transitionBoundsCollider != null
            && transitionBoundsCollider.gameObject.activeSelf
            && confiner2D != null
            && confiner2D.BoundingShape2D == transitionBoundsCollider;
    }

    Bounds GetCurrentCameraWorldBounds()
    {
        float ortho = GetCurrentOrthographicSize();
        float height = ortho * 2f;
        float aspect = GetCameraAspect();
        Vector3 center = GetCameraWorldPosition();
        return new Bounds(
            new Vector3(center.x, center.y, 0f),
            new Vector3(height * aspect, height, 1f));
    }

    Vector3 GetCameraWorldPosition()
    {
        Camera output = GetOutputCamera();
        if (output != null)
            return output.transform.position;

        if (cinemachineCamera != null)
            return cinemachineCamera.State.GetFinalPosition();

        return transform.position;
    }

    float GetCameraAspect()
    {
        Camera output = GetOutputCamera();
        if (output != null && output.aspect > 0.01f)
            return output.aspect;

        return Screen.height > 0 ? (float)Screen.width / Screen.height : 16f / 9f;
    }

    void EnsureTransitionBoundsCollider()
    {
        if (transitionBoundsCollider != null)
            return;

        var go = new GameObject("CameraBoundsTransition");
        go.hideFlags = HideFlags.HideAndDontSave;
        go.layer = 2;
        if (gameObject.scene.IsValid())
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, gameObject.scene);

        transitionBoundsCollider = go.AddComponent<BoxCollider2D>();
        transitionBoundsCollider.isTrigger = true;
        go.SetActive(false);
    }

    void ApplyTransitionBounds(Bounds worldBounds)
    {
        EnsureTransitionBoundsCollider();

        var boxTransform = transitionBoundsCollider.transform;
        boxTransform.SetPositionAndRotation(
            new Vector3(worldBounds.center.x, worldBounds.center.y, 0f),
            Quaternion.identity);
        boxTransform.localScale = Vector3.one;

        transitionBoundsCollider.offset = Vector2.zero;
        transitionBoundsCollider.size = new Vector2(
            Mathf.Max(0.01f, worldBounds.size.x),
            Mathf.Max(0.01f, worldBounds.size.y));

        if (!transitionBoundsCollider.gameObject.activeSelf)
            transitionBoundsCollider.gameObject.SetActive(true);
    }

    void DisableTransitionBoundsCollider()
    {
        if (transitionBoundsCollider == null)
            return;

        if (confiner2D != null && confiner2D.BoundingShape2D == transitionBoundsCollider)
            confiner2D.BoundingShape2D = null;

        transitionBoundsCollider.gameObject.SetActive(false);
    }

    static Bounds LerpBounds(Bounds from, Bounds to, float t)
    {
        return new Bounds(
            Vector3.Lerp(from.center, to.center, t),
            Vector3.Lerp(from.size, to.size, t));
    }

    float EvaluateTransition(float normalizedTime)
    {
        float t = Mathf.Clamp01(normalizedTime);
        if (boundsTransitionCurve == null || boundsTransitionCurve.length == 0)
            return t;
        return Mathf.Clamp01(boundsTransitionCurve.Evaluate(t));
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
