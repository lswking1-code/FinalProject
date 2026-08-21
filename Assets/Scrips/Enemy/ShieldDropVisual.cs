using UnityEngine;

/// <summary>
/// 破盾掉落视觉：解绑后先上抛再向后落下并旋转，lifetime 秒后自毁。
/// </summary>
public class ShieldDropVisual : MonoBehaviour
{
    [SerializeField] float lifetime = 2.5f;
    [SerializeField] float angularSpeed = 150f;
    [Tooltip("被击飞时的上抛速度")]
    [SerializeField] float popUpSpeed = 6f;
    [Tooltip("被击飞时向后的水平速度，越大飞得越远")]
    [SerializeField] float popOutSpeed = 6f;
    [Tooltip("下落重力倍率，越大掉得越快")]
    [SerializeField] float gravityScale = 2.5f;

    bool dropped;

    public void Drop()
    {
        if (dropped)
            return;

        dropped = true;

        Transform source = transform.parent;
        gameObject.SetActive(true);
        transform.SetParent(null, true);

        if (source != null)
            EnemySceneCleanup.PlaceInSourceScene(gameObject, source);

        var rb = GetComponent<Rigidbody2D>();
        if (rb != null)
        {
            rb.bodyType = RigidbodyType2D.Dynamic;
            rb.gravityScale = gravityScale;
            rb.freezeRotation = false;
            rb.constraints = RigidbodyConstraints2D.None;
            rb.simulated = true;

            float facing = 1f;
            if (source != null)
            {
                float sx = source.lossyScale.x;
                if (!Mathf.Approximately(sx, 0f))
                    facing = Mathf.Sign(sx);
            }

            rb.linearVelocity = new Vector2(-facing * popOutSpeed, popUpSpeed);
            rb.angularVelocity = facing * angularSpeed;
        }

        Destroy(gameObject, lifetime > 0f ? lifetime : 0f);
    }
}
