using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 倒计时装置：ToggleSwitch 打开后启动倒计时并程序触发遭遇战；
/// 期间按电梯楼层音效提示进度；结束后开门并 UnlockLock（不停刷怪）。
/// </summary>
[RequireComponent(typeof(DataDefination))]
public class CountdownDevice : MonoBehaviour, ISaveable
{
    const string CompletedKeySuffix = "completed";

    [Header("触发")]
    [SerializeField] ToggleSwitch activationSwitch;
    [Tooltip("关闭后仅响应开关 ON；打开后也可由 Begin() / UnityEvent 启动")]
    [SerializeField] bool listenToSwitch = true;

    [Header("倒计时")]
    [SerializeField, Min(0.1f)] float countdownDuration = 15f;
    [Tooltip("将总时长均分成若干层，每过一层播放一次叮声")]
    [SerializeField, Min(1)] int floorCount = 5;

    [Header("目标")]
    [SerializeField] EncounterZone encounterZone;
    [Tooltip("倒计时结束后开门（AnimatedDestroy.BeginDestroy）")]
    [SerializeField] AnimatedDestroy doorOnComplete;

    [Header("音效")]
    [SerializeField] AudioSource sfxSource;
    [SerializeField] AudioClip startClip;
    [SerializeField, Range(0f, 1f)] float startVolume = 0.8f;
    [SerializeField] AudioClip floorDingClip;
    [SerializeField, Range(0f, 1f)] float floorDingVolume = 0.85f;
    [SerializeField] AudioClip completeClip;
    [SerializeField, Range(0f, 1f)] float completeVolume = 0.9f;

    [Header("事件")]
    [SerializeField] UnityEvent onStarted;
    [SerializeField] UnityEvent onCompleted;

    bool completed;
    bool running;
    float remain;
    int floorsPassed;
    float secondsPerFloor;

    public bool IsRunning => running;
    public bool IsCompleted => completed;
    public float NormalizedRemaining =>
        !running || countdownDuration <= 0f ? 0f : Mathf.Clamp01(remain / countdownDuration);

    void Awake()
    {
        if (sfxSource == null)
            sfxSource = GetComponent<AudioSource>();
        if (sfxSource != null)
        {
            sfxSource.playOnAwake = false;
            sfxSource.loop = false;
            sfxSource.spatialBlend = 0f;
        }
    }

    void OnEnable()
    {
        if (listenToSwitch && activationSwitch != null)
            activationSwitch.onToggled.AddListener(OnSwitchToggled);

        ((ISaveable)this).RegisterSaveData();
        DataManager.instance?.ApplyLoadedData(this);
    }

    void Start()
    {
        // Additive 加载时 OnEnable 里 scene.name 可能仍为空，补一次。
        DataManager.instance?.ApplyLoadedData(this);
    }

    void OnDisable()
    {
        if (activationSwitch != null)
            activationSwitch.onToggled.RemoveListener(OnSwitchToggled);

        ((ISaveable)this).UnregisterSaveData();
    }

    void Update()
    {
        if (!running || completed)
            return;

        remain -= Time.deltaTime;
        TickFloorDings();

        if (remain <= 0f)
        {
            remain = 0f;
            Complete();
        }
    }

    /// <summary>UnityEvent / 开关调用：仅在 ON 时启动。</summary>
    public void OnSwitchToggled(bool on)
    {
        if (on)
            Begin();
    }

    /// <summary>启动倒计时与遭遇战（幂等）。</summary>
    public void Begin()
    {
        if (completed || running)
            return;

        running = true;
        remain = countdownDuration;
        floorsPassed = 0;
        secondsPerFloor = floorCount > 0 ? countdownDuration / floorCount : countdownDuration;

        PlaySfx(startClip, startVolume);

        if (encounterZone != null && !encounterZone.IsActive)
            encounterZone.StartEncounter();

        onStarted?.Invoke();
    }

    void TickFloorDings()
    {
        if (secondsPerFloor <= 0f || floorCount <= 0)
            return;

        // 启动不叮；每跨过一层边界叮一次；最后一层到达交给 completeClip。
        int maxDingFloors = Mathf.Max(0, floorCount - 1);
        float elapsed = countdownDuration - remain;
        int shouldHavePassed = Mathf.Min(maxDingFloors, Mathf.FloorToInt(elapsed / secondsPerFloor));

        while (floorsPassed < shouldHavePassed)
        {
            floorsPassed++;
            PlaySfx(floorDingClip, floorDingVolume);
        }
    }

    void Complete()
    {
        if (completed)
            return;

        running = false;
        completed = true;
        remain = 0f;

        PlaySfx(completeClip, completeVolume);

        if (doorOnComplete != null)
            doorOnComplete.BeginDestroy();

        if (encounterZone != null && encounterZone.IsActive)
            encounterZone.UnlockLock();

        onCompleted?.Invoke();
    }

    void ApplyCompletedState()
    {
        completed = true;
        running = false;
        remain = 0f;
        floorsPassed = Mathf.Max(0, floorCount - 1);

        if (doorOnComplete != null)
            doorOnComplete.BeginDestroy();

        if (activationSwitch != null)
            activationSwitch.SetOn(true, playSfx: false);
    }

    void PlaySfx(AudioClip clip, float volume)
    {
        if (sfxSource == null || clip == null)
            return;

        sfxSource.PlayOneShot(clip, volume);
    }

    public DataDefination GetDataID() => GetComponent<DataDefination>();

    string ProgressKey(string suffix)
    {
        var dataId = GetDataID();
        string id = dataId != null && !string.IsNullOrEmpty(dataId.ID) ? dataId.ID : name;
        string sceneName = gameObject.scene.IsValid() && !string.IsNullOrEmpty(gameObject.scene.name)
            ? gameObject.scene.name
            : name;
        return $"{sceneName}:{id}:{name}:{suffix}";
    }

    public void GetSaveData(Data data)
    {
        if (data?.boolSavedData == null)
            return;

        var dataId = GetDataID();
        if (dataId == null || string.IsNullOrEmpty(dataId.ID))
            return;

        data.boolSavedData[ProgressKey(CompletedKeySuffix)] = completed;
    }

    public void LoadSaveData(Data data)
    {
        if (data?.boolSavedData == null)
            return;

        var dataId = GetDataID();
        if (dataId == null || string.IsNullOrEmpty(dataId.ID))
            return;

        bool wasCompleted = data.boolSavedData.TryGetValue(ProgressKey(CompletedKeySuffix), out bool saved)
            && saved;

        if (wasCompleted)
        {
            ApplyCompletedState();
            return;
        }

        completed = false;
        running = false;
        remain = 0f;
        floorsPassed = 0;
    }
}
