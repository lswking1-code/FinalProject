using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 倒计时充能机关：被命中后激活并开始倒计时，结束后自动失活并恢复连接装置初始状态。
/// 倒计时期间颜色按固定格数离散变暗，作为剩余时间提示。
/// </summary>
public class TimedChargeNode : MonoBehaviour, IHitCountable
{
    [Header("计时")]
    [SerializeField, Min(0.1f)] float activeDuration = 10f;
    [SerializeField, Min(1)] int dimStepCount = 10;
    [SerializeField] bool refreshDurationOnHitWhileActive = true;
    [SerializeField] bool dedupeSameAttackSameFrame = true;

    [Header("连接装置")]
    [SerializeField] TimedChargeLinkedTarget[] linkedTargets;

    [Header("视觉")]
    [SerializeField] SpriteRenderer visual;
    [SerializeField] Color activeBrightColor = new Color(0.35f, 0.75f, 1f, 1f);
    [SerializeField] Color inactiveDarkColor = new Color(0.1f, 0.1f, 0.12f, 1f);

    [Header("事件")]
    [SerializeField] UnityEvent<bool> onActiveChanged;

    Attack lastHitAttacker;
    int lastHitFrame = -1;
    bool isActive;
    float remain;
    int lastVisualStep = -1;

    public bool IsActive => isActive;
    public float RemainingNormalized =>
        !isActive || activeDuration <= 0f ? 0f : Mathf.Clamp01(remain / activeDuration);

    void Awake()
    {
        if (visual == null)
            visual = GetComponent<SpriteRenderer>();

        CacheTargetInitialStates();
        ApplyVisual(force: true);
    }

    void Update()
    {
        if (!isActive)
            return;

        remain -= Time.deltaTime;
        if (remain <= 0f)
        {
            remain = 0f;
            DeactivateAndRestore();
            return;
        }

        ApplyVisual(force: false);
    }

    public bool RegisterHit(Attack attacker)
    {
        if (dedupeSameAttackSameFrame
            && attacker != null
            && attacker == lastHitAttacker
            && lastHitFrame == Time.frameCount)
            return true;

        lastHitAttacker = attacker;
        lastHitFrame = Time.frameCount;

        if (!isActive)
        {
            Activate();
            return true;
        }

        if (refreshDurationOnHitWhileActive)
        {
            remain = activeDuration;
            ApplyVisual(force: true);
        }

        return true;
    }

    void Activate()
    {
        isActive = true;
        remain = activeDuration;
        lastVisualStep = -1;

        ApplyActivatedStateForTargets();
        RaiseActiveChanged(true);
        ApplyVisual(force: true);
    }

    void DeactivateAndRestore()
    {
        if (!isActive)
            return;

        isActive = false;
        remain = 0f;
        lastVisualStep = -1;

        RestoreInitialStateForTargets();
        RaiseActiveChanged(false);
        ApplyVisual(force: true);
    }

    void CacheTargetInitialStates()
    {
        if (linkedTargets == null)
            return;

        for (int i = 0; i < linkedTargets.Length; i++)
        {
            if (linkedTargets[i] != null)
                linkedTargets[i].CaptureInitialState();
        }
    }

    void ApplyActivatedStateForTargets()
    {
        if (linkedTargets == null)
            return;

        for (int i = 0; i < linkedTargets.Length; i++)
        {
            if (linkedTargets[i] != null)
                linkedTargets[i].ApplyActivatedState();
        }
    }

    void RestoreInitialStateForTargets()
    {
        if (linkedTargets == null)
            return;

        for (int i = 0; i < linkedTargets.Length; i++)
        {
            if (linkedTargets[i] != null)
                linkedTargets[i].RestoreInitialState();
        }
    }

    int ComputeVisualStep()
    {
        if (!isActive)
            return dimStepCount;

        float elapsedNormalized = 1f - RemainingNormalized;
        int steps = Mathf.Max(1, dimStepCount);
        return Mathf.Clamp(Mathf.FloorToInt(elapsedNormalized * steps), 0, steps - 1);
    }

    void ApplyVisual(bool force)
    {
        if (visual == null)
            return;

        int currentStep = ComputeVisualStep();
        if (!force && currentStep == lastVisualStep)
            return;

        lastVisualStep = currentStep;

        if (!isActive)
        {
            visual.color = inactiveDarkColor;
            return;
        }

        float stepT = dimStepCount <= 0 ? 0f : (float)currentStep / dimStepCount;
        visual.color = Color.Lerp(activeBrightColor, inactiveDarkColor, stepT);
    }

    void RaiseActiveChanged(bool active)
    {
        onActiveChanged?.Invoke(active);
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        dimStepCount = Mathf.Max(1, dimStepCount);
        if (!Application.isPlaying)
            ApplyVisual(force: true);
    }
#endif
}
