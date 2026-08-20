using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 电梯笼单向空气墙：仅左右墙。玩家进笼后封死两侧；
/// 敌人可从外侧穿入，完全进入后不可再出；敌人弹只能进不能出。
/// </summary>
public class OneWayAirWallVolume : MonoBehaviour
{
    [Tooltip("笼内判定碰撞体，用于敌人完全进入后封门")]
    [SerializeField] Collider2D cageBounds;
    [Tooltip("空气墙根节点（仅左右墙，不要放顶墙）")]
    [SerializeField] GameObject airWallsRoot;
    [SerializeField] bool delaySealAirWalls = true;
    [SerializeField] bool allowEnemiesThroughAirWalls = true;

    static readonly List<OneWayAirWallVolume> s_activeVolumes = new();

    readonly List<Collider2D> airWallColliders = new();
    readonly List<int> airWallOriginalExcludeBits = new();
    readonly List<Collider2D> playerColliders = new();

    bool isActive;
    bool airWallsSealed;
    Coroutine sealAirWallsRoutine;

    public bool IsActive => isActive;
    internal IReadOnlyList<Collider2D> GetAirWallColliders() => airWallColliders;

    public static void PrepareSpawnedEnemyAll(GameObject enemyObject)
    {
        for (int i = s_activeVolumes.Count - 1; i >= 0; i--)
        {
            OneWayAirWallVolume volume = s_activeVolumes[i];
            if (volume == null)
            {
                s_activeVolumes.RemoveAt(i);
                continue;
            }

            volume.PrepareSpawnedEnemy(enemyObject);
        }
    }

    public void Activate(Collider2D playerCollider)
    {
        if (isActive)
            return;

        isActive = true;
        airWallsSealed = false;
        if (!s_activeVolumes.Contains(this))
            s_activeVolumes.Add(this);

        ActivateAirWalls(playerCollider);
    }

    public void Deactivate()
    {
        if (!isActive)
            return;

        isActive = false;
        s_activeVolumes.Remove(this);
        DeactivateAirWalls();
    }

    public void PrepareSpawnedEnemy(GameObject enemyObject)
    {
        if (!isActive || enemyObject == null)
            return;
        if (!allowEnemiesThroughAirWalls || airWallColliders.Count == 0)
            return;

        var gate = enemyObject.GetComponent<EnemyOneWayAirWallGate>();
        if (gate == null)
            gate = enemyObject.AddComponent<EnemyOneWayAirWallGate>();
        gate.Bind(this);
    }

    void OnDestroy()
    {
        s_activeVolumes.Remove(this);
        DeactivateAirWalls();
    }

    internal bool IsEnemyFullyInsideCombatArea(Vector2 worldPoint, Collider2D bodyCollider)
    {
        if (!IsPointInsideCage(worldPoint))
            return false;
        if (bodyCollider != null && IsColliderOverlappingAirWalls(bodyCollider))
            return false;
        return true;
    }

    internal bool IsPointInsideCage(Vector2 worldPoint)
    {
        if (cageBounds == null)
            return true;
        if (cageBounds.OverlapPoint(worldPoint))
            return true;
        return cageBounds.bounds.Contains(worldPoint);
    }

    internal bool IsColliderOverlappingAirWalls(Collider2D body)
    {
        if (body == null)
            return false;

        for (int i = 0; i < airWallColliders.Count; i++)
        {
            var wall = airWallColliders[i];
            if (wall == null || !wall.enabled)
                continue;

            var distance = Physics2D.Distance(body, wall);
            if (distance.isOverlapped || distance.distance <= 0.01f)
                return true;
        }

        return false;
    }

    void ActivateAirWalls(Collider2D playerCollider)
    {
        if (airWallsRoot == null)
            return;

        airWallsRoot.SetActive(true);
        CacheAirWallColliders();
        ApplyPlayerBulletPassthrough();
        PublishActiveAirWalls(true);
        CachePlayerColliders(playerCollider);

        if (!delaySealAirWalls || playerColliders.Count == 0)
        {
            airWallsSealed = true;
            return;
        }

        SetPlayerAirWallCollisionIgnored(true);
        if (sealAirWallsRoutine != null)
            StopCoroutine(sealAirWallsRoutine);
        sealAirWallsRoutine = StartCoroutine(SealAirWallsWhenPlayerClear());
    }

    void DeactivateAirWalls()
    {
        if (sealAirWallsRoutine != null)
        {
            StopCoroutine(sealAirWallsRoutine);
            sealAirWallsRoutine = null;
        }

        SetPlayerAirWallCollisionIgnored(false);
        RestoreAirWallExcludeLayers();
        PublishActiveAirWalls(false);
        playerColliders.Clear();
        airWallsSealed = false;
        airWallColliders.Clear();
        airWallOriginalExcludeBits.Clear();

        if (airWallsRoot != null)
            airWallsRoot.SetActive(false);
    }

    void PublishActiveAirWalls(bool publish)
    {
        for (int i = 0; i < airWallColliders.Count; i++)
        {
            var wall = airWallColliders[i];
            if (wall == null)
                continue;

            if (publish)
                AirWallRegistry.Register(wall, oneWay: true, cage: cageBounds);
            else
                AirWallRegistry.Unregister(wall);
        }
    }

    void CacheAirWallColliders()
    {
        airWallColliders.Clear();
        airWallOriginalExcludeBits.Clear();
        if (airWallsRoot == null)
            return;

        var cols = airWallsRoot.GetComponentsInChildren<Collider2D>(true);
        for (int i = 0; i < cols.Length; i++)
        {
            var col = cols[i];
            if (col == null || !col.enabled || col.isTrigger)
                continue;

            airWallColliders.Add(col);
            airWallOriginalExcludeBits.Add(col.excludeLayers.value);
        }
    }

    void ApplyPlayerBulletPassthrough()
    {
        int playerBullet = LayerMask.NameToLayer("PlayerBullet");
        int specialBullet = LayerMask.NameToLayer("PlayerSpecialBullet");
        int mask = 0;
        if (playerBullet >= 0)
            mask |= 1 << playerBullet;
        if (specialBullet >= 0)
            mask |= 1 << specialBullet;

        for (int i = 0; i < airWallColliders.Count; i++)
        {
            var wall = airWallColliders[i];
            if (wall == null)
                continue;
            wall.excludeLayers = airWallOriginalExcludeBits[i] | mask;
        }
    }

    void RestoreAirWallExcludeLayers()
    {
        int count = Mathf.Min(airWallColliders.Count, airWallOriginalExcludeBits.Count);
        for (int i = 0; i < count; i++)
        {
            var wall = airWallColliders[i];
            if (wall == null)
                continue;
            wall.excludeLayers = airWallOriginalExcludeBits[i];
        }
    }

    void CachePlayerColliders(Collider2D tip)
    {
        playerColliders.Clear();
        if (tip == null)
        {
            var player = GameObject.FindGameObjectWithTag("Player");
            if (player != null)
                tip = player.GetComponent<Collider2D>();
        }

        if (tip == null)
            return;

        var root = tip.attachedRigidbody != null ? tip.attachedRigidbody.gameObject : tip.gameObject;
        var cols = root.GetComponentsInChildren<Collider2D>(true);
        for (int i = 0; i < cols.Length; i++)
        {
            if (cols[i] != null && cols[i].enabled && !cols[i].isTrigger)
                playerColliders.Add(cols[i]);
        }

        if (playerColliders.Count == 0 && tip.enabled)
            playerColliders.Add(tip);
    }

    void SetPlayerAirWallCollisionIgnored(bool ignore)
    {
        for (int p = 0; p < playerColliders.Count; p++)
        {
            var playerCol = playerColliders[p];
            if (playerCol == null)
                continue;

            for (int i = 0; i < airWallColliders.Count; i++)
            {
                var wall = airWallColliders[i];
                if (wall == null)
                    continue;
                Physics2D.IgnoreCollision(playerCol, wall, ignore);
            }
        }
    }

    IEnumerator SealAirWallsWhenPlayerClear()
    {
        yield return new WaitForFixedUpdate();

        while (isActive)
        {
            if (!IsOverlappingAnyAirWall())
            {
                SetPlayerAirWallCollisionIgnored(false);
                airWallsSealed = true;
                sealAirWallsRoutine = null;
                yield break;
            }

            yield return new WaitForFixedUpdate();
        }

        sealAirWallsRoutine = null;
    }

    bool IsOverlappingAnyAirWall()
    {
        for (int p = 0; p < playerColliders.Count; p++)
        {
            var playerCol = playerColliders[p];
            if (playerCol == null)
                continue;

            for (int i = 0; i < airWallColliders.Count; i++)
            {
                var wall = airWallColliders[i];
                if (wall == null || !wall.enabled)
                    continue;

                var distance = Physics2D.Distance(playerCol, wall);
                if (distance.isOverlapped || distance.distance <= 0.01f)
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 敌人单向空气墙：区外 IgnoreCollision，完全进入笼内后恢复碰撞。
    /// </summary>
    class EnemyOneWayAirWallGate : MonoBehaviour
    {
        OneWayAirWallVolume volume;
        readonly List<Collider2D> bodyColliders = new();
        bool sealedInside;

        public void Bind(OneWayAirWallVolume owner)
        {
            volume = owner;
            sealedInside = false;
            CacheBodyColliders();
            EvaluateGate();
        }

        void CacheBodyColliders()
        {
            bodyColliders.Clear();
            var cols = GetComponentsInChildren<Collider2D>(true);
            for (int i = 0; i < cols.Length; i++)
            {
                var col = cols[i];
                if (col == null || !col.enabled || col.isTrigger)
                    continue;
                bodyColliders.Add(col);
            }
        }

        void FixedUpdate()
        {
            if (sealedInside)
                return;

            if (volume == null || !volume.IsActive)
            {
                SetIgnoringWalls(false);
                Destroy(this);
                return;
            }

            EvaluateGate();
        }

        void EvaluateGate()
        {
            if (volume == null)
                return;

            if (bodyColliders.Count == 0)
                CacheBodyColliders();

            bool fullyInside = volume.IsPointInsideCage(transform.position);
            if (fullyInside)
            {
                for (int i = 0; i < bodyColliders.Count; i++)
                {
                    if (volume.IsColliderOverlappingAirWalls(bodyColliders[i]))
                    {
                        fullyInside = false;
                        break;
                    }
                }
            }

            if (fullyInside)
            {
                SetIgnoringWalls(false);
                sealedInside = true;
                return;
            }

            SetIgnoringWalls(true);
        }

        void SetIgnoringWalls(bool ignore)
        {
            if (volume == null)
                return;

            var walls = volume.GetAirWallColliders();
            if (walls == null)
                return;

            for (int b = 0; b < bodyColliders.Count; b++)
            {
                var body = bodyColliders[b];
                if (body == null)
                    continue;

                for (int w = 0; w < walls.Count; w++)
                {
                    var wall = walls[w];
                    if (wall == null)
                        continue;
                    Physics2D.IgnoreCollision(body, wall, ignore);
                }
            }
        }

        void OnDestroy()
        {
            SetIgnoringWalls(false);
            volume = null;
        }
    }
}
