using UnityEngine;

/// <summary>
/// 玩家踏上电梯后通知导演开始行程。
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class ElevatorBoardTrigger : MonoBehaviour
{
    [SerializeField] ElevatorDirector director;

    void Awake()
    {
        if (director == null)
            director = GetComponentInParent<ElevatorDirector>();

        var col = GetComponent<Collider2D>();
        if (col != null && !col.isTrigger)
            Debug.LogWarning("ElevatorBoardTrigger: Collider 应勾选 Is Trigger。", this);
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (director == null || other == null)
            return;
        if (!other.CompareTag("Player"))
            return;

        director.TryBoard(other);
    }
}
