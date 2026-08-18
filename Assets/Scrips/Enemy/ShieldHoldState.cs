using UnityEngine;

/// <summary>
/// 盾兵举盾对峙：原地停下，未面向玩家时延迟转身；
/// 玩家离开理想距离持续一段时间后重新追击（专注模式除外）。
/// </summary>
public class ShieldHoldState : BaseState
{
    ShieldEnemy shieldEnemy;
    float turnTimer;

    public override void OnEnter(Enemy enemy)
    {
        currentEnemy = enemy;
        shieldEnemy = enemy as ShieldEnemy;
        turnTimer = 0f;

        currentEnemy.currentSpeed = 0f;
        currentEnemy.blockSeparation = true;
        if (currentEnemy.Rb != null)
            currentEnemy.Rb.linearVelocity = new Vector2(0f, currentEnemy.Rb.linearVelocity.y);

        if (currentEnemy.anim != null)
        {
            currentEnemy.anim.SetBool("walk", false);
            currentEnemy.anim.SetBool("melee", false);
            currentEnemy.anim.SetBool("meleeWindup", false);
        }
    }

    public override void LogicUpdate()
    {
        if (shieldEnemy == null || currentEnemy.isDead)
            return;

        if (!shieldEnemy.HasShield)
        {
            shieldEnemy.EvaluateCycle();
            return;
        }

        if (!shieldEnemy.enableFocusMode && shieldEnemy.TickReapproachDelay())
        {
            shieldEnemy.SwitchState(NPCState.GetClose);
            return;
        }

        if (IsFacingPlayer())
        {
            turnTimer = 0f;
            return;
        }

        turnTimer += Time.deltaTime;
        if (turnTimer >= shieldEnemy.faceTurnDelay)
        {
            currentEnemy.FacePlayer();
            turnTimer = 0f;
        }
    }

    public override void PhysicsUpdate()
    {
        if (currentEnemy == null || currentEnemy.Rb == null)
            return;

        currentEnemy.Rb.linearVelocity = new Vector2(0f, currentEnemy.Rb.linearVelocity.y);
    }

    public override void OnExit()
    {
        if (currentEnemy != null)
            currentEnemy.blockSeparation = false;
        turnTimer = 0f;
    }

    bool IsFacingPlayer()
    {
        currentEnemy.EnsurePlayerReference();
        if (currentEnemy.player == null)
            return true;

        float dx = currentEnemy.player.position.x - currentEnemy.transform.position.x;
        if (Mathf.Abs(dx) < 0.01f)
            return true;

        return Mathf.Sign(dx) * currentEnemy.faceDir.x > 0f;
    }
}
