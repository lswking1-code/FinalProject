using UnityEngine;

/// <summary>
/// 从进入 Stage1 到通关的累计时长。阈值与奖牌颜色可在 Inspector 调整。
/// </summary>
public class RunTimer : MonoBehaviour
{
    const string ElapsedSaveKey = "runElapsedSeconds";

    [Header("奖牌阈值（分钟）")]
    [SerializeField] float goldMaxMinutes = 5f;
    [SerializeField] float silverMaxMinutes = 10f;
    [SerializeField] float bronzeMaxMinutes = 15f;

    [Header("奖牌颜色")]
    [SerializeField] Color goldColor = new Color(1f, 0.84f, 0f, 1f);
    [SerializeField] Color silverColor = new Color(0.75f, 0.75f, 0.78f, 1f);
    [SerializeField] Color bronzeColor = new Color(0.8f, 0.5f, 0.2f, 1f);
    [SerializeField] Color defaultColor = Color.white;

    public float ElapsedSeconds { get; private set; }
    public bool IsRunning { get; private set; }

    public static RunTimer Instance { get; private set; }

    void Awake()
    {
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    void Update()
    {
        if (!IsRunning)
            return;

        ElapsedSeconds += Time.deltaTime;
    }

    public void ResetTimer()
    {
        ElapsedSeconds = 0f;
        IsRunning = false;
    }

    public void Stop()
    {
        IsRunning = false;
    }

    public void StartOrResume()
    {
        IsRunning = true;
    }

    public void WriteTo(Data data)
    {
        if (data?.floatSavedData == null)
            return;

        data.floatSavedData[ElapsedSaveKey] = ElapsedSeconds;
    }

    public void ReadFrom(Data data)
    {
        if (data?.floatSavedData == null)
            return;

        if (data.floatSavedData.TryGetValue(ElapsedSaveKey, out float saved))
            ElapsedSeconds = Mathf.Max(ElapsedSeconds, saved);
    }

    public string FormatElapsed()
    {
        int total = Mathf.Max(0, Mathf.FloorToInt(ElapsedSeconds));
        int minutes = total / 60;
        int seconds = total % 60;
        return $"{minutes}:{seconds:D2}";
    }

    public Color GetRankColor()
    {
        float minutes = ElapsedSeconds / 60f;
        if (minutes < goldMaxMinutes)
            return goldColor;
        if (minutes < silverMaxMinutes)
            return silverColor;
        if (minutes < bronzeMaxMinutes)
            return bronzeColor;
        return defaultColor;
    }
}
