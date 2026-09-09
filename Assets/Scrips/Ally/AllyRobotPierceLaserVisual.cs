using System.Collections;
using UnityEngine;

/// <summary>
/// 机器人贯穿激光视觉：按钩锁式 TiledSpriteRope 分段排布，保留每段 Animator。
/// 根节点与光束链节共用 Atan2(y, x) 朝向，起始段作为子物体继承该方向。
/// </summary>
public class AllyRobotPierceLaserVisual : MonoBehaviour
{
    [SerializeField] TiledSpriteRope tiledRope;
    [SerializeField] Transform head;
    [SerializeField] Transform blast;
    [Tooltip("整体生成点偏移（射击局部空间）：X 沿射击方向，Y 为垂直方向（射击方向左侧为正）")]
    [SerializeField] Vector2 spawnOffset;
    [Tooltip("第一段激光沿射击方向相对生成点的额外偏移；正值远离起点，便于露出 Head")]
    [SerializeField] float beamStartOffset = 0f;

    SpriteRenderer headRenderer;
    SpriteRenderer blastRenderer;
    Vector2 shotDir = Vector2.right;

    void Awake()
    {
        if (tiledRope == null)
            tiledRope = GetComponent<TiledSpriteRope>();
        if (head != null)
            headRenderer = head.GetComponent<SpriteRenderer>();
        if (blast != null)
            blastRenderer = blast.GetComponent<SpriteRenderer>();
    }

    public void Setup(Vector2 origin, Vector2 dir, float length, float duration)
    {
        if (dir.sqrMagnitude < 0.0001f)
            dir = Vector2.right;
        else
            dir.Normalize();

        shotDir = dir;

        Vector2 perp = new Vector2(-dir.y, dir.x);
        Vector2 spawn = origin + dir * spawnOffset.x + perp * spawnOffset.y;

        float safeLen = Mathf.Max(0f, length);
        Vector2 end = spawn + dir * safeLen;
        float startAlong = Mathf.Clamp(beamStartOffset, 0f, safeLen);
        Vector2 beamStart = spawn + dir * startAlong;

        ApplyShotRotation(spawn);

        if (tiledRope != null)
        {
            tiledRope.SetVisible(true);
            tiledRope.SetEndpoints(beamStart, end);
        }

        AlignChildToShot(head, headRenderer, Vector3.zero);

        if (blast != null)
        {
            if (blast.parent == transform)
                AlignChildToShot(blast, blastRenderer, new Vector3(safeLen, 0f, 0f));
            else
            {
                blast.SetPositionAndRotation(end, transform.rotation);
                ClearSpriteFlip(blastRenderer);
            }
        }

        StartCoroutine(DestroyAfter(Mathf.Max(0.01f, duration)));
    }

    void LateUpdate()
    {
        ApplyShotRotation(transform.position);
        AlignChildToShot(head, headRenderer, Vector3.zero);
    }

    void ApplyShotRotation(Vector3 position)
    {
        float angle = Mathf.Atan2(shotDir.y, shotDir.x) * Mathf.Rad2Deg;
        transform.SetPositionAndRotation(position, Quaternion.Euler(0f, 0f, angle));
    }

    static void AlignChildToShot(Transform child, SpriteRenderer renderer, Vector3 localPosition)
    {
        if (child == null)
            return;

        child.localPosition = localPosition;
        child.localRotation = Quaternion.identity;
        child.localScale = Vector3.one;
        ClearSpriteFlip(renderer);
    }

    static void ClearSpriteFlip(SpriteRenderer renderer)
    {
        if (renderer == null)
            return;

        renderer.flipX = false;
        renderer.flipY = false;
    }

    IEnumerator DestroyAfter(float delay)
    {
        yield return new WaitForSeconds(delay);
        Destroy(gameObject);
    }
}
