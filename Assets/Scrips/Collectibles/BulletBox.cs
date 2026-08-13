using UnityEngine;

public class BulletBox : MonoBehaviour
{
    [SerializeField] AmmoType ammoType = AmmoType.S;
    [SerializeField] int amount = 1;

    void OnTriggerEnter2D(Collider2D other) => TryPickup(other);

    void OnTriggerStay2D(Collider2D other) => TryPickup(other);

    void TryPickup(Collider2D other)
    {
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
