using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 死亡区域：玩家与敌人进入 Trigger 后立即死亡；机器人进入后立刻收回。
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class DeathZone : MonoBehaviour
{
    readonly HashSet<int> handledInstanceIds = new();

    void Awake()
    {
        var col = GetComponent<Collider2D>();
        if (col != null && !col.isTrigger)
            Debug.LogWarning("DeathZone: Collider2D 应勾选 Is Trigger。", this);
    }

    void OnTriggerEnter2D(Collider2D other) => TryHandle(other);

    void OnTriggerStay2D(Collider2D other) => TryHandle(other);

    void TryHandle(Collider2D other)
    {
        if (other == null)
            return;

        AllyRobot robot = other.GetComponentInParent<AllyRobot>();
        if (robot != null)
        {
            if (!MarkHandled(robot.gameObject))
                return;

            RecallRobot(robot);
            return;
        }

        Enemy enemy = other.GetComponentInParent<Enemy>();
        if (enemy != null || other.CompareTag("Enemy"))
        {
            Character enemyCharacter = enemy != null
                ? enemy.GetComponent<Character>()
                : other.GetComponentInParent<Character>();
            if (enemyCharacter == null)
                return;
            if (enemyCharacter.IsDead)
            {
                enemyCharacter.Kill();
                return;
            }
            if (!MarkHandled(enemyCharacter.gameObject))
                return;

            enemyCharacter.Kill();
            return;
        }

        Character playerCharacter = other.GetComponentInParent<Character>();
        if (playerCharacter == null || !playerCharacter.CompareTag("Player") || playerCharacter.IsDead)
            return;
        if (!MarkHandled(playerCharacter.gameObject))
            return;

        playerCharacter.Kill();
        PlaySessionRecorder.Instance?.RecordSceneHazardDeath("DeathZone");
    }

    bool MarkHandled(GameObject go)
    {
        if (go == null)
            return false;

        int id = go.GetInstanceID();
        if (handledInstanceIds.Contains(id))
            return false;

        handledInstanceIds.Add(id);
        return true;
    }

    static void RecallRobot(AllyRobot robot)
    {
        if (robot == null)
            return;

        PlayerAbilities abilities = null;
        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player != null)
            abilities = player.GetComponent<PlayerAbilities>();

        if (abilities != null && abilities.OwnsRobot(robot))
        {
            abilities.RecallRobot();
            return;
        }

        if (robot != null)
            Destroy(robot.gameObject);
    }
}
