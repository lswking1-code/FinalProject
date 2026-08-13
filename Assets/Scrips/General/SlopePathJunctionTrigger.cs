using UnityEngine;

/// <summary>
/// 分层斜坡交界 Trigger：仅标记玩家是否在坡脚/坡顶区域，闩锁由 LayeredPathGate 计算。
/// </summary>
[RequireComponent(typeof(BoxCollider2D))]
public class SlopePathJunctionTrigger : MonoBehaviour
{
    [SerializeField] SlopePathSegment.JunctionKind kind = SlopePathSegment.JunctionKind.Bottom;
    [SerializeField] SlopePathSegment segment;

    bool playerInside;
    Collider2D playerCollider;

    public SlopePathSegment.JunctionKind Kind => kind;
    public SlopePathSegment Segment => segment;
    public bool PlayerInside => playerInside;
    public Collider2D PlayerCollider => playerCollider;

    public void Initialize(SlopePathSegment owner, SlopePathSegment.JunctionKind junctionKind)
    {
        segment = owner;
        kind = junctionKind;
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (!other.CompareTag("Player"))
            return;
        playerInside = true;
        playerCollider = other;
    }

    void OnTriggerStay2D(Collider2D other)
    {
        if (!other.CompareTag("Player"))
            return;
        playerInside = true;
        playerCollider = other;
    }

    void OnTriggerExit2D(Collider2D other)
    {
        if (playerCollider == null || other != playerCollider)
            return;
        playerInside = false;
        playerCollider = null;
    }

    void OnDisable()
    {
        playerInside = false;
        playerCollider = null;
    }
}
