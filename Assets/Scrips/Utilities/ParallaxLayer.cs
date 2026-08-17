using UnityEngine;

/// <summary>
/// 正交相机下的视差层：相对相机位移按倍率移动，远处慢、近处快。
/// LateUpdate 排在 Cinemachine 之后，避免用到尚未落地的相机位置。
/// </summary>
[DefaultExecutionOrder(32000)]
public class ParallaxLayer : MonoBehaviour
{
    const float CameraTeleportRecalibrateDistance = 8f;

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
    Vector3 lastCamPos;
    bool hasOrigin;
    bool hasBaseline;
    bool hasLastCamPos;
    static Vector3 s_referenceCamPos;
    static bool s_hasReferenceCamPos;

    void Awake()
    {
        originPos = transform.position;
        hasOrigin = true;
    }

    void OnEnable()
    {
        hasLastCamPos = false;
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

        Vector3 camPos = cam.transform.position;
        bool teleported = hasLastCamPos
            && (camPos - lastCamPos).sqrMagnitude
                > CameraTeleportRecalibrateDistance * CameraTeleportRecalibrateDistance;
        if (teleported)
        {
            RecalibrateToCamera();
            return;
        }

        ApplyParallax(cam);
        RememberCameraPos(camPos);
    }

    /// <summary>
    /// 用场景原始坐标 + 关卡参考相机重建基线。
    /// 参考相机是关卡摆放时的镜头原点，不能用读档后的当前相机。
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
            hasLastCamPos = false;
            return;
        }

        RememberReferenceCamera(cam.transform.position);
        startCamPos = s_referenceCamPos;
        hasBaseline = true;
        RememberCameraPos(cam.transform.position);
        ApplyParallax(cam);
    }

    /// <summary>
    /// 将场景内所有视差层对齐到参考相机（在相机 Snap 之后调用）。
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

        startPos = originPos;
        Camera cam = ResolveCamera();
        if (cam == null)
        {
            hasBaseline = false;
            return;
        }

        RememberReferenceCamera(cam.transform.position);
        startCamPos = s_referenceCamPos;
        hasBaseline = true;
        RememberCameraPos(cam.transform.position);
    }

    static void RememberReferenceCamera(Vector3 camPos)
    {
        if (s_hasReferenceCamPos)
            return;

        // 关卡层按镜头约在 x=0 时摆放。读档/Continue 时镜头已在存档点，
        // 若把当前相机当成原点，背景会钉回默认坐标并离开画面。
        if (Mathf.Abs(camPos.x) > 8f)
            camPos.x = 0f;

        s_referenceCamPos = camPos;
        s_hasReferenceCamPos = true;
    }

    void RememberCameraPos(Vector3 camPos)
    {
        lastCamPos = camPos;
        hasLastCamPos = true;
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
