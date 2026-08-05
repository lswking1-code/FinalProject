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

    /// <summary>Animation Event：float 为前冲时长（秒）。</summary>
    public void BeginAttackLunge(float duration)
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
}
