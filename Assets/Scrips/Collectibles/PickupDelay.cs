using UnityEngine;

/// <summary>
/// 运行时生成的掉落物拾取锁定。默认未武装，场景摆放实例立即可捡。
/// </summary>
public class PickupDelay : MonoBehaviour
{
    [SerializeField] float duration = 0.6f;

    float unlockTime;
    bool armed;

    public bool IsLocked => armed && Time.time < unlockTime;

    public void Arm()
    {
        armed = true;
        unlockTime = Time.time + duration;
    }

    public static void Arm(GameObject instance)
    {
        if (instance == null)
            return;

        var delay = instance.GetComponent<PickupDelay>();
        if (delay != null)
            delay.Arm();
    }
}
