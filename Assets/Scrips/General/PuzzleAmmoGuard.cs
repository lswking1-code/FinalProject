using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 解谜区弹药安全网：玩家在区内且缺少指定弹药、谜题未完成时，
/// 按冷却刷弱敌；击杀后通过 ApplyDropOverride 掉落对应弹药包（不直接刷包）。
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class PuzzleAmmoGuard : MonoBehaviour
{
    [Header("需求弹药")]
    [SerializeField] AmmoType requiredAmmo = AmmoType.M;

    [Header("援助刷怪")]
    [Tooltip("弱敌预制体；手枪 / 空手近战应可击杀")]
    [SerializeField] GameObject assistEnemyPrefab;
    [Tooltip("敌人死亡时掉落的弹药包（BulletBoxS/M/L）")]
    [SerializeField] GameObject ammoDropPrefab;
    [SerializeField] Transform[] spawnPoints;
    [SerializeField, Min(0.5f)] float assistInterval = 10f;
    [Tooltip("进入区域后首次检测前等待（秒）；0 表示与 assistInterval 相同")]
    [SerializeField, Min(0f)] float firstCheckDelay = 3f;

    [Header("谜题完成条件")]
    [SerializeField] BoundDevice boundDevice;
    [SerializeField] EnergyNode[] energyNodes;

    [Header("编辑器")]
    [SerializeField] bool alwaysDrawGizmos = true;

    readonly List<Enemy> assistEnemies = new();
    readonly List<Collider2D> playerOverlaps = new();

    Collider2D zoneCollider;
    Coroutine assistRoutine;
    int nextSpawnIndex;
    bool puzzleCompleted;
    bool playerInside;
    float nextSpawnAllowedTime;

    void Awake()
    {
        zoneCollider = GetComponent<Collider2D>();
        if (zoneCollider != null && !zoneCollider.isTrigger)
            Debug.LogWarning("PuzzleAmmoGuard: Collider2D 建议勾选 Is Trigger。", this);
        nextSpawnAllowedTime = Time.time + Mathf.Max(0f, firstCheckDelay);
    }

    void OnEnable()
    {
        if (!puzzleCompleted)
            assistRoutine = StartCoroutine(AssistLoop());
    }

    void OnDisable()
    {
        if (assistRoutine != null)
        {
            StopCoroutine(assistRoutine);
            assistRoutine = null;
        }
    }

    void Update()
    {
        if (!puzzleCompleted && IsPuzzleComplete())
            MarkPuzzleCompleted();
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (!IsPlayerCollider(other))
            return;
        if (!playerOverlaps.Contains(other))
            playerOverlaps.Add(other);
        bool wasInside = playerInside;
        playerInside = playerOverlaps.Count > 0;
        // 刚进入时允许较快首检，但仍尊重 firstCheckDelay 起点
        if (!wasInside && playerInside)
            nextSpawnAllowedTime = Mathf.Min(nextSpawnAllowedTime, Time.time + 0.5f);
    }

    void OnTriggerExit2D(Collider2D other)
    {
        if (!IsPlayerCollider(other))
            return;
        playerOverlaps.Remove(other);
        playerInside = playerOverlaps.Count > 0;
    }

    /// <summary>可供门 / BoundDevice UnityEvent 手动标记完成。</summary>
    public void MarkPuzzleCompleted()
    {
        puzzleCompleted = true;
        if (assistRoutine != null)
        {
            StopCoroutine(assistRoutine);
            assistRoutine = null;
        }
    }

    IEnumerator AssistLoop()
    {
        var wait = new WaitForSeconds(0.5f);
        float cooldown = Mathf.Max(0.5f, assistInterval);

        while (!puzzleCompleted)
        {
            if (IsPuzzleComplete())
            {
                MarkPuzzleCompleted();
                yield break;
            }

            if (Time.time >= nextSpawnAllowedTime
                && playerInside
                && IsPlayerOutOfRequiredAmmo()
                && !HasLiveAssistEnemy()
                && !HasLiveRequiredAmmoDrop())
            {
                SpawnAssistEnemy();
                nextSpawnAllowedTime = Time.time + cooldown;
            }

            yield return wait;
        }
    }

    bool IsPuzzleComplete()
    {
        if (puzzleCompleted)
            return true;

        if (boundDevice != null && boundDevice.IsPermanentlyActive)
            return true;

        if (AllNodesHeld())
            return true;

        return false;
    }

    bool AllNodesHeld()
    {
        if (energyNodes == null || energyNodes.Length == 0)
            return false;

        for (int i = 0; i < energyNodes.Length; i++)
        {
            if (energyNodes[i] == null || !energyNodes[i].IsHeld)
                return false;
        }

        return true;
    }

    bool IsPlayerOutOfRequiredAmmo()
    {
        var character = ResolvePlayerCharacter();
        if (character == null)
            return false;

        if (character.GetAmmo(requiredAmmo) > 0)
            return false;

        return !HasMatchingSpecialRound(character);
    }

    bool HasMatchingSpecialRound(Character character)
    {
        var magazine = character.GetComponent<SpecialMagazine>()
            ?? character.GetComponentInChildren<SpecialMagazine>();
        if (magazine == null || magazine.Count <= 0)
            return false;

        SpecialAmmoType needed = requiredAmmo switch
        {
            AmmoType.S => SpecialAmmoType.S,
            AmmoType.M => SpecialAmmoType.M,
            AmmoType.L => SpecialAmmoType.L,
            _ => SpecialAmmoType.M,
        };

        foreach (var round in magazine.EnumerateRounds())
        {
            if (round == needed)
                return true;
        }

        return false;
    }

    Character ResolvePlayerCharacter()
    {
        var player = GameObject.FindGameObjectWithTag("Player");
        if (player == null)
            return null;

        return player.GetComponent<Character>()
            ?? player.GetComponentInParent<Character>()
            ?? player.GetComponentInChildren<Character>();
    }

    bool HasLiveAssistEnemy()
    {
        for (int i = assistEnemies.Count - 1; i >= 0; i--)
        {
            var enemy = assistEnemies[i];
            if (enemy == null || enemy.isDead)
            {
                assistEnemies.RemoveAt(i);
                continue;
            }
        }

        return assistEnemies.Count > 0;
    }

    bool HasLiveRequiredAmmoDrop()
    {
        var boxes = Object.FindObjectsByType<BulletBox>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < boxes.Length; i++)
        {
            var box = boxes[i];
            if (box == null || box.AmmoType != requiredAmmo)
                continue;
            if (IsWorldPointInZone(box.transform.position))
                return true;
        }

        return false;
    }

    bool IsWorldPointInZone(Vector3 worldPos)
    {
        if (zoneCollider == null)
            return true;

        return zoneCollider.OverlapPoint(worldPos);
    }

    void SpawnAssistEnemy()
    {
        if (assistEnemyPrefab == null)
        {
            Debug.LogWarning("PuzzleAmmoGuard: assistEnemyPrefab 未配置。", this);
            return;
        }

        if (ammoDropPrefab == null)
        {
            Debug.LogWarning("PuzzleAmmoGuard: ammoDropPrefab 未配置。", this);
            return;
        }

        Transform point = ResolveSpawnPoint();
        Vector3 pos = point != null ? point.position : transform.position;
        Quaternion rot = point != null ? point.rotation : Quaternion.identity;

        var instance = Instantiate(assistEnemyPrefab, pos, rot);
        EnemySceneCleanup.PlaceInSourceScene(instance, this);

        var enemy = instance.GetComponent<Enemy>() ?? instance.GetComponentInChildren<Enemy>();
        if (enemy == null)
        {
            Debug.LogWarning("PuzzleAmmoGuard: 援助预制体上找不到 Enemy。", this);
            Destroy(instance);
            return;
        }

        enemy.MarkAsRuntimeSpawned();
        enemy.ApplyDropOverride(dropAmmo: true, ammoDropPrefab, dropHealth: false, healthPrefab: null);
        assistEnemies.Add(enemy);
    }

    Transform ResolveSpawnPoint()
    {
        if (spawnPoints == null || spawnPoints.Length == 0)
            return null;

        for (int attempt = 0; attempt < spawnPoints.Length; attempt++)
        {
            int index = (nextSpawnIndex + attempt) % spawnPoints.Length;
            if (spawnPoints[index] != null)
            {
                nextSpawnIndex = (index + 1) % spawnPoints.Length;
                return spawnPoints[index];
            }
        }

        return null;
    }

    static bool IsPlayerCollider(Collider2D col)
    {
        if (col == null)
            return false;
        if (col.CompareTag("Player"))
            return true;
        var character = col.GetComponentInParent<Character>();
        return character != null && character.CompareTag("Player");
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        var col = GetComponent<Collider2D>();
        if (col != null && !col.isTrigger)
            Debug.LogWarning("PuzzleAmmoGuard: Collider2D 建议勾选 Is Trigger。", this);

        if (assistEnemyPrefab == null)
            Debug.LogWarning("PuzzleAmmoGuard: 未配置 assistEnemyPrefab。", this);
        if (ammoDropPrefab == null)
            Debug.LogWarning("PuzzleAmmoGuard: 未配置 ammoDropPrefab。", this);
        if (spawnPoints == null || spawnPoints.Length == 0)
            Debug.LogWarning("PuzzleAmmoGuard: spawnPoints 为空，将在自身位置刷怪。", this);
    }

    void OnDrawGizmos()
    {
        if (!alwaysDrawGizmos)
            return;
        DrawAssistGizmos();
    }

    void OnDrawGizmosSelected() => DrawAssistGizmos();

    void DrawAssistGizmos()
    {
        Gizmos.color = new Color(1f, 0.92f, 0.2f, 0.9f);
        var col = GetComponent<Collider2D>();
        if (col is BoxCollider2D box)
        {
            Matrix4x4 old = Gizmos.matrix;
            Gizmos.matrix = Matrix4x4.TRS(box.transform.TransformPoint(box.offset), box.transform.rotation, box.transform.lossyScale);
            Gizmos.DrawWireCube(Vector3.zero, box.size);
            Gizmos.matrix = old;
        }

        if (spawnPoints == null)
            return;

        Gizmos.color = new Color(1f, 0.45f, 0.2f, 0.9f);
        for (int i = 0; i < spawnPoints.Length; i++)
        {
            if (spawnPoints[i] == null)
                continue;
            Vector3 p = spawnPoints[i].position;
            Gizmos.DrawWireSphere(p, 0.28f);
            Gizmos.DrawLine(p + Vector3.left * 0.35f, p + Vector3.right * 0.35f);
            Gizmos.DrawLine(p + Vector3.up * 0.35f, p + Vector3.down * 0.35f);
        }
    }
#endif
}
