using UnityEngine;

/// <summary>
/// 挂到带 Animator 的角色上，供 Animation Event 调用以触发震屏。
/// </summary>
public class CameraShakeAnimEvent : MonoBehaviour
{
    [SerializeField] FloatEventSO cameraShakeEvent;
    [SerializeField, Tooltip("角色级力度倍率，最终 = Anim Event float × 此值")]
    float forceMultiplier = 1f;

    /// <summary>Animation Event：勾选 Float 参数，按动画填不同力度。</summary>
    public void TriggerCameraShake(float force)
    {
        if (cameraShakeEvent == null || force <= 0f)
            return;

        cameraShakeEvent.RaiseEvent(force * forceMultiplier);
    }
}
