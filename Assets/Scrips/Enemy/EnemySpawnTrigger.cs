using UnityEngine;

/// <summary>
/// 玩家进入触发区域后生成敌人，默认只生成一次。
/// </summary>
public class EnemySpawnTrigger : MonoBehaviour
{
    [SerializeField] GameObject enemyPrefab;
    [SerializeField] Transform spawnPoint;
    [SerializeField] bool spawnOnce = true;

    bool hasSpawned;

    void OnTriggerEnter2D(Collider2D other)
    {
        if (!other.CompareTag("Player"))
            return;
        if (spawnOnce && hasSpawned)
            return;

        hasSpawned = true;
        SpawnEnemy();
    }

    void SpawnEnemy()
    {
        if (enemyPrefab == null)
        {
            Debug.LogWarning("EnemySpawnTrigger: enemyPrefab 未配置。", this);
            return;
        }

        Vector3 pos = spawnPoint != null ? spawnPoint.position : transform.position;
        Instantiate(enemyPrefab, pos, Quaternion.identity);
    }

    void OnDrawGizmosSelected()
    {
        if (spawnPoint != null)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(spawnPoint.position, 0.3f);
        }
    }
}
