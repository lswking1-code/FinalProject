using UnityEngine;

/// <summary>
/// 直升机刷完离场：垂直向上飞离，延迟后销毁自身。
/// </summary>
public class HelicopterDepartState : BaseState
{
    HelicopterEnemy helicopter;
    float destroyTimer;

    public override void OnEnter(Enemy enemy)
    {
        currentEnemy = enemy;
        helicopter = enemy as HelicopterEnemy;
        destroyTimer = 0f;

        if (helicopter == null)
            return;

        helicopter.BeginDepart();
        helicopter.StopSummonAttack();

        if (currentEnemy.anim != null)
        {
            currentEnemy.SetAnimBool("walk", false);
            currentEnemy.SetAnimBool("shoot", false);
            currentEnemy.SetAnimBool("shootDown", false);
        }
    }

    public override void LogicUpdate()
    {
        if (helicopter == null || currentEnemy.isDead)
            return;

        destroyTimer += Time.deltaTime;
        if (destroyTimer >= helicopter.DepartDestroyDelay)
            helicopter.FinishDepartDestroy();
    }

    public override void PhysicsUpdate()
    {
        if (helicopter == null || currentEnemy.isDead)
            return;

        helicopter.ApplyDepartAscent();
    }

    public override void OnExit()
    {
        helicopter?.EndDepart();
    }
}
