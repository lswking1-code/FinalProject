using UnityEngine;

/// <summary>
/// 正交相机下的视差层：相对相机位移按倍率移动，远处慢、近处快。
/// </summary>
public class ParallaxLayer : MonoBehaviour
{
    [Header("视差倍率")]
    [Tooltip("0 = 几乎不动（最远），1 = 与玩法层同速")]
    [SerializeField] float parallaxFactorX = 0.5f;
    [SerializeField] float parallaxFactorY = 0.2f;

    [Header("相机")]
    [Tooltip("留空则使用 Camera.main（含 Cinemachine 输出相机）")]
    [SerializeField] Camera cameraOverride;

    /// <summary>场景中摆放的原始坐标，读档/切场景后用于重新校准，避免叠错位。</summary>
    Vector3 originPos;
    Vector3 startPos;
    Vector3 startCamPos;
    bool hasOrigin;
    bool hasBaseline;

    void Awake()
    {
        originPos = transform.position;
        hasOrigin = true;
    }

    void OnEnable()
    {
        CaptureBaseline();
    }

    void LateUpdate()
    {
        Camera cam = ResolveCamera();
        if (cam == null)
            return;

        if (!hasBaseline)
            CaptureBaseline();

        if (!hasBaseline)
            return;

        ApplyParallax(cam);
    }

    /// <summary>
    /// 相机 Snap 到玩家后调用：用场景原始坐标 + 当前相机位置重建基线，
    /// 修复读档/切场景时 OnEnable 采到旧相机位置导致的背景错位。
    /// </summary>
    public void RecalibrateToCamera()
    {
        if (!hasOrigin)
        {
            originPos = transform.position;
            hasOrigin = true;
        }

        startPos = originPos;
        Camera cam = ResolveCamera();
        if (cam == null)
        {
            hasBaseline = false;
            return;
        }

        startCamPos = cam.transform.position;
        hasBaseline = true;
        ApplyParallax(cam);
    }

    /// <summary>
    /// 将场景内所有视差层对齐到当前相机（在相机 Snap 之后调用）。
    /// </summary>
    public static void RecalibrateAllToCamera()
    {
        var layers = Object.FindObjectsByType<ParallaxLayer>(FindObjectsSortMode.None);
        for (int i = 0; i < layers.Length; i++)
        {
            if (layers[i] != null)
                layers[i].RecalibrateToCamera();
        }
    }

    void CaptureBaseline()
    {
        if (!hasOrigin)
        {
            originPos = transform.position;
            hasOrigin = true;
        }

        // 优先用场景原始坐标，避免在已错位的 transform 上重复采基线
        startPos = originPos;
        Camera cam = ResolveCamera();
        if (cam == null)
        {
            hasBaseline = false;
            return;
        }

        startCamPos = cam.transform.position;
        hasBaseline = true;
    }

    void ApplyParallax(Camera cam)
    {
        Vector3 camDelta = cam.transform.position - startCamPos;
        transform.position = new Vector3(
            startPos.x + camDelta.x * parallaxFactorX,
            startPos.y + camDelta.y * parallaxFactorY,
            startPos.z);
    }

    Camera ResolveCamera()
    {
        if (cameraOverride != null)
            return cameraOverride;
        return Camera.main;
    }
}
