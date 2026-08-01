using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 遭遇战锁区：玩家进入后限制相机 Bounds 并启用空气墙。
/// 刷怪由外部脚本负责；通过 RegisterEnemy 登记遭遇中生成的敌人，清敌后可自动结束。
/// 也可由外部调用 EndEncounter 手动结束（事件型遭遇战）。
/// </summary>
[RequireComponent(typeof(Collider2D))]
[RequireComponent(typeof(DataDefination))]
public class EncounterZone : MonoBehaviour, ISaveable
{
    [Header("锁区")]
    [Tooltip("本区域相机限制碰撞体（不要使用 Bounds 标签）")]
    [SerializeField] Collider2D encounterBounds;
    [Tooltip("空气墙根节点，启用后阻挡玩家离开区域")]
    [SerializeField] GameObject airWallsRoot;

    [Header("行为")]
    [SerializeField] bool triggerOnce = true;
    [SerializeField] bool autoEndWhenCleared = true;
    [Tooltip("启用空气墙后先与玩家忽略碰撞，直到玩家不再与空气墙重叠，避免卡在墙外")]
    [SerializeField] bool delaySealAirWalls = true;

    [Header("事件")]
    public UnityEvent OnEncounterStarted;
    public UnityEvent OnEncounterEnded;

    readonly HashSet<Character> aliveRegistered = new();
    readonly Dictionary<Character, UnityAction> dieHandlers = new();
    readonly List<Collider2D> airWallColliders = new();

    bool isActive;
    bool hasCompleted;
    bool hasRegisteredAny;
    bool airWallsSealed;
    int pendingSpawnSources;
    CameraControl cameraControl;
    readonly List<Collider2D> playerColliders = new();
    Coroutine sealAirWallsRoutine;

    public bool IsActive => isActive;
    public bool HasCompleted => hasCompleted;
    public int AliveRegisteredCount => aliveRegistered.Count;

    void Awake()
    {
        var col = GetComponent<Collider2D>();
        if (col != null && !col.isTrigger)
            Debug.LogWarning("EncounterZone: 进入检测 Collider 建议勾选 Is Trigger。", this);

        if (encounterBounds != null && encounterBounds.gameObject == gameObject)
        {
            Debug.LogWarning(
                "EncounterZone: Encounter Bounds 不应使用本物体上的 Collider。请单独建子物体 EncounterBounds。",
                this);
        }

        SetEncounterBoundsVisible(false);
        if (airWallsRoot != null)
            airWallsRoot.SetActive(false);
    }

    void OnEnable()
    {
        ((ISaveable)this).RegisterSaveData();
        DataManager.instance?.ApplyLoadedData(this);
    }

    void OnDisable() => ((ISaveable)this).UnregisterSaveData();

    void OnTriggerEnter2D(Collider2D other)
    {
        // #region agent log
        AgentDebugLog.Write("C", "EncounterZone.cs:OnTriggerEnter2D", "trigger enter",
            "{\"zone\":\"" + name + "\",\"otherTag\":\"" + other.tag + "\",\"isPlayer\":" + (other.CompareTag("Player") ? "true" : "false") + "}");
        // #endregion
        if (!other.CompareTag("Player"))
            return;

        StartEncounter(other);
    }

    public void StartEncounter() => StartEncounter(null);

    public void StartEncounter(Collider2D playerCollider)
    {
        // #region agent log
        int listenerCount = OnEncounterStarted != null ? OnEncounterStarted.GetPersistentEventCount() : 0;
        string target0 = listenerCount > 0 ? (OnEncounterStarted.GetPersistentTarget(0) != null ? OnEncounterStarted.GetPersistentTarget(0).GetType().Name + ":" + OnEncounterStarted.GetPersistentMethodName(0) : "NULL_TARGET") : "NO_LISTENERS";
        // #endregion
        if (isActive)
        {
            // #region agent log
            AgentDebugLog.Write("B", "EncounterZone.cs:StartEncounter", "early return isActive",
                "{\"zone\":\"" + name + "\",\"isActive\":true,\"hasCompleted\":" + (hasCompleted ? "true" : "false") + ",\"listenerCount\":" + listenerCount + ",\"target0\":\"" + target0 + "\"}");
            // #endregion
            return;
        }
        if (triggerOnce && hasCompleted)
        {
            // #region agent log
            AgentDebugLog.Write("B", "EncounterZone.cs:StartEncounter", "early return triggerOnce+completed",
                "{\"zone\":\"" + name + "\",\"triggerOnce\":true,\"hasCompleted\":true,\"listenerCount\":" + listenerCount + ",\"target0\":\"" + target0 + "\"}");
            // #endregion
            return;
        }

        isActive = true;
        hasRegisteredAny = false;
        airWallsSealed = false;
        pendingSpawnSources = 0;
        ClearRegistrations();

        SetEncounterBoundsVisible(true);
        ActivateAirWalls(ResolvePlayerCollider(playerCollider));

        EnsureCameraControl();
        if (cameraControl != null && encounterBounds != null)
            cameraControl.SetCameraBounds(encounterBounds);

        // #region agent log
        AgentDebugLog.Write("A", "EncounterZone.cs:StartEncounter", "invoking OnEncounterStarted",
            "{\"zone\":\"" + name + "\",\"listenerCount\":" + listenerCount + ",\"target0\":\"" + target0 + "\"}");
        // #endregion
        OnEncounterStarted?.Invoke();
    }

    public void EndEncounter()
    {
        if (!isActive)
            return;

        ApplyCompletedState(invokeEndedEvent: true);
    }

    /// <summary>
    /// 标记遭遇已完成并恢复锁区表现。Load 时可调用；OnEncounterEnded 监听需幂等。
    /// </summary>
    public void ApplyCompletedState(bool invokeEndedEvent)
    {
        isActive = false;
        hasCompleted = true;
        pendingSpawnSources = 0;

        ClearRegistrations();
        DeactivateAirWalls();

        EnsureCameraControl();
        cameraControl?.RestoreCameraBounds();

        SetEncounterBoundsVisible(false);

        if (invokeEndedEvent)
            OnEncounterEnded?.Invoke();
    }

    /// <summary>
    /// 刷怪源开始刷怪时调用；全部波次完成前不会因清场自动结束。
    /// </summary>
    public void NotifySpawningStarted()
    {
        pendingSpawnSources++;
    }

    /// <summary>
    /// 刷怪源全部波次结束后调用；若场上已无敌再尝试自动结束。
    /// </summary>
    public void NotifySpawningCompleted()
    {
        pendingSpawnSources = Mathf.Max(0, pendingSpawnSources - 1);
        TryAutoEnd();
    }

    Collider2D ResolvePlayerCollider(Collider2D playerCollider)
    {
        if (playerCollider != null)
            return playerCollider;

        var player = GameObject.FindGameObjectWithTag("Player");
        if (player == null)
            return null;

        return player.GetComponent<Collider2D>();
    }

    void ActivateAirWalls(Collider2D playerCollider)
    {
        if (airWallsRoot == null)
            return;

        airWallsRoot.SetActive(true);
        CacheAirWallColliders();
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
        playerColliders.Clear();
        airWallsSealed = false;
        airWallColliders.Clear();

        if (airWallsRoot != null)
            airWallsRoot.SetActive(false);
    }

    IEnumerator SealAirWallsWhenPlayerClear()
    {
        // 等一帧让空气墙碰撞体完成启用
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

    void CachePlayerColliders(Collider2D tip)
    {
        playerColliders.Clear();
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

    void CacheAirWallColliders()
    {
        airWallColliders.Clear();
        if (airWallsRoot == null)
            return;

        var cols = airWallsRoot.GetComponentsInChildren<Collider2D>(true);
        for (int i = 0; i < cols.Length; i++)
        {
            if (cols[i] != null && cols[i].enabled && !cols[i].isTrigger)
                airWallColliders.Add(cols[i]);
        }
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
    /// 启用/隐藏区域相机 Bounds。若 Bounds 挂在本物体上则只开关 Collider，避免把遭遇区整棵关掉。
    /// </summary>
    void SetEncounterBoundsVisible(bool visible)
    {
        if (encounterBounds == null)
            return;

        if (encounterBounds.gameObject == gameObject)
        {
            encounterBounds.enabled = true;
            return;
        }

        encounterBounds.gameObject.SetActive(visible);
    }

    /// <summary>
    /// 登记遭遇战中生成的敌人。区域原本存在的敌人不要登记。
    /// </summary>
    public void RegisterEnemy(GameObject enemyObject)
    {
        if (enemyObject == null)
            return;

        var character = enemyObject.GetComponent<Character>();
        if (character == null)
            character = enemyObject.GetComponentInChildren<Character>();
        if (character == null)
            character = enemyObject.GetComponentInParent<Character>();

        RegisterEnemy(character);
    }

    /// <summary>
    /// 登记遭遇战中生成的敌人。区域原本存在的敌人不要登记。
    /// </summary>
    public void RegisterEnemy(Character character)
    {
        if (!isActive || character == null || character.IsDead)
            return;
        if (aliveRegistered.Contains(character))
            return;

        hasRegisteredAny = true;
        aliveRegistered.Add(character);

        UnityAction handler = () => OnRegisteredEnemyDied(character);
        dieHandlers[character] = handler;
        character.OnDie.AddListener(handler);

        var tracker = character.gameObject.GetComponent<EncounterEnemyTracker>();
        if (tracker == null)
            tracker = character.gameObject.AddComponent<EncounterEnemyTracker>();
        tracker.Bind(this, character);
    }

    void OnRegisteredEnemyDied(Character character)
    {
        UnregisterEnemy(character);
        TryAutoEnd();
    }

    internal void NotifyEnemyDestroyed(Character character)
    {
        if (!aliveRegistered.Contains(character) && (character == null || !dieHandlers.ContainsKey(character)))
            return;

        UnregisterEnemy(character);
        TryAutoEnd();
    }

    void UnregisterEnemy(Character character)
    {
        aliveRegistered.Remove(character);

        if (dieHandlers.TryGetValue(character, out var handler))
        {
            if (character != null)
                character.OnDie.RemoveListener(handler);
            dieHandlers.Remove(character);
        }

        if (character != null)
        {
            var tracker = character.GetComponent<EncounterEnemyTracker>();
            if (tracker != null)
                tracker.Unbind(this);
        }
    }

    void TryAutoEnd()
    {
        if (!autoEndWhenCleared || !isActive)
            return;
        if (pendingSpawnSources > 0)
            return;
        if (!hasRegisteredAny)
            return;
        if (aliveRegistered.Count > 0)
            return;

        EndEncounter();
    }

    void ClearRegistrations()
    {
        var snapshot = new List<Character>(aliveRegistered);
        foreach (var character in snapshot)
            UnregisterEnemy(character);

        dieHandlers.Clear();
        aliveRegistered.Clear();
    }

    /// <summary>
    /// 敌人销毁时通知遭遇区，避免未走 OnDie 时存活计数卡住。
    /// </summary>
    class EncounterEnemyTracker : MonoBehaviour
    {
        EncounterZone zone;
        Character character;

        public void Bind(EncounterZone owner, Character target)
        {
            zone = owner;
            character = target;
        }

        public void Unbind(EncounterZone owner)
        {
            if (zone == owner)
                zone = null;
        }

        void OnDestroy()
        {
            zone?.NotifyEnemyDestroyed(character);
        }
    }

    void EnsureCameraControl()
    {
        if (cameraControl != null)
            return;

        cameraControl = FindFirstObjectByType<CameraControl>();
    }

    void OnDestroy()
    {
        DeactivateAirWalls();
        ClearRegistrations();
    }

    public DataDefination GetDataID() => GetComponent<DataDefination>();

    string ProgressKey(string suffix)
    {
        var dataId = GetDataID();
        string id = dataId != null && !string.IsNullOrEmpty(dataId.ID) ? dataId.ID : name;
        return $"{gameObject.scene.name}:{id}:{name}:{suffix}";
    }

    public void GetSaveData(Data data)
    {
        var dataId = GetDataID();
        if (dataId == null || string.IsNullOrEmpty(dataId.ID))
            return;

        data.boolSavedData[ProgressKey("completed")] = hasCompleted;
    }

    public void LoadSaveData(Data data)
    {
        var dataId = GetDataID();
        if (dataId == null || string.IsNullOrEmpty(dataId.ID))
            return;

        if (!data.boolSavedData.TryGetValue(ProgressKey("completed"), out bool completed) || !completed)
            return;

        ApplyCompletedState(invokeEndedEvent: true);
    }

    void OnDrawGizmosSelected()
    {
        var col = GetComponent<Collider2D>();
        if (col == null)
            return;

        Gizmos.color = new Color(1f, 0.4f, 0.1f, 0.35f);
        if (col is BoxCollider2D box)
        {
            Matrix4x4 old = Gizmos.matrix;
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawCube(box.offset, box.size);
            Gizmos.matrix = old;
        }
        else
        {
            Gizmos.DrawWireSphere(col.bounds.center, Mathf.Max(col.bounds.extents.x, col.bounds.extents.y));
        }
    }
}
