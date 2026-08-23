using UnityEngine;

public class BulletBox : MonoBehaviour
{
    [SerializeField] AmmoType ammoType = AmmoType.S;
    [SerializeField] int amount = 1;

    public AmmoType AmmoType => ammoType;

    PickupDelay pickupDelay;
    Rigidbody2D rb;
    bool landed;

    void Awake()
    {
        pickupDelay = GetComponent<PickupDelay>();
        rb = GetComponent<Rigidbody2D>();
        if (rb != null)
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

        // 落地盒保持足够大以免穿地；角色层全部排除，避免再卡住玩家。
        int exclude = LayerMask.GetMask(
            "Player", "Robot", "Enemy", "EliteEnemy",
            "EnemyBullet", "PlayerBullet", "PlayerSpecialBullet",
            "RobotWeapen", "RobotTop");
        var colliders = GetComponents<Collider2D>();
        for (int i = 0; i < colliders.Length; i++)
        {
            if (colliders[i] != null && !colliders[i].isTrigger)
                colliders[i].excludeLayers = exclude;
        }
    }

    void OnCollisionEnter2D(Collision2D collision)
    {
        if (landed || rb == null)
            return;

        for (int i = 0; i < collision.contactCount; i++)
        {
            if (collision.GetContact(i).normal.y <= 0.5f)
                continue;

            landed = true;
            rb.linearVelocity = Vector2.zero;
            rb.angularVelocity = 0f;
            rb.constraints = RigidbodyConstraints2D.FreezeAll;
            return;
        }
    }

    void OnTriggerEnter2D(Collider2D other) => TryPickup(other);

    void OnTriggerStay2D(Collider2D other) => TryPickup(other);

    void TryPickup(Collider2D other)
    {
        if (pickupDelay != null && pickupDelay.IsLocked)
            return;

        if (!other.CompareTag("Player"))
            return;

        var character = other.GetComponent<Character>()
            ?? other.GetComponentInParent<Character>();
        if (character == null)
            return;

        if (!character.AddAmmo(ammoType, amount))
            return;

        Destroy(gameObject);
    }
}
