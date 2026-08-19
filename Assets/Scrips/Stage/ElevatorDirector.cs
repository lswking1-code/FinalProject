using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Events;

public enum ElevatorRideState
{
    Idle,
    Ascending,
    FloorEvent,
    WaveActive,
    Complete
}

[Serializable]
public class ElevatorFloorEvent
{
    [Tooltip("行进距离达到该值时停梯并触发本楼层事件")]
    public float triggerDistance = 8f;
    [Tooltip("楼层遭遇；可空，仅触发 onTriggered")]
    public EncounterZone encounter;
    public UnityEvent onTriggered;
}

/// <summary>
/// Stage3 电梯关卡导演：假上升、楼层遭遇、二阶段底板危机。
/// 仅在 ASCENDING 且当前遭遇已清空时累加行进距离，避免提前触发下一层。
/// </summary>
[RequireComponent(typeof(DataDefination))]
public class ElevatorDirector : MonoBehaviour, ISaveable
{
    [Header("行程")]
    [SerializeField, Min(0.1f)] float riseSpeed = 4f;
    [SerializeField] float phase2Distance = 28f;
    [SerializeField] float rideEndDistance = 50f;
    [SerializeField] ElevatorFloorEvent[] floorEvents;

    [Header("锁区 / 镜头")]
    [SerializeField] OneWayAirWallVolume airWalls;
    [SerializeField] Collider2D cameraBounds;
    [SerializeField] bool overrideOrthographicSize = true;
    [SerializeField] float rideOrthographicSize = 6.5f;
    [SerializeField] bool restoreCameraOnComplete;
    [SerializeField] bool unlockAirWallsOnComplete;

    [Header("表现")]
    [SerializeField] ElevatorBackgroundScroller backgroundScroller;
    [SerializeField] ElevatorFloorHazard floorHazard;
    [SerializeField] GameObject[] riseVfx;
    [SerializeField] AudioSource riseAudio;
    [SerializeField] AudioClip riseLoopClip;
    [SerializeField, Range(0f, 1f)] float riseVolume = 0.45f;

    [Header("事件")]
    public UnityEvent OnRideStarted;
    public UnityEvent OnPhase2Started;
    public UnityEvent OnRideCompleted;

    ElevatorRideState state = ElevatorRideState.Idle;
    float travelDistance;
    int nextFloorIndex;
    int activeFloorIndex = -1;
    bool boarded;
    bool phase2Started;
    bool completed;
    CameraControl cameraControl;
    EncounterZone subscribedEncounter;

    public ElevatorRideState State => state;
    public float TravelDistance => travelDistance;
    public bool HasBoarded => boarded;

    bool CanAdvanceDistance =>
        state == ElevatorRideState.Ascending
        && !completed
        && !EncounterZone.HasActiveEncounter
        && activeFloorIndex < 0;

    void Awake()
    {
        SortFloorEvents();
        SetRisePresentation(false);
        if (riseAudio == null)
            riseAudio = GetComponent<AudioSource>();
        if (riseAudio != null)
        {
            riseAudio.playOnAwake = false;
            riseAudio.loop = true;
            riseAudio.spatialBlend = 0f;
        }
    }

    void OnEnable()
    {
        ((ISaveable)this).RegisterSaveData();
        DataManager.instance?.ApplyLoadedData(this);
    }

    void Start()
    {
        DataManager.instance?.ApplyLoadedData(this);
    }

    void OnDisable()
    {
        UnsubscribeActiveEncounter();
        ((ISaveable)this).UnregisterSaveData();
    }

    void Update()
    {
        if (!CanAdvanceDistance)
            return;

        travelDistance += riseSpeed * Time.deltaTime;
        TryEnterPhase2();
        TryTriggerNextFloor();
        TryCompleteRide();
    }

    public void TryBoard(Collider2D playerCollider)
    {
        if (boarded || completed)
            return;

        boarded = true;
        BeginRide(playerCollider, resetProgress: true);
        OnRideStarted?.Invoke();
    }

    void BeginRide(Collider2D playerCollider, bool resetProgress)
    {
        if (resetProgress)
        {
            travelDistance = 0f;
            nextFloorIndex = 0;
            activeFloorIndex = -1;
            phase2Started = false;
            completed = false;
        }

        airWalls?.Activate(playerCollider);
        ApplyRideCamera();
        floorHazard?.SetPhase2Active(phase2Started);
        EnterState(ElevatorRideState.Ascending);
    }

    void ApplyRideCamera()
    {
        if (cameraBounds == null)
            return;

        EnsureCameraControl();
        cameraControl?.SetCameraBounds(
            cameraBounds,
            smooth: true,
            overrideOrthographicSize,
            rideOrthographicSize);
    }

    void TryEnterPhase2()
    {
        if (phase2Started || travelDistance < phase2Distance)
            return;

        phase2Started = true;
        floorHazard?.SetPhase2Active(true);
        OnPhase2Started?.Invoke();
    }

    void TryTriggerNextFloor()
    {
        if (floorEvents == null || nextFloorIndex >= floorEvents.Length)
            return;

        ElevatorFloorEvent floor = floorEvents[nextFloorIndex];
        if (floor == null || travelDistance < floor.triggerDistance)
            return;

        BeginFloorEvent(nextFloorIndex);
    }

    void BeginFloorEvent(int index)
    {
        activeFloorIndex = index;
        EnterState(ElevatorRideState.FloorEvent);

        ElevatorFloorEvent floor = floorEvents[index];
        floor?.onTriggered?.Invoke();

        EncounterZone encounter = floor != null ? floor.encounter : null;
        if (encounter == null)
        {
            FinishActiveFloor();
            return;
        }

        if (encounter.HasCompleted)
        {
            FinishActiveFloor();
            return;
        }

        UnsubscribeActiveEncounter();
        subscribedEncounter = encounter;
        encounter.OnEncounterEnded.AddListener(OnActiveFloorEncounterEnded);
        if (!encounter.IsActive)
            encounter.StartEncounter();
        EnterState(ElevatorRideState.WaveActive);
    }

    void OnActiveFloorEncounterEnded()
    {
        FinishActiveFloor();
    }

    void FinishActiveFloor()
    {
        UnsubscribeActiveEncounter();
        nextFloorIndex = Mathf.Max(nextFloorIndex, activeFloorIndex + 1);
        activeFloorIndex = -1;

        if (completed)
            return;

        EnterState(ElevatorRideState.Ascending);
        TryCompleteRide();
    }

    void TryCompleteRide()
    {
        if (completed || travelDistance < rideEndDistance)
            return;
        if (activeFloorIndex >= 0)
            return;
        if (floorEvents != null && nextFloorIndex < floorEvents.Length)
            return;

        CompleteRide();
    }

    void CompleteRide()
    {
        if (completed)
            return;

        completed = true;
        activeFloorIndex = -1;
        EnterState(ElevatorRideState.Complete);
        floorHazard?.SetPhase2Active(false);

        if (unlockAirWallsOnComplete)
            airWalls?.Deactivate();

        if (restoreCameraOnComplete)
        {
            EnsureCameraControl();
            cameraControl?.RestoreCameraBounds(smooth: true);
        }

        OnRideCompleted?.Invoke();
    }

    void EnterState(ElevatorRideState next)
    {
        if (state == next)
        {
            SetRisePresentation(next == ElevatorRideState.Ascending);
            return;
        }

        state = next;
        SetRisePresentation(next == ElevatorRideState.Ascending);
    }

    void SetRisePresentation(bool rising)
    {
        backgroundScroller?.SetScrolling(rising);

        if (riseVfx != null)
        {
            for (int i = 0; i < riseVfx.Length; i++)
            {
                if (riseVfx[i] != null)
                    riseVfx[i].SetActive(rising);
            }
        }

        if (riseAudio == null)
            return;

        if (rising)
        {
            if (riseLoopClip != null)
                riseAudio.clip = riseLoopClip;
            riseAudio.volume = riseVolume;
            riseAudio.loop = true;
            if (riseAudio.clip != null && !riseAudio.isPlaying)
                riseAudio.Play();
        }
        else if (riseAudio.isPlaying)
        {
            riseAudio.Stop();
        }
    }

    void UnsubscribeActiveEncounter()
    {
        if (subscribedEncounter != null)
        {
            subscribedEncounter.OnEncounterEnded.RemoveListener(OnActiveFloorEncounterEnded);
            subscribedEncounter = null;
        }
    }

    void SortFloorEvents()
    {
        if (floorEvents == null || floorEvents.Length <= 1)
            return;

        Array.Sort(floorEvents, (a, b) =>
        {
            float da = a != null ? a.triggerDistance : 0f;
            float db = b != null ? b.triggerDistance : 0f;
            return da.CompareTo(db);
        });
    }

    void EnsureCameraControl()
    {
        if (cameraControl != null)
            return;
        cameraControl = FindFirstObjectByType<CameraControl>();
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
        if (data == null)
            return;

        if (data.boolSavedData != null)
        {
            data.boolSavedData[ProgressKey("boarded")] = boarded;
            data.boolSavedData[ProgressKey("completed")] = completed;
            data.boolSavedData[ProgressKey("phase2")] = phase2Started;
        }

        if (data.floatSavedData != null)
            data.floatSavedData[ProgressKey("distance")] = travelDistance;

        if (data.intListSavedData != null)
        {
            data.intListSavedData[ProgressKey("index")] = new System.Collections.Generic.List<int>
            {
                nextFloorIndex,
                activeFloorIndex,
                (int)state
            };
        }
    }

    public void LoadSaveData(Data data)
    {
        if (data?.boolSavedData == null)
            return;

        boarded = data.boolSavedData.TryGetValue(ProgressKey("boarded"), out bool savedBoarded) && savedBoarded;
        completed = data.boolSavedData.TryGetValue(ProgressKey("completed"), out bool savedDone) && savedDone;
        phase2Started = data.boolSavedData.TryGetValue(ProgressKey("phase2"), out bool savedPhase) && savedPhase;

        if (data.floatSavedData != null && data.floatSavedData.TryGetValue(ProgressKey("distance"), out float savedDist))
            travelDistance = savedDist;

        nextFloorIndex = 0;
        activeFloorIndex = -1;
        ElevatorRideState savedState = ElevatorRideState.Idle;
        if (data.intListSavedData != null
            && data.intListSavedData.TryGetValue(ProgressKey("index"), out var list)
            && list != null
            && list.Count >= 3)
        {
            nextFloorIndex = Mathf.Max(0, list[0]);
            activeFloorIndex = list[1];
            savedState = (ElevatorRideState)list[2];
        }

        if (!boarded)
        {
            airWalls?.Deactivate();
            EnterState(ElevatorRideState.Idle);
            return;
        }

        StopCoroutine(nameof(ResumeAfterLoad));
        StartCoroutine(ResumeAfterLoad(savedState));
    }

    IEnumerator ResumeAfterLoad(ElevatorRideState savedState)
    {
        yield return null;

        var player = GameObject.FindGameObjectWithTag("Player");
        Collider2D playerCol = player != null ? player.GetComponent<Collider2D>() : null;
        BeginRide(playerCol, resetProgress: false);

        if (completed)
        {
            CompleteRide();
            yield break;
        }

        if (savedState == ElevatorRideState.FloorEvent || savedState == ElevatorRideState.WaveActive)
        {
            int floorIndex = activeFloorIndex >= 0 ? activeFloorIndex : nextFloorIndex;
            if (floorEvents != null && floorIndex >= 0 && floorIndex < floorEvents.Length)
                BeginFloorEvent(floorIndex);
            else
                EnterState(ElevatorRideState.Ascending);
        }
        else
        {
            EnterState(ElevatorRideState.Ascending);
        }
    }
}
