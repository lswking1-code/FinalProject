using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 遭遇战锁区：玩家进入后限制相机 Bounds 并启用空气墙。
/// 刷怪由外部脚本负责；通过 RegisterEnemy 登记的有限敌人清光后可自动结束。
/// EnemyGenerate 中勾选 infiniteRefresh 的敌人不会登记，不影响清敌结算。
/// 机关/独立事件等可 UnityEvent 调用 EndEncounter() 强制结算；
/// 结束后经 OnEncounterEnded → StopSpawning 停止（含无限刷怪）。
/// 敌人空气墙为单向：区外可穿入，进入后锁定不让出区；敌人弹不能穿过空气墙。
/// 可选弹药援助：停留过久且 S/M/L 全空时在固定点刷 BulletBox。
/// </summary>
[RequireComponent(typeof(Collider2D))]
[RequireComponent(typeof(DataDefination))]
public class EncounterZone : MonoBehaviour, ISaveable
{
    [Header("锁区")]
    [Tooltip("本区域相机限制碰撞体（不要使用 Bounds 标签）")]
    [SerializeField] Collider2D encounterBounds;
    [Tooltip("空气墙根节点，启用后阻挡玩家离开区域；敌人弹会命中销毁")]
    [SerializeField] GameObject airWallsRoot;

    [Header("镜头")]
    [Tooltip("勾选后进入本遭遇时覆盖相机 Orthographic Size（更大=看到更多世界）")]
    [SerializeField] bool overrideOrthographicSize;
    [Tooltip("仅在勾选覆盖时生效")]
    [SerializeField] float encounterOrthographicSize = 8f;

    [Header("行为")]
    [SerializeField] bool triggerOnce = true;
    [SerializeField] bool autoEndWhenCleared = true;
    [Tooltip("启用空气墙后先与玩家忽略碰撞，直到玩家不再与空气墙重叠，避免卡在墙外")]
    [SerializeField] bool delaySealAirWalls = true;
    [Tooltip("允许敌人从区外单向穿入空气墙；完全进入后恢复碰撞，不可再穿出")]
    [SerializeField] bool allowEnemiesThroughAirWalls = true;

    [Header("弹药援助")]
    [Tooltip("开启后：遭遇中每隔一段时间检测玩家 S/M/L 是否全空，是则在固定点位刷弹药包")]
    [SerializeField] bool enableAmmoAssist;
    [Tooltip("遭遇开始后每隔多少秒检查一次（首检也需等待此间隔）")]
    [SerializeField] float assistInterval = 25f;
    [Tooltip("援助弹药包预制体（BulletBoxS/M/L，需挂 BulletBox）")]
    [SerializeField] GameObject ammoDropPrefab;
    [Tooltip("固定刷新点；无效或空点会跳过")]
    [SerializeField] Transform[] ammoDropPoints;

    /// <summary>当前激活的遭遇空气墙碰撞体，供敌人弹判定销毁。</summary>
    static readonly HashSet<Collider2D> s_activeAirWalls = new();
    /// <summary>当前激活中的遭遇区，供盟友索敌等按区域过滤。</summary>
    static readonly List<EncounterZone> s_activeZones = new();

    [Header("事件")]
    public UnityEvent OnEncounterStarted;
    public UnityEvent OnEncounterEnded;

    [Header("编辑器显示")]
    [Tooltip("在 Scene 视图中始终绘制遭遇区域（触发区 / 相机 Bounds / 空气墙）")]
    [SerializeField] bool alwaysDrawInEditor = true;
    [Tooltip("在 Scene 视图中绘制开战时相机单屏视野（以遭遇中心为原点）")]
    [SerializeField] bool drawCameraViewportInEditor = true;
    [Tooltip("Game 视图尺寸无效时的回退宽高比")]
    [SerializeField] float previewAspect = 16f / 9f;

    const float DefaultEncounterOrthographicSize = 6.5f;

    readonly HashSet<Character> aliveRegistered = new();
    readonly Dictionary<Character, UnityAction> dieHandlers = new();
    readonly List<Collider2D> airWallColliders = new();
    readonly List<int> airWallOriginalExcludeBits = new();
    readonly List<GameObject> assistSpawned = new();

    bool isActive;
    bool hasCompleted;
    bool hasRegisteredAny;
    bool airWallsSealed;
    int pendingSpawnSources;
    CameraControl cameraControl;
    readonly List<Collider2D> playerColliders = new();
    Coroutine sealAirWallsRoutine;
    Coroutine ammoAssistRoutine;

    public bool IsActive => isActive;
    public bool HasCompleted => hasCompleted;
    public int AliveRegisteredCount => aliveRegistered.Count;

    /// <summary>是否存在进行中的遭遇战。</summary>
    public static bool HasActiveEncounter => s_activeZones.Count > 0;

    /// <summary>是否为当前遭遇战启用中的空气墙碰撞体。</summary>
    public static bool IsAirWallCollider(Collider2D col)
    {
        return col != null && s_activeAirWalls.Contains(col);
    }

    /// <summary>世界坐标是否落在任一激活遭遇区的 EncounterBounds 内。</summary>
    public static bool IsPointInsideAnyActiveEncounter(Vector2 worldPoint)
    {
        for (int i = s_activeZones.Count - 1; i >= 0; i--)
        {
            EncounterZone zone = s_activeZones[i];
            if (zone == null)
            {
                s_activeZones.RemoveAt(i);
                continue;
            }

            if (zone.IsActive && zone.IsPointInsideEncounterBounds(worldPoint))
                return true;
        }

        return false;
    }

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

    void Start()
    {
        // Additive 加载时 OnEnable 里 scene.name 可能仍为空，补一次以免完成旗标对不上。
        DataManager.instance?.ApplyLoadedData(this);
    }

    void OnDisable() => ((ISaveable)this).UnregisterSaveData();

    void OnTriggerEnter2D(Collider2D other)
    {
        if (!other.CompareTag("Player"))
            return;

        StartEncounter(other);
    }

    public void StartEncounter() => StartEncounter(null);

    public void StartEncounter(Collider2D playerCollider)
    {
        if (isActive)
            return;
        if (triggerOnce && hasCompleted)
            return;

        isActive = true;
        hasRegisteredAny = false;
        airWallsSealed = false;
        pendingSpawnSources = 0;
        ClearRegistrations();
        RegisterActiveZone(this);

        SetEncounterBoundsVisible(true);
        ActivateAirWalls(ResolvePlayerCollider(playerCollider));

        EnsureCameraControl();
        if (cameraControl != null && encounterBounds != null)
        {
            cameraControl.SetCameraBounds(
                encounterBounds,
                smooth: true,
                overrideOrthographicSize,
                encounterOrthographicSize);
        }

        // 机器人若未进入遭遇锁区，开战时收回，避免锁区外残留
        TryRecallRobotOutsideEncounter();

        OnEncounterStarted?.Invoke();
        StartAmmoAssist();
    }

    void TryRecallRobotOutsideEncounter()
    {
        var player = GameObject.FindGameObjectWithTag("Player");
        if (player == null)
            return;

        var abilities = player.GetComponent<PlayerAbilities>();
        abilities?.RecallRobotIfOutsideActiveEncounter();
    }

    /// <summary>
    /// 结束遭遇战（清敌自动结束或机关/Timeline 等外部事件均可调用）。
    /// 触发 OnEncounterEnded（通常用于 StopSpawning 停刷）。
    /// </summary>
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
        ApplyCompletedState(invokeEndedEvent, restoreBoundsSmooth: true);
    }

    public void ApplyCompletedState(bool invokeEndedEvent, bool restoreBoundsSmooth)
    {
        if (hasCompleted && !isActive)
        {
            if (triggerOnce)
                SetEnterTriggerEnabled(false);
            return;
        }

        // #region agent log
        AgentDebugLog.Write("C", "EncounterZone.cs:ApplyCompletedState", "ending encounter",
            "{\"sessionId\":\"914a21\",\"zone\":\"" + name + "\",\"wasActive\":" + (isActive ? "true" : "false") + ",\"alive\":" + aliveRegistered.Count + ",\"pending\":" + pendingSpawnSources + ",\"hasRegisteredAny\":" + (hasRegisteredAny ? "true" : "false") + ",\"activeZones\":" + s_activeZones.Count + "}");
        // #endregion
        isActive = false;
        hasCompleted = true;
        pendingSpawnSources = 0;
        UnregisterActiveZone(this);
        StopAmmoAssist();

        ClearRegistrations();
        DeactivateAirWalls();

        EnsureCameraControl();
        cameraControl?.RestoreCameraBounds(restoreBoundsSmooth);

        SetEncounterBoundsVisible(false);
        if (triggerOnce)
            SetEnterTriggerEnabled(false);

        if (invokeEndedEvent)
            OnEncounterEnded?.Invoke();
    }

    /// <summary>
    /// 读档时用于未完成遭遇：清掉进行中的锁区，保持可再次触发。
    /// </summary>
    void ResetIncompleteEncounter()
    {
        hasCompleted = false;
        if (triggerOnce)
            SetEnterTriggerEnabled(true);

        if (!isActive)
            return;

        isActive = false;
        pendingSpawnSources = 0;
        UnregisterActiveZone(this);
        StopAmmoAssist();
        ClearRegistrations();
        DeactivateAirWalls();
        EnsureCameraControl();
        // 读档必须立刻恢复场景 Bounds：平滑过渡会把遭遇区阻尼带到 Persistent 相机上，
        // 视差会在相机尚未回到存档点时采基线。
        cameraControl?.RestoreCameraBounds(smooth: false);
        cameraControl?.SnapCameraToFollowTarget();
        SetEncounterBoundsVisible(false);
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

    void StartAmmoAssist()
    {
        StopAmmoAssist();
        if (!enableAmmoAssist)
            return;

        ammoAssistRoutine = StartCoroutine(AmmoAssistRoutine());
    }

    void StopAmmoAssist()
    {
        if (ammoAssistRoutine != null)
        {
            StopCoroutine(ammoAssistRoutine);
            ammoAssistRoutine = null;
        }

        // 只清跟踪列表，不销毁已落地的援助包
        assistSpawned.Clear();
    }

    IEnumerator AmmoAssistRoutine()
    {
        float interval = Mathf.Max(0.1f, assistInterval);

        while (isActive)
        {
            yield return new WaitForSeconds(interval);
            if (!isActive)
                yield break;

            if (!IsPlayerOutOfAllAmmo())
                continue;

            if (HasLiveAssistDrops())
                continue;

            SpawnAssistAmmoDrops();
        }
    }

    bool IsPlayerOutOfAllAmmo()
    {
        var character = ResolvePlayerCharacter();
        if (character == null)
            return false;

        return character.BulletS <= 0
            && character.BulletM <= 0
            && character.BulletL <= 0;
    }

    Character ResolvePlayerCharacter()
    {
        var player = GameObject.FindGameObjectWithTag("Player");
        if (player == null)
            return null;

        var character = player.GetComponent<Character>();
        if (character == null)
            character = player.GetComponentInParent<Character>();
        if (character == null)
            character = player.GetComponentInChildren<Character>();
        return character;
    }

    bool HasLiveAssistDrops()
    {
        for (int i = assistSpawned.Count - 1; i >= 0; i--)
        {
            if (assistSpawned[i] == null)
                assistSpawned.RemoveAt(i);
        }

        return assistSpawned.Count > 0;
    }

    void SpawnAssistAmmoDrops()
    {
        if (ammoDropPrefab == null)
        {
            Debug.LogWarning("EncounterZone: 弹药援助已启用但 ammoDropPrefab 未配置。", this);
            return;
        }

        if (ammoDropPoints == null || ammoDropPoints.Length == 0)
        {
            Debug.LogWarning("EncounterZone: 弹药援助已启用但 ammoDropPoints 为空。", this);
            return;
        }

        for (int i = 0; i < ammoDropPoints.Length; i++)
        {
            var point = ammoDropPoints[i];
            if (point == null)
                continue;

            var instance = Instantiate(ammoDropPrefab, point.position, point.rotation);
            PickupDelay.Arm(instance);
            EnemySceneCleanup.PlaceInSourceScene(instance, this);
            assistSpawned.Add(instance);
        }
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

    /// <summary>
    /// 遭遇刷怪后调用：给敌人挂单向穿墙门控（区外可入，进区后不可出）。
    /// 有限/无限生成都可调用；不负责 RegisterEnemy。
    /// </summary>
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

    void PublishActiveAirWalls(bool publish)
    {
        for (int i = 0; i < airWallColliders.Count; i++)
        {
            var wall = airWallColliders[i];
            if (wall == null)
                continue;

            if (publish)
                s_activeAirWalls.Add(wall);
            else
                s_activeAirWalls.Remove(wall);
        }
    }

    internal IReadOnlyList<Collider2D> GetAirWallColliders() => airWallColliders;

    internal bool IsEnemyFullyInsideCombatArea(Vector2 worldPoint, Collider2D bodyCollider)
    {
        if (!IsPointInsideEncounterBounds(worldPoint))
            return false;

        if (bodyCollider != null && IsColliderOverlappingAirWalls(bodyCollider))
            return false;

        return true;
    }

    /// <summary>世界坐标是否落在本遭遇区的 EncounterBounds 内。</summary>
    public bool IsPointInsideEncounterBounds(Vector2 worldPoint)
    {
        if (encounterBounds == null)
            return true;

        if (encounterBounds.OverlapPoint(worldPoint))
            return true;

        return encounterBounds.bounds.Contains(worldPoint);
    }

    static void RegisterActiveZone(EncounterZone zone)
    {
        if (zone == null || s_activeZones.Contains(zone))
            return;

        s_activeZones.Add(zone);
    }

    static void UnregisterActiveZone(EncounterZone zone)
    {
        if (zone == null)
            return;

        s_activeZones.Remove(zone);
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

    void SetEnterTriggerEnabled(bool enabled)
    {
        var col = GetComponent<Collider2D>();
        if (col == null || col == encounterBounds)
            return;

        col.enabled = enabled;
    }

    /// <summary>
    /// 登记遭遇战中生成的、计入清敌结算的敌人。
    /// 无限刷新敌人勿调用；区域原本存在的敌人也不要登记。
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
    /// 登记遭遇战中生成的、计入清敌结算的敌人。
    /// 无限刷新敌人勿调用；区域原本存在的敌人也不要登记。
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

    /// <summary>
    /// 敌人单向空气墙：区外/穿墙过程中 IgnoreCollision，完全进入 EncounterBounds 后恢复碰撞锁区。
    /// </summary>
    class EnemyOneWayAirWallGate : MonoBehaviour
    {
        EncounterZone zone;
        readonly List<Collider2D> bodyColliders = new();
        bool sealedInside;

        public void Bind(EncounterZone owner)
        {
            zone = owner;
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

            if (zone == null || !zone.IsActive)
            {
                SetIgnoringWalls(false);
                Destroy(this);
                return;
            }

            EvaluateGate();
        }

        void EvaluateGate()
        {
            if (zone == null)
                return;

            if (bodyColliders.Count == 0)
                CacheBodyColliders();

            Vector2 pos = transform.position;
            bool fullyInside = zone.IsPointInsideEncounterBounds(pos);
            if (fullyInside)
            {
                for (int i = 0; i < bodyColliders.Count; i++)
                {
                    if (zone.IsColliderOverlappingAirWalls(bodyColliders[i]))
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
            if (zone == null)
                return;

            var walls = zone.GetAirWallColliders();
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
            zone = null;
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
        UnregisterActiveZone(this);
        StopAmmoAssist();
        DeactivateAirWalls();
        ClearRegistrations();
    }

    public DataDefination GetDataID() => GetComponent<DataDefination>();

    string ProgressKey(string suffix)
    {
        var dataId = GetDataID();
        string id = dataId != null && !string.IsNullOrEmpty(dataId.ID) ? dataId.ID : name;
        return $"{ResolveSceneName()}:{id}:{name}:{suffix}";
    }

    string ResolveSceneName()
    {
        var scene = gameObject.scene;
        if (scene.IsValid() && !string.IsNullOrEmpty(scene.name))
            return scene.name;

        var active = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        return !string.IsNullOrEmpty(active.name) ? active.name : name;
    }

    public void GetSaveData(Data data)
    {
        if (data?.boolSavedData == null)
            return;

        // 可重复触发的遭遇不写入完成旗标。
        if (!triggerOnce)
            return;

        // 存档时若仍在遭遇中，不刷新完成旗标，沿用上一次在区外存下的进度。
        if (HasActiveEncounter)
            return;

        data.boolSavedData[ProgressKey("completed")] = hasCompleted;
    }

    public void LoadSaveData(Data data)
    {
        if (data?.boolSavedData == null)
            return;

        if (!triggerOnce)
        {
            ResetIncompleteEncounter();
            return;
        }

        bool completed = data.boolSavedData.TryGetValue(ProgressKey("completed"), out bool saved)
            && saved;

        if (completed)
            ApplyCompletedState(invokeEndedEvent: true, restoreBoundsSmooth: false);
        else
            ResetIncompleteEncounter();
    }

    void OnDrawGizmos()
    {
        if (!alwaysDrawInEditor)
            return;

        // 玩家进入触发区（本物体 Collider）
        DrawCollider2DGizmo(
            GetComponent<Collider2D>(),
            new Color(1f, 0.45f, 0.1f, 0.18f),
            new Color(1f, 0.5f, 0.15f, 0.95f));

        // 遭遇锁区 / 相机 Bounds（即便运行时默认关闭，编辑器也常亮）
        if (encounterBounds != null && encounterBounds.gameObject != gameObject)
        {
            DrawCollider2DGizmo(
                encounterBounds,
                new Color(0.15f, 0.75f, 1f, 0.12f),
                new Color(0.2f, 0.85f, 1f, 0.9f));
        }

        // 空气墙：挡住玩家；默认允许敌人从墙外刷点进入
        if (airWallsRoot != null)
        {
            var walls = airWallsRoot.GetComponentsInChildren<Collider2D>(true);
            for (int i = 0; i < walls.Length; i++)
            {
                DrawCollider2DGizmo(
                    walls[i],
                    new Color(1f, 0.2f, 0.25f, 0.2f),
                    new Color(1f, 0.25f, 0.3f, 0.9f));
            }
        }

        // 弹药援助刷新点
        if (enableAmmoAssist && ammoDropPoints != null)
        {
            Gizmos.color = new Color(0.2f, 1f, 0.45f, 0.9f);
            for (int i = 0; i < ammoDropPoints.Length; i++)
            {
                if (ammoDropPoints[i] == null)
                    continue;
                Vector3 p = ammoDropPoints[i].position;
                Gizmos.DrawWireSphere(p, 0.22f);
                Gizmos.DrawLine(p + Vector3.left * 0.18f, p + Vector3.right * 0.18f);
                Gizmos.DrawLine(p + Vector3.up * 0.18f, p + Vector3.down * 0.18f);
            }
        }

        if (drawCameraViewportInEditor)
        {
            Vector3 center = GetEncounterCenter();
            float ortho = GetPreviewOrthographicSize();
            float aspect = GetPreviewAspect();
            DrawCameraViewportGizmo(center, ortho, aspect);
        }
    }

    Vector3 GetEncounterCenter()
    {
        if (encounterBounds != null)
            return encounterBounds.bounds.center;
        return transform.position;
    }

    float GetPreviewOrthographicSize()
    {
        if (overrideOrthographicSize)
            return Mathf.Max(0.01f, encounterOrthographicSize);
        return DefaultEncounterOrthographicSize;
    }

    float GetPreviewAspect()
    {
#if UNITY_EDITOR
        Vector2 gameView = UnityEditor.Handles.GetMainGameViewSize();
        if (gameView.y > 0.01f)
            return gameView.x / gameView.y;
#endif
        if (Application.isPlaying)
        {
            Camera cam = Camera.main;
            if (cam != null && cam.aspect > 0.01f)
                return cam.aspect;
            if (Screen.height > 0)
                return (float)Screen.width / Screen.height;
        }

        return previewAspect > 0.01f ? previewAspect : 16f / 9f;
    }

    void DrawCameraViewportGizmo(Vector3 center, float ortho, float aspect)
    {
        center.z = 0f;
        float height = ortho * 2f;
        float width = height * aspect;
        Vector3 size = new Vector3(width, height, 0.01f);

        Color fill = new Color(0.85f, 0.25f, 1f, 0.08f);
        Color wire = new Color(0.9f, 0.35f, 1f, 0.95f);

        Matrix4x4 old = Gizmos.matrix;
        Gizmos.matrix = Matrix4x4.identity;

        Gizmos.color = fill;
        Gizmos.DrawCube(center, size);
        Gizmos.color = wire;
        Gizmos.DrawWireCube(center, size);

        const float cross = 0.45f;
        Gizmos.DrawLine(center + Vector3.left * cross, center + Vector3.right * cross);
        Gizmos.DrawLine(center + Vector3.up * cross, center + Vector3.down * cross);

        Gizmos.matrix = old;

#if UNITY_EDITOR
        string label = overrideOrthographicSize
            ? $"Camera {ortho:0.##}"
            : $"Camera {ortho:0.##} (default)";
        var style = new GUIStyle(UnityEditor.EditorStyles.boldLabel)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 11
        };
        style.normal.textColor = wire;
        UnityEditor.Handles.Label(center + Vector3.up * (height * 0.5f + 0.35f), label, style);
#endif
    }

    static void DrawCollider2DGizmo(Collider2D col, Color fill, Color wire)
    {
        if (col == null)
            return;

        Matrix4x4 old = Gizmos.matrix;
        Gizmos.matrix = col.transform.localToWorldMatrix;

        if (col is BoxCollider2D box)
        {
            Gizmos.color = fill;
            Gizmos.DrawCube(box.offset, box.size);
            Gizmos.color = wire;
            Gizmos.DrawWireCube(box.offset, box.size);
        }
        else if (col is CircleCollider2D circle)
        {
            Gizmos.matrix = Matrix4x4.identity;
            float scale = Mathf.Max(
                Mathf.Abs(col.transform.lossyScale.x),
                Mathf.Abs(col.transform.lossyScale.y));
            Vector3 worldCenter = col.transform.TransformPoint(circle.offset);
            Gizmos.color = wire;
            Gizmos.DrawWireSphere(worldCenter, circle.radius * scale);
        }
        else if (col is CapsuleCollider2D capsule)
        {
            Gizmos.color = fill;
            Gizmos.DrawCube(capsule.offset, capsule.size);
            Gizmos.color = wire;
            Gizmos.DrawWireCube(capsule.offset, capsule.size);
        }
        else
        {
            Gizmos.matrix = Matrix4x4.identity;
            Bounds b = col.bounds;
            if (b.size.sqrMagnitude > 0.0001f)
            {
                Gizmos.color = wire;
                Gizmos.DrawWireCube(b.center, b.size);
            }
        }

        Gizmos.matrix = old;
    }
}
