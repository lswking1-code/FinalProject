using UnityEngine;

/// <summary>Only changes the upper display transform; no Animator, physics or facing writes.</summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(10000)]
public sealed class GunnerBodyAlignment : MonoBehaviour
{
    public GunnerBodyCalibration calibration;
    public SpriteRenderer upperRenderer;
    public SpriteRenderer lowerRenderer;
    public GameObject fullBody;
    Transform capturedUpper;
    Vector3 restLocalPosition;
    Transform muzzlePoint;

    public Transform ResolveMuzzle(int weaponId, GunnerBodyCalibration.MuzzleDirection direction, Transform fallback)
    {
        if (!isActiveAndEnabled || calibration == null || upperRenderer == null
            || lowerRenderer == null || lowerRenderer.sprite == null || !lowerRenderer.enabled
            || !lowerRenderer.gameObject.activeInHierarchy
            || !upperRenderer.enabled || !upperRenderer.gameObject.activeInHierarchy
            || (fullBody != null && fullBody.activeInHierarchy)
            || !calibration.TryGetMuzzle(upperRenderer.sprite, weaponId, direction, out var pixel)) return fallback;
        ApplyAlignment();
        if (muzzlePoint == null)
        {
            muzzlePoint = new GameObject("Gunner calibrated muzzle").transform;
            muzzlePoint.SetParent(transform, false);
        }
        muzzlePoint.position = upperRenderer.transform.TransformPoint(
            GunnerBodyCalibration.MuzzleLocalPosition(upperRenderer.sprite, pixel, upperRenderer.flipX, upperRenderer.flipY));
        return muzzlePoint;
    }

    void OnDestroy()
    {
        if (muzzlePoint == null) return;
        if (Application.isPlaying) Destroy(muzzlePoint.gameObject);
        else DestroyImmediate(muzzlePoint.gameObject);
    }

    void Awake() => CaptureRestPosition();
    void OnEnable() { if (capturedUpper == null) CaptureRestPosition(); }
    void LateUpdate() => ApplyAlignment();
    void OnDisable() => RestorePosition();

    void CaptureRestPosition()
    {
        if (upperRenderer == null) return;
        capturedUpper = upperRenderer.transform;
        restLocalPosition = capturedUpper.localPosition;
    }

    public void ApplyAlignment()
    {
        if (upperRenderer == null || capturedUpper != upperRenderer.transform)
        {
            RestorePosition();
            capturedUpper = null;
            CaptureRestPosition();
        }
        if (capturedUpper == null) return;
        if (calibration == null || lowerRenderer == null || upperRenderer.sprite == null || lowerRenderer.sprite == null
            || !upperRenderer.enabled || !lowerRenderer.enabled
            || !upperRenderer.gameObject.activeInHierarchy || !lowerRenderer.gameObject.activeInHierarchy
            || (fullBody != null && fullBody.activeInHierarchy))
        {
            RestorePosition();
            return;
        }
        Vector2 offset = calibration.GetCombinedOffset(upperRenderer.sprite, lowerRenderer.sprite);
        capturedUpper.localPosition = ResolvePosition(transform, capturedUpper.parent, restLocalPosition,
            capturedUpper.localPosition.z, offset);
    }

    public static Vector3 ResolvePosition(Transform character, Transform upperParent, Vector3 rest,
        float currentZ, Vector2 rootOffset)
    {
        Vector3 worldOffset = character.TransformVector(new Vector3(rootOffset.x, rootOffset.y, 0));
        Vector3 parentOffset = upperParent != null ? upperParent.InverseTransformVector(worldOffset) : worldOffset;
        return new Vector3(rest.x + parentOffset.x, rest.y + parentOffset.y, currentZ);
    }

    void RestorePosition()
    {
        if (capturedUpper == null) return;
        var current = capturedUpper.localPosition;
        capturedUpper.localPosition = new Vector3(restLocalPosition.x, restLocalPosition.y, current.z);
    }
}
