using UnityEngine;

/// <summary>
/// 飞行敌人射击：按相对位置选择向下或斜向单发，射击时静止，随后进入原地后摇。
/// </summary>
public class FlyingShotState : BaseState
{
    FlyingEnemy flyingEnemy;
    bool firingDown;

    public override void OnEnter(Enemy enemy)
    {
        currentEnemy = enemy;
        flyingEnemy = enemy as FlyingEnemy;

        if (flyingEnemy == null)
            return;

        flyingEnemy.FacePlayer();
        flyingEnemy.StopHorizontalMotion();

        firingDown = flyingEnemy.ShouldFireDown();

        if (currentEnemy.anim != null)
        {
            currentEnemy.SetAnimBool("walk", false);
            currentEnemy.SetAnimBool("shoot", !firingDown);
            currentEnemy.SetAnimBool("shootDown", firingDown);
        }

        // #region agent log
        try
        {
            long ts = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            string line = "{\"sessionId\":\"960d0c\",\"runId\":\"post-fix\",\"hypothesisId\":\"D\",\"location\":\"FlyingShotState.OnEnter\",\"message\":\"set shoot params\",\"data\":{\"firingDown\":" + (firingDown ? "true" : "false") + ",\"shoot\":" + (!firingDown ? "true" : "false") + ",\"shootDown\":" + (firingDown ? "true" : "false") + "},\"timestamp\":" + ts + "}\n";
            string path = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "..", "debug-960d0c.log"));
            System.IO.File.AppendAllText(path, line);
        }
        catch { }
        // #endregion

        if (firingDown)
            flyingEnemy.FireDownProjectile();
        else
            flyingEnemy.FireDiagonalProjectile();
    }

    public override void LogicUpdate()
    {
        if (flyingEnemy == null || currentEnemy.isDead)
            return;

        flyingEnemy.SwitchState(NPCState.Reload);
    }

    public override void PhysicsUpdate()
    {
        if (flyingEnemy == null || currentEnemy.isHurt || currentEnemy.isDead)
            return;

        flyingEnemy.StopHorizontalMotion();
    }

    public override void OnExit()
    {
        if (currentEnemy?.anim == null)
            return;

        currentEnemy.SetAnimBool("shoot", false);
        currentEnemy.SetAnimBool("shootDown", false);
    }
}
