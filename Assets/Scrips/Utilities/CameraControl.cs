using Unity.Cinemachine;
using UnityEngine;

public class CameraControl : MonoBehaviour
{
    [Header("跟随目标")]
    [Tooltip("留空则自动查找 Player 标签对象")]
    public Transform playerTransform;

    [Header("事件监听")]
    public VoidEventSO afterSceneLoadEvent;

    private CinemachineCamera cinemachineCamera;
    private CinemachineConfiner2D confiner2D;
    public CinemachineImpulseSource impulseSource;
    public VoidEventSO cameraShakeEvent;

    private void Awake()
    {
        cinemachineCamera = GetComponent<CinemachineCamera>();
        confiner2D = GetComponent<CinemachineConfiner2D>();
        EnsurePositionComposer();
    }

    private void OnEnable()
    {
        if (cameraShakeEvent != null)
            cameraShakeEvent.OnEventRaised += OnCameraShakeEvent;
        if (afterSceneLoadEvent != null)
            afterSceneLoadEvent.OnEventRaised += OnAfterSceneLoadEvent;

        BindFollowTarget();
    }

    private void OnDisable()
    {
        if (cameraShakeEvent != null)
            cameraShakeEvent.OnEventRaised -= OnCameraShakeEvent;
        if (afterSceneLoadEvent != null)
            afterSceneLoadEvent.OnEventRaised -= OnAfterSceneLoadEvent;
    }

    private void EnsurePositionComposer()
    {
        if (GetComponent<CinemachinePositionComposer>() != null)
            return;

        var composer = gameObject.AddComponent<CinemachinePositionComposer>();
        composer.CameraDistance = 0f;
        composer.Damping = new Vector3(0.1f, 0.1f, 0f);
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
        GetNewCameraBounds();
    }

    private void OnCameraShakeEvent()
    {
        if (impulseSource != null)
            impulseSource.GenerateImpulse();
    }

    private void GetNewCameraBounds()
    {
        if (confiner2D == null)
            return;

        var obj = GameObject.FindGameObjectWithTag("Bounds");
        if (obj == null)
            return;

        confiner2D.BoundingShape2D = obj.GetComponent<Collider2D>();
        confiner2D.InvalidateBoundingShapeCache();
    }
}
