using UnityEngine;

/// <summary>
/// 通关后冻结敌方战斗：停 AI、弹药、爆炸与刷怪。不用 Time.timeScale。
/// </summary>
public static class GameplayHold
{
    public static bool IsHeld { get; private set; }

    public static void Hold()
    {
        if (IsHeld)
            return;

        IsHeld = true;
        FreezeEnemies();
        FreezeProjectiles();
        StopSpawners();
    }

    public static void Release()
    {
        IsHeld = false;
    }

    static void FreezeEnemies()
    {
        var enemies = Object.FindObjectsByType<Enemy>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < enemies.Length; i++)
        {
            var enemy = enemies[i];
            if (enemy == null || !enemy.isActiveAndEnabled)
                continue;

            FreezeRigidbody(enemy.Rb);
            if (enemy.anim != null)
                enemy.anim.speed = 0f;

            DisableAttacks(enemy.gameObject);
            enemy.StopAllCoroutines();
            enemy.enabled = false;
        }
    }

    static void FreezeProjectiles()
    {
        FreezeBehaviours(Object.FindObjectsByType<EnemyProjectile>(FindObjectsInactive.Exclude, FindObjectsSortMode.None));
        FreezeBehaviours(Object.FindObjectsByType<EnemyMissile>(FindObjectsInactive.Exclude, FindObjectsSortMode.None));
        FreezeBehaviours(Object.FindObjectsByType<EnemyHomingMissile>(FindObjectsInactive.Exclude, FindObjectsSortMode.None));
        FreezeBehaviours(Object.FindObjectsByType<EnemyRocketHomingMissile>(FindObjectsInactive.Exclude, FindObjectsSortMode.None));
        FreezeBehaviours(Object.FindObjectsByType<EnemyGrenade>(FindObjectsInactive.Exclude, FindObjectsSortMode.None));
        FreezeBehaviours(Object.FindObjectsByType<EnemyGrenadeExplosion>(FindObjectsInactive.Exclude, FindObjectsSortMode.None));
    }

    static void StopSpawners()
    {
        var generators = Object.FindObjectsByType<EnemyGenerate>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < generators.Length; i++)
        {
            if (generators[i] != null)
                generators[i].StopSpawning();
        }

        var triggers = Object.FindObjectsByType<EnemySpawnTrigger>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < triggers.Length; i++)
        {
            if (triggers[i] != null)
                triggers[i].enabled = false;
        }
    }

    static void FreezeBehaviours(MonoBehaviour[] behaviours)
    {
        if (behaviours == null)
            return;

        for (int i = 0; i < behaviours.Length; i++)
        {
            var behaviour = behaviours[i];
            if (behaviour == null || !behaviour.isActiveAndEnabled)
                continue;

            FreezeRigidbody(behaviour.GetComponent<Rigidbody2D>());

            var animator = behaviour.GetComponent<Animator>();
            if (animator != null)
                animator.speed = 0f;

            DisableAttacks(behaviour.gameObject);
            behaviour.CancelInvoke();
            behaviour.StopAllCoroutines();
            behaviour.enabled = false;
        }
    }

    static void FreezeRigidbody(Rigidbody2D rb)
    {
        if (rb == null)
            return;

        rb.linearVelocity = Vector2.zero;
        rb.angularVelocity = 0f;
        rb.simulated = false;
    }

    static void DisableAttacks(GameObject root)
    {
        if (root == null)
            return;

        var attacks = root.GetComponentsInChildren<Attack>(true);
        for (int i = 0; i < attacks.Length; i++)
        {
            if (attacks[i] != null)
                attacks[i].enabled = false;
        }
    }
}
