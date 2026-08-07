using System.Collections;
using UnityEngine;

/// <summary>
/// 机器人贯穿激光视觉：按钩锁式 TiledSpriteRope 分段排布，保留每段 Animator。
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

        Vector2 perp = new Vector2(-dir.y, dir.x);
        Vector2 spawn = origin + dir * spawnOffset.x + perp * spawnOffset.y;

        float safeLen = Mathf.Max(0f, length);
        Vector2 end = spawn + dir * safeLen;
        float startAlong = Mathf.Clamp(beamStartOffset, 0f, safeLen);
        Vector2 beamStart = spawn + dir * startAlong;

        transform.position = spawn;

        if (tiledRope != null)
        {
            tiledRope.SetVisible(true);
            tiledRope.SetEndpoints(beamStart, end);
        }

        if (head != null)
        {
            head.localPosition = Vector3.zero;
            ApplyEndpointFacing(head, headRenderer, dir);
        }

        if (blast != null)
        {
            blast.position = end;
            ApplyEndpointFacing(blast, blastRenderer, dir);
        }

        StartCoroutine(DestroyAfter(Mathf.Max(0.01f, duration)));
    }

    /// <summary>
    /// 默认贴图朝 +X。朝左用 flipX 而非旋转 180°；俯仰用 Atan2(y, |x|)。
    /// </summary>
    static void ApplyEndpointFacing(Transform endpoint, SpriteRenderer renderer, Vector2 dir)
    {
        if (dir.sqrMagnitude < 0.0001f)
            return;

        bool faceLeft = dir.x < 0f;
        if (renderer != null)
        {
            renderer.flipX = faceLeft;
            renderer.flipY = false;
        }

        float angle = Mathf.Atan2(dir.y, Mathf.Abs(dir.x)) * Mathf.Rad2Deg;
        endpoint.rotation = Quaternion.Euler(0f, 0f, angle);
    }

    IEnumerator DestroyAfter(float delay)
    {
        yield return new WaitForSeconds(delay);
        Destroy(gameObject);
    }
}
