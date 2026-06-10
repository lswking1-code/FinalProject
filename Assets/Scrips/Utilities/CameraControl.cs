using Unity.Cinemachine;
using UnityEngine;

public class CameraControl : MonoBehaviour
{
    [Header("事件监听")]
    public VoidEventSO afterSceneLoadEvent;

    private CinemachineConfiner2D confiner2D;
    public CinemachineImpulseSource impulseSource;
    public VoidEventSO cameraShakeEvent;

    private void Awake()
    {
        confiner2D = GetComponent<CinemachineConfiner2D>();
    }

    private void OnEnable()
    {
        if (cameraShakeEvent != null)
            cameraShakeEvent.OnEventRaised += OnCameraShakeEvent;
        if (afterSceneLoadEvent != null)
            afterSceneLoadEvent.OnEventRaised += OnAfterSceneLoadEvent;
    }

    private void OnDisable()
    {
        if (cameraShakeEvent != null)
            cameraShakeEvent.OnEventRaised -= OnCameraShakeEvent;
        if (afterSceneLoadEvent != null)
            afterSceneLoadEvent.OnEventRaised -= OnAfterSceneLoadEvent;
    }

    private void OnAfterSceneLoadEvent()
    {
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
