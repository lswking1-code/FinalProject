using FMODUnity;
using TMPro;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 倒计时装置：ToggleSwitch 打开后启动倒计时并程序触发遭遇战；
/// 期间按电梯楼层音效提示进度；结束后开门并 EndEncounter（停刷，场上敌人保留）。
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
    [Tooltip("倒计时结束后结束遭遇并停刷；关闭则只 UnlockLock，刷怪继续")]
    [SerializeField] bool endEncounterOnComplete = true;

    [Header("FMOD 音效")]
    [SerializeField] EventReference startEvent;
    [SerializeField] EventReference tickEvent;
    [SerializeField] EventReference floorDingEvent;
    [SerializeField] EventReference completeEvent;

    [Header("门上倒计时")]
    [SerializeField] bool showCountdown;
    [SerializeField] Vector3 displayWorldOffset = new Vector3(-1.5f, -6.5f, -0.1f);
    [SerializeField] Sprite[] chargeSprites;
    [SerializeField] TMP_FontAsset countdownFont;
    [SerializeField] Transform displayRoot;
    [SerializeField] SpriteRenderer chargeVisual;
    [SerializeField] TextMeshPro countdownText;
    int lastDisplayedSecond = -1;

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
        if (doorOnComplete == null)
            doorOnComplete = GetComponent<AnimatedDestroy>();
        CreateDisplay();
        UpdateDisplay();
    }

    void OnEnable()
    {
        if (listenToSwitch && activationSwitch != null)
            activationSwitch.onToggled.AddListener(OnSwitchToggled);

        ((ISaveable)this).RegisterSaveData();
        DataManager.instance?.ApplyLoadedData(this);
        if (listenToSwitch && activationSwitch != null && activationSwitch.IsOn)
            Begin();
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
        AdvanceCountdown(Time.deltaTime);
    }

    void AdvanceCountdown(float deltaTime)
    {
        if (!running || completed)
            return;

        int previousSecond = Mathf.CeilToInt(remain);
        remain = Mathf.Max(0f, remain - deltaTime);
        int currentSecond = Mathf.CeilToInt(Mathf.Max(0f, remain));
        if (currentSecond > 0 && currentSecond != previousSecond)
            FmodAudio.Play(tickEvent, transform.position);
        TickFloorDings();
        UpdateDisplay();

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

        FmodAudio.Play(startEvent, transform.position);
        UpdateDisplay();

        if (encounterZone != null && !encounterZone.IsActive)
            encounterZone.StartEncounter();

        onStarted?.Invoke();
    }

    void TickFloorDings()
    {
        if (secondsPerFloor <= 0f || floorCount <= 0)
            return;

        // 启动不叮；每跨过一层边界叮一次；最后一层到达交给 completeEvent。
        int maxDingFloors = Mathf.Max(0, floorCount - 1);
        float elapsed = countdownDuration - remain;
        int shouldHavePassed = Mathf.Min(maxDingFloors, Mathf.FloorToInt(elapsed / secondsPerFloor));

        while (floorsPassed < shouldHavePassed)
        {
            floorsPassed++;
            FmodAudio.Play(floorDingEvent, transform.position);
        }
    }

    void Complete()
    {
        if (completed)
            return;

        running = false;
        completed = true;
        remain = 0f;

        FmodAudio.Play(completeEvent, transform.position);
        UpdateDisplay();

        if (doorOnComplete != null)
            doorOnComplete.BeginDestroy();

        FinishLinkedEncounter();

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

        FinishLinkedEncounter();

        UpdateDisplay();
        if (activationSwitch != null)
            activationSwitch.SetOn(true, playSfx: false);
    }

    void FinishLinkedEncounter()
    {
        if (encounterZone == null)
            return;

        if (encounterZone.IsActive)
        {
            if (endEncounterOnComplete)
                encounterZone.EndEncounter();
            else
                encounterZone.UnlockLock();
            return;
        }

        if (encounterZone.IsPendingStart)
            encounterZone.CancelPendingStart();
    }

    void CreateDisplay()
    {
        if (!showCountdown || (displayRoot != null && chargeVisual != null && countdownText != null)) return;
        SpriteRenderer artwork = null;
        foreach (var candidate in GetComponentsInChildren<SpriteRenderer>())
        {
            if (candidate.enabled && (artwork == null ||
                SortingLayer.GetLayerValueFromID(candidate.sortingLayerID) >
                SortingLayer.GetLayerValueFromID(artwork.sortingLayerID)))
                artwork = candidate;
        }
        displayRoot = new GameObject("ElevatorCountdownDisplay").transform;
        displayRoot.position = transform.position + displayWorldOffset;
        // Preserve world orientation and size under the rotated, stretched door.
        displayRoot.SetParent(transform, true);
        var icon = new GameObject("ChargeIndicator");
        icon.transform.SetParent(displayRoot, false);
        icon.transform.localPosition = new Vector3(-1.4f, 0f, 0f);
        chargeVisual = icon.AddComponent<SpriteRenderer>();
        chargeVisual.sortingLayerID = SortingLayer.NameToID("Default");
        chargeVisual.sortingOrder = 100;
        var label = new GameObject("SecondsRemaining");
        label.transform.SetParent(displayRoot, false);
        label.transform.localPosition = new Vector3(0.6f, 0f, -0.01f);
        countdownText = label.AddComponent<TextMeshPro>();
        countdownText.font = countdownFont != null ? countdownFont : TMP_Settings.defaultFontAsset;
        countdownText.fontSize = 9;
        countdownText.alignment = TextAlignmentOptions.Center;
        countdownText.rectTransform.sizeDelta = new Vector2(3.2f, 1.4f);
        countdownText.GetComponent<MeshRenderer>().sortingOrder = 101;
        // Use the same sorting layer as the door artwork, above its renderers.
        if (artwork != null)
        {
            chargeVisual.sortingLayerID = artwork.sortingLayerID;
            chargeVisual.sortingOrder = Mathf.Max(100, artwork.sortingOrder + 1);
            countdownText.GetComponent<MeshRenderer>().sortingLayerID = artwork.sortingLayerID;
            countdownText.GetComponent<MeshRenderer>().sortingOrder = chargeVisual.sortingOrder + 1;
        }
    }

    void UpdateDisplay()
    {
        if (countdownText == null) return;
        int seconds = Mathf.CeilToInt(running ? remain : completed ? 0f : countdownDuration);
        if (seconds != lastDisplayedSecond || !running)
        {
            lastDisplayedSecond = seconds;
            countdownText.text = completed ? "OPEN" : seconds.ToString("00");
            countdownText.color = completed ? Color.green : running && seconds <= 10
                ? new Color(1f, 0.35f, 0.15f) : new Color(0.4f, 0.9f, 1f);
        }
        int state = !running ? (completed ? 3 : 0) : NormalizedRemaining > 0.66f ? 3
            : NormalizedRemaining > 0.33f ? 2 : 1;
        if (chargeSprites != null && state < chargeSprites.Length && chargeSprites[state] != null)
        {
            chargeVisual.sprite = chargeSprites[state];
            float size = Mathf.Max(chargeVisual.sprite.bounds.size.x, chargeVisual.sprite.bounds.size.y);
            chargeVisual.transform.localScale = Vector3.one * (1.5f / Mathf.Max(size, 0.01f));
        }
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

        bool hasSavedState = data.boolSavedData.TryGetValue(ProgressKey(CompletedKeySuffix), out bool wasCompleted);

        if (wasCompleted)
        {
            ApplyCompletedState();
            return;
        }

        // Additive scene setup can apply the same checkpoint more than once.
        // Never cancel a live countdown after its single-use switch has fired.
        if (running || completed || !hasSavedState)
            return;

        completed = false;
        running = false;
        remain = 0f;
        floorsPassed = 0;
        UpdateDisplay();
    }

#if UNITY_EDITOR
    public void BakeDisplay()
    {
        CreateDisplay();
        UpdateDisplay();
    }
#endif
}
