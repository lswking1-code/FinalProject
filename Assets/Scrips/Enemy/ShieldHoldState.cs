using UnityEngine;

/// <summary>
/// 盾兵举盾对峙：原地停下，未面向玩家时延迟转身；
/// 玩家离开理想距离持续一段时间后重新追击（专注模式除外）。
/// enableShoot 时举盾满 holdDuration 后再掷射击。
/// </summary>
public class ShieldHoldState : BaseState
{
    ShieldEnemy shieldEnemy;
    float turnTimer;
    float holdTimer;

    public override void OnEnter(Enemy enemy)
    {
        currentEnemy = enemy;
        shieldEnemy = enemy as ShieldEnemy;
        turnTimer = 0f;
        holdTimer = 0f;

        currentEnemy.currentSpeed = 0f;
        currentEnemy.blockSeparation = true;
        if (currentEnemy.Rb != null)
            currentEnemy.Rb.linearVelocity = new Vector2(0f, currentEnemy.Rb.linearVelocity.y);

        if (currentEnemy.anim != null)
        {
            currentEnemy.SetAnimBool("walk", false);
            currentEnemy.SetAnimBool("melee", false);
            currentEnemy.SetAnimBool("meleeWindup", false);
            currentEnemy.SetAnimBool("shoot", false);
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

        if (TryRerollShoot())
            return;

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
        holdTimer = 0f;
    }

    bool TryRerollShoot()
    {
        if (!shieldEnemy.enableShoot)
            return false;

        holdTimer += Time.deltaTime;
        if (holdTimer < shieldEnemy.holdDuration || shieldEnemy.IsShootOnCooldown)
            return false;

        holdTimer = 0f;
        shieldEnemy.EvaluateCycle();
        return true;
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
