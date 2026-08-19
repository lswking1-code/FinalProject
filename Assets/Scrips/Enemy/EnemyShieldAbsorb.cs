using UnityEngine;

/// <summary>
/// 挂在 Shield 子物体上：吸收正面非 Electric 伤害；上方/背后穿透；盾血归零后销毁。
/// </summary>
public class EnemyShieldAbsorb : MonoBehaviour, IDamageAbsorb
{
    const string ElectricTag = "Electric";
    const string BlastTag = "Blast";

    [Header("护盾")]
    [Tooltip("护盾生命上限")]
    public float maxShieldHealth = 30f;
    [Tooltip("当前护盾生命")]
    public float currentShieldHealth = 30f;
    [Tooltip("攻击源相对敌人中心高于此值视为上方攻击，不吸收")]
    public float aboveHeightThreshold = 0.8f;

    ShieldEnemy shieldEnemy;
    Enemy enemy;

    void Awake()
    {
        currentShieldHealth = maxShieldHealth;
        shieldEnemy = GetComponentInParent<ShieldEnemy>();
        enemy = shieldEnemy != null ? shieldEnemy : GetComponentInParent<Enemy>();

        // 盾牌只做伤害吸收体积，不参与地面物理，避免复合碰撞体把人从悬崖边翘下去
        var absorbCol = GetComponent<Collider2D>();
        if (absorbCol != null)
            absorbCol.isTrigger = true;
    }

    public bool TryAbsorb(Attack attacker)
    {
        if (attacker == null || enemy == null || enemy.isDead)
            return false;

        if (IsElectric(attacker.transform))
            return false;

        Vector3 attackPos = attacker.transform.position;
        float dy = attackPos.y - enemy.transform.position.y;
        if (dy >= aboveHeightThreshold)
            return false;

        float toAttackX = attackPos.x - enemy.transform.position.x;
        // 与 faceDir 同号 = 攻击来自面朝一侧（正面）
        if (toAttackX * enemy.faceDir.x <= 0f)
            return false;

        // 正面 Blast 虽被盾吸收、不会进 Character.OnTakeDamage，但仍需引爆盾上标记炸弹
        TryDetonateMarkBombsFromBlast(attacker);

        float multiplier = attacker.shieldDamageMultiplier > 0f
            ? attacker.shieldDamageMultiplier
            : 1f;
        float damage = Mathf.Max(0, attacker.damage) * multiplier;
        currentShieldHealth -= damage;

        NotifyAggroIfNeeded();

        bool broken = currentShieldHealth <= 0f;
        if (broken)
            BreakShield();
        else if (shieldEnemy != null)
            shieldEnemy.PlayShieldHitAnim();

        return true;
    }

    void TryDetonateMarkBombsFromBlast(Attack attacker)
    {
        if (attacker == null || !IsBlast(attacker.transform))
            return;

        var bombs = GetComponentsInChildren<EnemyMarkBomb>();
        for (int i = 0; i < bombs.Length; i++)
        {
            if (bombs[i] != null)
                bombs[i].TryDetonateFromBlast(attacker.transform);
        }
    }

    static bool IsElectric(Transform root)
    {
        for (Transform t = root; t != null; t = t.parent)
        {
            if (t.CompareTag(ElectricTag))
                return true;
        }

        return false;
    }

    static bool IsBlast(Transform root)
    {
        for (Transform t = root; t != null; t = t.parent)
        {
            if (t.CompareTag(BlastTag))
                return true;
        }

        return false;
    }

    void NotifyAggroIfNeeded()
    {
        if (shieldEnemy == null)
            return;

        if (shieldEnemy.isPatrol && !shieldEnemy.isAggro)
        {
            shieldEnemy.EnterPatrolCombat();
            shieldEnemy.EvaluateCycle();
        }
    }

    void BreakShield()
    {
        // 破盾前把标记炸弹转挂到敌人，避免随盾销毁丢失标记
        ReparentMarkBombsToEnemy();

        if (shieldEnemy != null)
            shieldEnemy.NotifyShieldBroken();

        Destroy(gameObject);
    }

    void ReparentMarkBombsToEnemy()
    {
        if (enemy == null)
            return;

        var bombs = GetComponentsInChildren<EnemyMarkBomb>();
        for (int i = 0; i < bombs.Length; i++)
        {
            EnemyMarkBomb bomb = bombs[i];
            if (bomb == null)
                continue;

            bomb.transform.SetParent(enemy.transform, true);
        }
    }
}
