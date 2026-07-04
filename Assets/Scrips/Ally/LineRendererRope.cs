using UnityEngine;

[RequireComponent(typeof(LineRenderer))]
public class LineRendererRope : MonoBehaviour, IRopeVisual
{
    [SerializeField] float width = 0.05f;

    LineRenderer line;

    void EnsureInitialized()
    {
        if (line != null)
            return;

        line = GetComponent<LineRenderer>();
        line.positionCount = 2;
        line.useWorldSpace = true;
        line.startWidth = width;
        line.endWidth = width;
        line.textureMode = LineTextureMode.Tile;
        line.enabled = false;
    }

    public void SetEndpoints(Vector2 start, Vector2 end)
    {
        EnsureInitialized();
        line.SetPosition(0, start);
        line.SetPosition(1, end);
    }

    public void SetVisible(bool visible)
    {
        EnsureInitialized();
        line.enabled = visible;
    }
}
