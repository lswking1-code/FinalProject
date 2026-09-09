using UnityEngine;

/// <summary>
/// 挂在机械师 idle_up（带 Animator）上，将 Animation Event 转发到父级 MachinistShooting。
/// Animator 与 MachinistShooting 不在同一物体时必须用此中继。
/// </summary>
public class MachinistAnimEvents : MonoBehaviour
{
    [SerializeField] MachinistShooting shooting;

    void Awake()
    {
        if (shooting == null)
            shooting = GetComponentInParent<MachinistShooting>();
    }

    /// <summary>Animation Event：String 为 FMOD MeleeTypeM 标签（Blast1/Blast2/Blast3/Explode）。</summary>
    public void PlayMeleeSfx(string meleeType)
    {
        if (shooting == null)
            shooting = GetComponentInParent<MachinistShooting>();
        shooting?.PlayMeleeSfx(meleeType);
    }
}
