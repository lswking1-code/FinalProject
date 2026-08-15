using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 标记区域：场上同时只允许一个；敌人进入 Trigger 被标记，离开或区域销毁时清除。
/// 存活 10 秒后自毁。生成后通知 AllyRobot 重新索敌。
/// </summary>
public class TaggetArea : MonoBehaviour
{
    [SerializeField] float lifetime = 10f;

    readonly HashSet<Enemy> markedEnemies = new HashSet<Enemy>();
    bool isValid;

    void Awake()
    {
        GameObject[] areas = GameObject.FindGameObjectsWithTag("TaggetArea");
        foreach (var area in areas)
        {
            if (area != null && area != gameObject)
                Destroy(area);
        }

        isValid = true;
    }

    void Start()
    {
        if (!isValid)
            return;

        Destroy(gameObject, lifetime);
        StartCoroutine(NotifyRobotsRetargetAfterPhysics());
    }

    /// <summary>
    /// 等一次 FixedUpdate，确保重叠敌人已触发 OnTriggerEnter2D 打上标记后再索敌。
    /// </summary>
    IEnumerator NotifyRobotsRetargetAfterPhysics()
    {
        yield return new WaitForFixedUpdate();

        if (!isValid)
            yield break;

        AllyRobot[] robots = FindObjectsByType<AllyRobot>(FindObjectsSortMode.None);
        foreach (var robot in robots)
        {
            if (robot != null)
                robot.RequestRetarget();
        }
    }

    static bool IsMarkableEnemy(Collider2D other)
    {
        return other.CompareTag("Enemy") || other.CompareTag("AirEnemy");
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (!isValid)
            return;

        if (!IsMarkableEnemy(other))
            return;

        Enemy enemy = other.GetComponent<Enemy>() ?? other.GetComponentInParent<Enemy>();
        if (enemy == null || !enemy.IsHittable)
            return;

        enemy.isMarked = true;
        markedEnemies.Add(enemy);
    }

    void OnTriggerExit2D(Collider2D other)
    {
        if (!isValid)
            return;

        if (!IsMarkableEnemy(other))
            return;

        Enemy enemy = other.GetComponent<Enemy>() ?? other.GetComponentInParent<Enemy>();
        if (enemy == null)
            return;

        enemy.isMarked = false;
        markedEnemies.Remove(enemy);
    }

    void OnDestroy()
    {
        foreach (var enemy in markedEnemies)
        {
            if (enemy != null)
                enemy.isMarked = false;
        }

        markedEnemies.Clear();
    }
}
