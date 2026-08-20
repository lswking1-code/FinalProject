using UnityEngine;

/// <summary>
/// 挂在机器人 Visual（带 Animator）上，将 Animation Event 转发到父级 AllyRobot。
/// Animator 与 AllyRobot 不在同一物体时必须用此中继。
/// </summary>
public class AllyRobotAnimEvents : MonoBehaviour
{
    [SerializeField] AllyRobot robot;

    void Awake()
    {
        if (robot == null)
            robot = GetComponentInParent<AllyRobot>();
    }

    /// <summary>Animation Event：无参，使用 AllyRobot 默认前冲时长。</summary>
    public void BeginAttackLunge()
    {
        if (robot == null)
            robot = GetComponentInParent<AllyRobot>();
        robot?.BeginAttackLunge();
    }

    /// <summary>Animation Event：float 为前冲时长（秒）。不用 BeginAttackLunge 重载，避免 Unity Animation Event 警告。</summary>
    public void BeginAttackLungeTimed(float duration)
    {
        if (robot == null)
            robot = GetComponentInParent<AllyRobot>();
        robot?.BeginAttackLunge(duration);
    }

    /// <summary>Animation Event：立即结束前冲。</summary>
    public void EndAttackLunge()
    {
        if (robot == null)
            robot = GetComponentInParent<AllyRobot>();
        robot?.EndAttackLunge();
    }

    /// <summary>Animation Event：String 为 FMOD AttackType 标签（Normal/Combo/DashAttack/Blast1-3）。</summary>
    public void PlayAttackSfx(string attackType)
    {
        if (robot == null)
            robot = GetComponentInParent<AllyRobot>();
        robot?.PlayAttackSfx(attackType);
    }
}
