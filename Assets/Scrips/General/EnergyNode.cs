using System;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 充能机关：三名角色的 M 攻击（镭射 / 特殊弹 M / 鞭）可充能；每次命中重置 10 秒倒计时，超时回未充能。
/// 非 M 攻击命中只播弹开火花，不改变充能状态。
/// </summary>
public class EnergyNode : MonoBehaviour, IHitCountable
{
    [Header("充能")]
    [SerializeField, Min(0.1f)] float chargeDuration = 10f;
    [Tooltip("同一攻击实例同一帧内去重（Bob Trigger + Overlap 双路径）")]
    [SerializeField] bool dedupeSameAttackSameFrame = true;

    [Header("视觉")]
    [SerializeField] SpriteRenderer visual;
    [SerializeField] Color unchargedColor = new Color(0.22f, 0.22f, 0.28f, 1f);
    [SerializeField] Color chargedBrightColor = new Color(0.35f, 0.75f, 1f, 1f);
    [SerializeField] Color chargedDimColor = new Color(0.12f, 0.28f, 0.45f, 1f);

    [Header("音效")]
    [SerializeField] AudioSource sfxSource;
    [SerializeField] AudioClip sparkClip;
    [SerializeField, Range(0f, 1f)] float sparkVolume = 0.7f;
    [SerializeField] AudioClip chargeClip;
    [SerializeField, Range(0f, 1f)] float chargeVolume = 0.8f;
    [SerializeField] AudioClip warnClip;
    [SerializeField, Range(0f, 1f)] float warnVolume = 0.7f;
    [Tooltip("刚充能时的提示间隔（秒）")]
    [SerializeField, Min(0.05f)] float warnIntervalMax = 1.2f;
    [Tooltip("即将到期时的提示间隔（秒）")]
    [SerializeField, Min(0.05f)] float warnIntervalMin = 0.15f;

    [Header("事件")]
    [SerializeField] UnityEvent<bool> onChargeChanged;

    Attack lastHitAttacker;
    int lastHitFrame = -1;
    bool isCharged;
    float remain;
    float warnTimer;

    public bool IsCharged => isCharged;
    public bool IsHeld => held;
    public float ChargeNormalized =>
        !isCharged || chargeDuration <= 0f ? 0f : Mathf.Clamp01(remain / chargeDuration);

    public event Action<EnergyNode, bool> OnChargeChanged;

    bool held;

    void Awake()
    {
        if (visual == null)
            visual = GetComponent<SpriteRenderer>();
        if (sfxSource == null)
            sfxSource = GetComponent<AudioSource>();
        if (sfxSource != null)
        {
            sfxSource.playOnAwake = false;
            sfxSource.loop = false;
            sfxSource.spatialBlend = 0f;
        }

        ApplyVisual();
    }

    void Update()
    {
        if (!isCharged || held)
            return;

        remain -= Time.deltaTime;
        ApplyVisual();
        TickWarn();

        if (remain <= 0f)
            Discharge();
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

        if (attacker != null && attacker.chargesEnergyNode)
        {
            Charge();
            return true;
        }

        PlaySfx(sparkClip, sparkVolume);
        return true;
    }

    public void Charge()
    {
        if (held)
            return;

        bool wasCharged = isCharged;
        isCharged = true;
        remain = chargeDuration;
        warnTimer = CurrentWarnInterval();
        ApplyVisual();

        if (!wasCharged)
        {
            PlaySfx(chargeClip, chargeVolume);
            PlaySfx(warnClip, warnVolume);
            RaiseChargeChanged(true);
        }
    }

    /// <summary>绑定装置全亮待激活时锁定：保持已充能，停止倒计时与提示声。</summary>
    public void HoldCharged()
    {
        if (!isCharged)
            Charge();

        held = true;
        warnTimer = 0f;
        if (sfxSource != null)
            sfxSource.Stop();
        ApplyVisual();
    }

    void Discharge()
    {
        if (!isCharged || held)
            return;

        isCharged = false;
        remain = 0f;
        warnTimer = 0f;
        if (sfxSource != null)
            sfxSource.Stop();
        ApplyVisual();
        RaiseChargeChanged(false);
    }

    void TickWarn()
    {
        if (warnClip == null)
            return;

        warnTimer -= Time.deltaTime;
        if (warnTimer > 0f)
            return;

        PlaySfx(warnClip, warnVolume);
        warnTimer = CurrentWarnInterval();
    }

    float CurrentWarnInterval()
    {
        float t = 1f - ChargeNormalized;
        return Mathf.Lerp(warnIntervalMax, warnIntervalMin, t);
    }

    void ApplyVisual()
    {
        if (visual == null)
            return;

        if (!isCharged)
        {
            visual.color = unchargedColor;
            return;
        }

        visual.color = Color.Lerp(chargedBrightColor, chargedDimColor, 1f - ChargeNormalized);
    }

    void RaiseChargeChanged(bool charged)
    {
        OnChargeChanged?.Invoke(this, charged);
        onChargeChanged?.Invoke(charged);
    }

    void PlaySfx(AudioClip clip, float volume)
    {
        if (sfxSource == null || clip == null)
            return;

        sfxSource.PlayOneShot(clip, volume);
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        if (warnIntervalMin > warnIntervalMax)
            warnIntervalMin = warnIntervalMax;

        if (!Application.isPlaying)
            ApplyVisual();
    }
#endif
}
