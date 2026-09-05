using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 拉杆开关：被近战/子弹命中时翻转状态，驱动通电平台，并更新拉杆与指示灯。
/// </summary>
public class ToggleSwitch : MonoBehaviour, IHitCountable
{
    [Header("状态")]
    [SerializeField] bool isOn;
    [Tooltip("同一攻击实例同一帧内去重（Bob Trigger + Overlap 双路径）")]
    [SerializeField] bool dedupeSameAttackSameFrame = true;

    [Header("驱动目标")]
    [SerializeField] ElectrifiedPlatform[] targets;
    public UnityEvent<bool> onToggled;

    [Header("视觉")]
    [SerializeField] Transform lever;
    [Tooltip("关闭时拉杆本地欧拉角（度）")]
    [SerializeField] Vector3 leverOffEuler = new Vector3(0f, 0f, 35f);
    [Tooltip("开启时拉杆本地欧拉角（度）")]
    [SerializeField] Vector3 leverOnEuler = new Vector3(0f, 0f, -35f);
    [SerializeField] SpriteRenderer lamp;
    [SerializeField] Color lampOnColor = new Color(0.35f, 1f, 0.35f, 1f);
    [SerializeField] Color lampOffColor = new Color(0.25f, 0.25f, 0.25f, 1f);

    [Header("音效")]
    [SerializeField] AudioSource sfxSource;
    [SerializeField] AudioClip toggleClip;
    [SerializeField, Range(0f, 1f)] float toggleVolume = 0.8f;

    Attack lastHitAttacker;
    int lastHitFrame = -1;

    public bool IsOn => isOn;

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

        ApplyVisual();
        SyncTargets();
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

        SetOn(!isOn, playSfx: true);
        return true;
    }

    public void SetOn(bool on, bool playSfx = false)
    {
        if (isOn == on)
        {
            ApplyVisual();
            SyncTargets();
            return;
        }

        isOn = on;
        ApplyVisual();
        SyncTargets();
        onToggled?.Invoke(isOn);

        if (playSfx)
            PlayToggleSfx();
    }

    public void Toggle() => SetOn(!isOn, playSfx: true);

    void SyncTargets()
    {
        if (targets == null)
            return;

        for (int i = 0; i < targets.Length; i++)
        {
            if (targets[i] != null)
                targets[i].SetFromSwitch(isOn);
        }
    }

    void ApplyVisual()
    {
        if (lever != null)
            lever.localRotation = Quaternion.Euler(isOn ? leverOnEuler : leverOffEuler);

        if (lamp != null)
            lamp.color = isOn ? lampOnColor : lampOffColor;
    }

    void PlayToggleSfx()
    {
        if (sfxSource == null || toggleClip == null)
            return;

        sfxSource.PlayOneShot(toggleClip, toggleVolume);
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        ApplyVisual();
    }
#endif
}
