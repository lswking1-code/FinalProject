using System.Collections;
using UnityEngine;

/// <summary>
/// 按固定间隔在生成点批量实例化敌人预制体。
/// </summary>
public class EnemyGenerate : MonoBehaviour
{
    [Header("生成配置")]
    [SerializeField] GameObject enemyPrefab;
    [SerializeField] Transform[] spawnPoints;
    [SerializeField] int spawnCountPerWave = 1;
    [SerializeField] float spawnInterval = 3f;
    [SerializeField] float initialDelay;

    [Header("启动")]
    [SerializeField] bool spawnOnStart = true;

    Coroutine spawnRoutine;

    void Start()
    {
        if (spawnOnStart)
            StartSpawning();
    }

    void OnDisable() => StopSpawning();

    public void StartSpawning()
    {
        StopSpawning();
        spawnRoutine = StartCoroutine(SpawnRoutine());
    }

    public void StopSpawning()
    {
        if (spawnRoutine == null)
            return;

        StopCoroutine(spawnRoutine);
        spawnRoutine = null;
    }

    IEnumerator SpawnRoutine()
    {
        if (initialDelay > 0f)
            yield return new WaitForSeconds(initialDelay);

        while (true)
        {
            SpawnWave();
            yield return new WaitForSeconds(spawnInterval);
        }
    }

    void SpawnWave()
    {
        if (enemyPrefab == null)
        {
            Debug.LogWarning("EnemyGenerate: enemyPrefab 未配置。", this);
            return;
        }

        for (int i = 0; i < spawnCountPerWave; i++)
            SpawnEnemyAt(GetSpawnPosition(i));
    }

    Vector3 GetSpawnPosition(int index)
    {
        if (spawnPoints == null || spawnPoints.Length == 0)
            return transform.position;

        var point = spawnPoints[index % spawnPoints.Length];
        return point != null ? point.position : transform.position;
    }

    void SpawnEnemyAt(Vector3 position)
    {
        Instantiate(enemyPrefab, position, Quaternion.identity);
    }
}
