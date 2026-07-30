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

    Vector3 startPos;
    Vector3 startCamPos;
    bool hasBaseline;

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

        Vector3 camDelta = cam.transform.position - startCamPos;
        transform.position = new Vector3(
            startPos.x + camDelta.x * parallaxFactorX,
            startPos.y + camDelta.y * parallaxFactorY,
            startPos.z);
    }

    void CaptureBaseline()
    {
        startPos = transform.position;
        Camera cam = ResolveCamera();
        if (cam == null)
        {
            hasBaseline = false;
            return;
        }

        startCamPos = cam.transform.position;
        hasBaseline = true;
    }

    Camera ResolveCamera()
    {
        if (cameraOverride != null)
            return cameraOverride;
        return Camera.main;
    }
}
