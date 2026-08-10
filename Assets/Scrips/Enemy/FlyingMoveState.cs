using UnityEngine;

/// <summary>
/// 飞行敌人射击前走位：在头顶扇区内随机水平移动，Y 轴有界浮动，结束后进入射击。
/// </summary>
public class FlyingMoveState : BaseState
{
    FlyingEnemy flyingEnemy;
    float actionTimer;
    float moveDir;

    public override void OnEnter(Enemy enemy)
    {
        currentEnemy = enemy;
        flyingEnemy = enemy as FlyingEnemy;

        if (flyingEnemy == null)
            return;

        moveDir = Random.value < 0.5f ? -1f : 1f;
        actionTimer = flyingEnemy.actionDuration;
        currentEnemy.currentSpeed = currentEnemy.normalSpeed;
        flyingEnemy.SyncHoverBaseToPlayer(forceImmediate: true);
        moveDir = flyingEnemy.ClampMoveDirInsideFan(moveDir);

        if (currentEnemy.anim != null)
            currentEnemy.anim.SetBool("walk", true);

        // #region agent log
        try
        {
            string clip = "none";
            if (currentEnemy.anim != null && currentEnemy.anim.runtimeAnimatorController != null)
            {
                var clips = currentEnemy.anim.GetCurrentAnimatorClipInfo(0);
                if (clips != null && clips.Length > 0 && clips[0].clip != null)
                    clip = clips[0].clip.name;
            }
            long ts = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            string line = "{\"sessionId\":\"960d0c\",\"runId\":\"post-fix\",\"hypothesisId\":\"C\",\"location\":\"FlyingMoveState.OnEnter\",\"message\":\"set walk true\",\"data\":{\"walk\":true,\"clipBeforeTransition\":\"" + clip + "\"},\"timestamp\":" + ts + "}\n";
            string path = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "..", "debug-960d0c.log"));
            System.IO.File.AppendAllText(path, line);
        }
        catch { }
        // #endregion
    }

    public override void LogicUpdate()
    {
        if (flyingEnemy == null || currentEnemy.isDead)
            return;

        actionTimer -= Time.deltaTime;
        if (actionTimer <= 0f)
            flyingEnemy.SwitchState(NPCState.Shot);
    }

    public override void PhysicsUpdate()
    {
        if (flyingEnemy == null || currentEnemy.isHurt || currentEnemy.isDead)
            return;

        flyingEnemy.SyncHoverBaseToPlayer();
        moveDir = flyingEnemy.ClampMoveDirInsideFan(moveDir);
        flyingEnemy.ApplyHoverBob(moveDir, currentEnemy.currentSpeed);

        if (currentEnemy.TryFlipOnObstacle(moveDir))
            moveDir = -moveDir;

        if (moveDir > 0f)
            currentEnemy.transform.localScale = new Vector3(-1f, 1f, 1f);
        else if (moveDir < 0f)
            currentEnemy.transform.localScale = new Vector3(1f, 1f, 1f);
    }

    public override void OnExit()
    {
        if (currentEnemy?.anim != null)
            currentEnemy.anim.SetBool("walk", false);
    }
}
