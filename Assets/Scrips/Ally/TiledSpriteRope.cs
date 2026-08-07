using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 固定间距多节链环绳索：从起点沿方向依次排布链节 Sprite，可替换 LineRendererRope。
/// </summary>
public class TiledSpriteRope : MonoBehaviour, IRopeVisual
{
    [Header("链节精灵")]
    [SerializeField] Sprite linkSprite;
    [SerializeField] Sprite linkSpriteAlt;

    [Header("排布")]
    [Tooltip("相邻链节锚点间距（世界单位）；锚点为链节靠发射端一侧的连接点")]
    [SerializeField] float linkSpacing = 0.25f;
    [SerializeField] float linkScale = 1f;
    [Tooltip("绳索长度低于此值时不显示链节")]
    [SerializeField] float minLength = 0.01f;
    [Tooltip("勾选后锚点取 Sprite 左缘中心（开口朝右时靠发射端）；取消则用手动偏移")]
    [SerializeField] bool anchorAtSpriteStart = true;
    [Tooltip("锚点相对 Pivot 的本地偏移（anchorAtSpriteStart 关闭时生效）")]
    [SerializeField] Vector2 anchorOffsetFromPivotLocal;

    [Header("渲染")]
    [SerializeField] int sortingOrder;
    [SerializeField] string sortingLayerName;

    [Header("对象池")]
    [SerializeField] int maxPooledLinks = 128;
    [Tooltip("可选：带 SpriteRenderer 的模板子物体，用于复制链节")]
    [SerializeField] Transform linkTemplate;
    [Tooltip("关闭后不覆盖链节 sprite，保留模板 Animator 驱动的帧动画")]
    [SerializeField] bool overwriteLinkSprite = true;

    Transform poolRoot;
    readonly List<SpriteRenderer> links = new List<SpriteRenderer>();
    bool isVisible;
    bool warnedPoolExhausted;

    void OnDestroy()
    {
        if (poolRoot != null)
            Destroy(poolRoot.gameObject);
    }

    float GetLinkFullLength(Sprite sprite)
    {
        if (sprite == null)
            return linkSpacing;
        return sprite.bounds.size.x * linkScale;
    }

    Vector2 GetAnchorFromPivotLocal(Sprite sprite)
    {
        if (!anchorAtSpriteStart || sprite == null)
            return anchorOffsetFromPivotLocal;

        return new Vector2(sprite.bounds.min.x, sprite.bounds.center.y);
    }

    Vector2 PivotWorldFromAnchor(Vector2 anchorWorld, Sprite sprite, Vector2 direction)
    {
        Vector2 anchorLocal = GetAnchorFromPivotLocal(sprite);
        float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
        Vector3 rotated = Quaternion.Euler(0f, 0f, angle) * new Vector3(anchorLocal.x, anchorLocal.y, 0f);
        return anchorWorld - (Vector2)rotated * linkScale;
    }

    void EnsureInitialized()
    {
        if (poolRoot != null)
            return;

        var poolGo = new GameObject($"LinkPool_{GetInstanceID()}");
        poolGo.transform.SetParent(null, false);
        poolRoot = poolGo.transform;

        if (linkTemplate != null)
        {
            var templateRenderer = linkTemplate.GetComponent<SpriteRenderer>();
            if (linkSprite == null && templateRenderer != null)
                linkSprite = templateRenderer.sprite;
            linkTemplate.gameObject.SetActive(false);
        }

        for (int i = 0; i < maxPooledLinks; i++)
            links.Add(CreateLink(i));
    }

    SpriteRenderer CreateLink(int index)
    {
        GameObject linkGo;
        if (linkTemplate != null)
        {
            linkGo = Instantiate(linkTemplate.gameObject, poolRoot);
            linkGo.name = $"Link_{index}";
        }
        else
        {
            linkGo = new GameObject($"Link_{index}");
            linkGo.transform.SetParent(poolRoot, false);
            linkGo.AddComponent<SpriteRenderer>();
        }

        linkGo.SetActive(true);
        var renderer = linkGo.GetComponent<SpriteRenderer>();
        renderer.enabled = false;
        if (overwriteLinkSprite && linkSprite != null)
            renderer.sprite = linkSprite;
        renderer.flipX = false;
        renderer.flipY = false;
        renderer.sortingOrder = sortingOrder;

        if (!string.IsNullOrEmpty(sortingLayerName))
            renderer.sortingLayerName = sortingLayerName;

        return renderer;
    }

    public void SetEndpoints(Vector2 start, Vector2 end)
    {
        EnsureInitialized();

        if (!isVisible)
        {
            DeactivateUnused(0);
            return;
        }

        Vector2 delta = end - start;
        float length = delta.magnitude;
        if (length < minLength)
        {
            DeactivateUnused(0);
            return;
        }

        Vector2 dir = delta / length;
        float spacing = Mathf.Max(linkSpacing, 0.001f);
        float linkLength = GetLinkFullLength(linkSprite);
        float endLimit = Mathf.Max(0f, length - linkLength);

        int index = 0;
        for (float dist = 0f; dist <= endLimit + 0.0001f; dist += spacing)
        {
            if (index >= links.Count)
            {
                if (!warnedPoolExhausted)
                {
                    Debug.LogWarning(
                        $"TiledSpriteRope: 链节数量超出池上限 {maxPooledLinks}，请增大 maxPooledLinks 或 linkSpacing。",
                        this);
                    warnedPoolExhausted = true;
                }
                break;
            }

            PlaceLink(index, start + dir * dist, dir);
            index++;
        }

        DeactivateUnused(index);
    }

    void PlaceLink(int index, Vector2 anchorOnRope, Vector2 direction)
    {
        SpriteRenderer renderer = links[index];
        Transform linkTransform = renderer.transform;

        Sprite sprite = renderer.sprite;
        if (overwriteLinkSprite)
        {
            if (linkSpriteAlt != null)
                sprite = index % 2 == 0 ? linkSprite : linkSpriteAlt;
            else if (linkSprite != null)
                sprite = linkSprite;

            if (sprite != null)
                renderer.sprite = sprite;
        }
        else if (sprite == null)
        {
            sprite = linkSprite;
        }

        float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
        linkTransform.rotation = Quaternion.Euler(0f, 0f, angle);
        linkTransform.localScale = Vector3.one * linkScale;
        linkTransform.position = PivotWorldFromAnchor(anchorOnRope, sprite, direction);

        renderer.enabled = true;
    }

    void DeactivateUnused(int fromIndex)
    {
        for (int i = fromIndex; i < links.Count; i++)
            links[i].enabled = false;
    }

    public void SetVisible(bool visible)
    {
        isVisible = visible;
        EnsureInitialized();

        if (!visible)
            DeactivateUnused(0);
    }
}
