using UnityEngine;

[DefaultExecutionOrder(99)]
[RequireComponent(typeof(PlayerAnimBase))]
[RequireComponent(typeof(PlayerMovement))]
public class PlayerMelee : MonoBehaviour
{
    [SerializeField] int damage = 40;
    [SerializeField] float hitStart = 0.15f;
    [SerializeField] float hitEnd = 0.45f;
    [SerializeField] Transform meleePoint1;
    [SerializeField] Transform meleePoint2;
    [SerializeField] GameObject meleeHitbox;
    [SerializeField] MeleeDetectZone detectZone;

    PlayerAnimBase playerAnim;
    PlayerMovement playerMovement;
    Attack attack;

    void Awake()
    {
        playerAnim = GetComponent<PlayerAnimBase>();
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

    void LateUpdate() => SyncDetectZoneAnchor();

    public bool IsEnemyInMeleeRange()
        => detectZone != null && detectZone.HasValidTarget;

    public bool TryMelee()
    {
        if (detectZone == null)
            return false;

        var target = detectZone.GetNearestTarget(transform.position);
        if (target == null)
            return false;

        playerMovement.FaceTowardWorldX(target.position.x);
        return playerAnim.TryPlayMeleeAnim();
    }

    void SyncDetectZoneAnchor()
    {
        if (detectZone == null)
            return;

        Transform anchor = playerAnim.IsCrouching ? meleePoint2 : meleePoint1;
        if (anchor == null)
            anchor = transform;

        var zoneTransform = detectZone.transform;
        if (zoneTransform.parent == anchor)
            return;

        zoneTransform.SetParent(anchor, false);
        zoneTransform.localPosition = Vector3.zero;
        zoneTransform.localRotation = Quaternion.identity;
        zoneTransform.localScale = Vector3.one;
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
}
