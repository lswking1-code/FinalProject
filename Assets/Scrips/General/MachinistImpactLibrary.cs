using UnityEngine;

public enum MachinistImpactKind { Auto, None, Bullet, Heavy, Electric, Slash, BlastSlash, Shield, Surface }

/// <summary>Shared pixel-art frames and tuning for machinist hit feedback.</summary>
[CreateAssetMenu(menuName = "Combat/Machinist Impact Library")]
public class MachinistImpactLibrary : ScriptableObject
{
    public Shader shader;
    public Sprite[] bullet;
    public Sprite[] heavy;
    public Sprite[] electric;
    public Sprite[] ring;
    public Sprite[] slash;
    [Header("原创像素弹着 V2")]
    public Shader ballisticShader;
    public Sprite[] ballistic;
    [Range(16f, 40f)] public float ballisticFramesPerSecond = 28f;
    [Range(0.25f, 3f)] public float ballisticSize = 1f;
    [Tooltip("Visible pixel size in world units. The machinist uses 16 pixels per unit.")]
    [Range(0.02f, 0.125f)] public float ballisticWorldPixelSize = 0.0625f;
    [Min(1f)] public float ballisticPixelsPerUnit = 256f;
    [Range(12f, 40f)] public float framesPerSecond = 26f;
    [Range(0.25f, 3f)] public float size = 1f;
    [Range(8, 96)] public int poolCapacity = 48;
    public string sortingLayer = "Bullet";
    public int sortingOrder = 20;
    public Color warm = new Color(1f, 0.46f, 0.08f, 1f);
    public Color energy = new Color(0.15f, 0.7f, 1f, 1f);
    [Tooltip("重击弹命中火星颜色；烟尘保持灰色")]
    public Color heavyColor = new Color(0.08f, 0.4f, 1f, 1f);
    public Color metal = new Color(0.62f, 0.78f, 1f, 1f);

    public bool UsesBallistic(MachinistImpactKind kind) => ballisticShader != null
        && ballistic != null && ballistic.Length > 0
        && (kind == MachinistImpactKind.Bullet || kind == MachinistImpactKind.Shield
            || kind == MachinistImpactKind.Surface);

    public Sprite[] Frames(MachinistImpactKind kind) => UsesBallistic(kind) ? ballistic : kind switch
    {
        MachinistImpactKind.Heavy => heavy,
        MachinistImpactKind.BlastSlash => slash,
        MachinistImpactKind.Electric => electric,
        MachinistImpactKind.Shield => electric,
        MachinistImpactKind.Slash => slash,
        _ => bullet,
    };
}
