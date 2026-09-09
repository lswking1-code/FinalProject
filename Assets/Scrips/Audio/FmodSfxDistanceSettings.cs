using UnityEngine;

/// <summary>
/// 2D 世界音效的距离裁剪 / 音量曲线 / 左右声像。放到 Resources 后运行时自动加载。
/// </summary>
[CreateAssetMenu(
    fileName = "FmodSfxDistanceSettings",
    menuName = "Audio/FMOD SFX Distance Settings")]
public class FmodSfxDistanceSettings : ScriptableObject
{
    public const string ResourcesName = "FmodSfxDistanceSettings";

    [Tooltip("此距离内（世界单位，XY）保持满音量")]
    [Min(0f)] public float minDistance = 8f;

    [Tooltip("达到此距离不播放。默认约 1.5 个 16:9 屏幕宽（正交尺寸 5）")]
    [Min(0.01f)] public float maxDistance = 28f;

    [Tooltip("相对玩家的 X 偏移达到此值时完全偏到左/右声道")]
    [Min(0.01f)] public float panRange = 16f;

    [Tooltip("横轴 0 = minDistance（满音量），1 = maxDistance（静音）")]
    public AnimationCurve volumeCurve = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);

    [Tooltip("按声源相对玩家的 X 做左右声像。Stereo 发糊时可关掉")]
    public bool enablePan = true;

    [Tooltip("按当前相机正交尺寸相对参考值缩放 min/max/panRange")]
    public bool scaleWithCamera = true;

    [Tooltip("scaleWithCamera 时的参考正交尺寸（项目默认相机约 5）")]
    [Min(0.01f)] public float referenceOrthographicSize = 5f;

    static FmodSfxDistanceSettings cached;

    public static FmodSfxDistanceSettings Resolve()
    {
        if (cached != null)
            return cached;

        cached = Resources.Load<FmodSfxDistanceSettings>(ResourcesName);
        return cached;
    }
}
