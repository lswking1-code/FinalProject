using UnityEngine;

public class HealthPack : MonoBehaviour
{
    [SerializeField] float amount = 50f;

    PickupDelay pickupDelay;

    void Awake()
    {
        pickupDelay = GetComponent<PickupDelay>();
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

        if (!character.TryHeal(amount))
            return;

        Destroy(gameObject);
    }
}
