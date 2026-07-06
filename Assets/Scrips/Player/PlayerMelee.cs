using UnityEngine;

[DefaultExecutionOrder(99)]
[RequireComponent(typeof(PlayerAnim))]
[RequireComponent(typeof(PlayerMovement))]
public class PlayerMelee : MonoBehaviour
{
    [SerializeField] float meleeDetectRange = 2f;
    [SerializeField] float meleeDetectHeight = 2f;
    [SerializeField] int damage = 40;
    [SerializeField] float hitStart = 0.15f;
    [SerializeField] float hitEnd = 0.45f;
    [SerializeField] Transform meleePoint1;
    [SerializeField] Transform meleePoint2;
    [SerializeField] GameObject meleeHitbox;

    PlayerAnim playerAnim;
    PlayerMovement playerMovement;
    Attack attack;

    void Awake()
    {
        playerAnim = GetComponent<PlayerAnim>();
        playerMovement = GetComponent<PlayerMovement>();

        if (meleeHitbox != null)
        {
            attack = meleeHitbox.GetComponent<Attack>();
            if (attack != null)
            {
                attack.damage = damage;
                attack.attackType = AttackType.Melee;
                attack.ignoreTag = "Player";
            }

            meleeHitbox.SetActive(false);
        }
    }

    void Update()
    {
        if (!playerAnim.IsMelee)
        {
            if (meleeHitbox != null && meleeHitbox.activeSelf)
                meleeHitbox.SetActive(false);
            return;
        }

        SyncHitboxAnchor();

        if (playerAnim.TryGetMeleeAnimProgress(out float t) && t >= hitStart && t <= hitEnd)
        {
            if (meleeHitbox != null && !meleeHitbox.activeSelf)
                meleeHitbox.SetActive(true);
        }
        else if (meleeHitbox != null && meleeHitbox.activeSelf)
        {
            meleeHitbox.SetActive(false);
        }
    }

    public bool IsEnemyInMeleeRange() => FindNearestMeleeTarget() != null;

    public bool TryMelee()
    {
        var target = FindNearestMeleeTarget();
        if (target == null)
            return false;

        playerMovement.FaceTowardWorldX(target.position.x);
        return playerAnim.TryPlayMeleeAnim();
    }

    void SyncHitboxAnchor()
    {
        if (meleeHitbox == null)
            return;

        Transform anchor = playerAnim.IsCrouching ? meleePoint2 : meleePoint1;
        if (anchor == null)
            anchor = transform;

        var hitboxTransform = meleeHitbox.transform;
        if (hitboxTransform.parent == anchor)
            return;

        hitboxTransform.SetParent(anchor, false);
        hitboxTransform.localPosition = Vector3.zero;
        hitboxTransform.localRotation = Quaternion.identity;
        hitboxTransform.localScale = Vector3.one;
    }

    Transform FindNearestMeleeTarget()
    {
        var enemies = GameObject.FindGameObjectsWithTag("Enemy");
        Vector2 playerPos = transform.position;
        Transform nearest = null;
        float nearestDist = float.MaxValue;

        foreach (var enemy in enemies)
        {
            if (!IsValidMeleeTarget(enemy.transform, playerPos, out float dist))
                continue;

            if (dist < nearestDist)
            {
                nearestDist = dist;
                nearest = enemy.transform;
            }
        }

        return nearest;
    }

    bool IsValidMeleeTarget(Transform enemy, Vector2 playerPos, out float distance)
    {
        distance = float.MaxValue;

        var enemyComponent = enemy.GetComponent<Enemy>();
        if (enemyComponent != null && enemyComponent.isDead)
            return false;

        var character = enemy.GetComponent<Character>();
        if (character != null && character.currentHealth <= 0f)
            return false;

        Vector2 delta = (Vector2)enemy.position - playerPos;
        if (Mathf.Abs(delta.y) > meleeDetectHeight)
            return false;

        distance = delta.magnitude;
        return distance <= meleeDetectRange;
    }
}
