using UnityEngine;

/// <summary>
/// 假上升背景：电梯不动时沿 -Y 循环滚动子精灵。
/// </summary>
public class ElevatorBackgroundScroller : MonoBehaviour
{
    [SerializeField] Transform[] tiles;
    [SerializeField] float scrollSpeed = 4f;
    [SerializeField] float tileHeight = 20f;

    bool scrolling;

    public void SetScrolling(bool enabled)
    {
        scrolling = enabled;
    }

    void LateUpdate()
    {
        if (!scrolling || tiles == null || tiles.Length == 0)
            return;

        float delta = scrollSpeed * Time.deltaTime;
        float wrapSpan = tileHeight * tiles.Length;

        for (int i = 0; i < tiles.Length; i++)
        {
            Transform tile = tiles[i];
            if (tile == null)
                continue;

            Vector3 pos = tile.localPosition;
            pos.y -= delta;
            if (pos.y < -tileHeight)
                pos.y += wrapSpan;
            tile.localPosition = pos;
        }
    }
}
