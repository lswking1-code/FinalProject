using UnityEngine;

/// <summary>
/// 玩家进入触发区域后结束指定遭遇战（调用 EncounterZone.EndEncounter）。
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class EncounterEndZone : MonoBehaviour
{
    [SerializeField] EncounterZone encounterZone;
    [Tooltip("成功结束后禁用自身 Collider，避免重复触发")]
    [SerializeField] bool triggerOnce = true;

    Collider2D triggerCollider;
    bool hasTriggered;

    void Awake()
    {
        triggerCollider = GetComponent<Collider2D>();
        if (triggerCollider != null && !triggerCollider.isTrigger)
            Debug.LogWarning("EncounterEndZone: Collider2D 应勾选 Is Trigger。", this);
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (hasTriggered && triggerOnce)
            return;
        if (other == null || !other.CompareTag("Player"))
            return;
        if (encounterZone == null)
        {
            Debug.LogWarning("EncounterEndZone: encounterZone 未配置。", this);
            return;
        }

        if (!encounterZone.IsActive)
            return;

        encounterZone.EndEncounter();
        hasTriggered = true;

        if (triggerOnce && triggerCollider != null)
            triggerCollider.enabled = false;
    }
}
