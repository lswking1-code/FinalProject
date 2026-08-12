using UnityEngine;

/// <summary>
/// 挂到带 Animator 的角色上，供 Animation Event 调用以触发 Recoil Impulse Shape 震屏。
/// </summary>
public class CameraRecoilShakeAnimEvent : MonoBehaviour
{
    [SerializeField] FloatEventSO cameraRecoilShakeEvent;
    [SerializeField, Tooltip("角色级力度倍率，最终 = Anim Event float × 此值")]
    float forceMultiplier = 1f;

    /// <summary>Animation Event：勾选 Float 参数，按动画填不同力度。</summary>
    public void TriggerCameraRecoilShake(float force)
    {
        if (cameraRecoilShakeEvent == null || force <= 0f)
            return;

        cameraRecoilShakeEvent.RaiseEvent(force * forceMultiplier);
    }
}
