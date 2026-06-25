using UnityEngine;

/// <summary>
/// 简单摄像机跟随，适用于未配置 Cinemachine 的测试场景。
/// </summary>
public class CameraFollow : MonoBehaviour
{
    [SerializeField] Transform target;
    [SerializeField] Vector3 offset = new Vector3(0f, 0f, -10f);
    [SerializeField] bool smoothFollow;
    [SerializeField] float smoothSpeed = 8f;

    void LateUpdate()
    {
        if (target == null)
        {
            var player = GameObject.FindGameObjectWithTag("Player");
            if (player != null)
                target = player.transform;
            if (target == null)
                return;
        }

        Vector3 desiredPosition = target.position + offset;
        if (smoothFollow)
            transform.position = Vector3.Lerp(transform.position, desiredPosition, smoothSpeed * Time.deltaTime);
        else
            transform.position = desiredPosition;
    }
}
