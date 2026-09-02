using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 压力地板：Tag 为 Player / Robot / Box 的物体压在上面时开启，全部离开后关闭。
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class PressurePlate : MonoBehaviour
{
    static readonly string[] OccupantTags = { "Player", "Robot", "Box" };

    [Header("状态")]
    [SerializeField] bool isOn;

    [Header("驱动目标")]
    [SerializeField] ElectrifiedPlatform[] targets;
    [SerializeField] LaserGate[] laserGateTargets;
    public UnityEvent<bool> onToggled;

    [Header("视觉")]
    [SerializeField] Transform plateVisual;
    [Tooltip("开启时视觉相对本地原点的压下偏移")]
    [SerializeField] Vector3 pressedLocalOffset = new Vector3(0f, -0.08f, 0f);
    [SerializeField] SpriteRenderer lamp;
    [SerializeField] Color lampOnColor = new Color(0.35f, 1f, 0.35f, 1f);
    [SerializeField] Color lampOffColor = new Color(0.25f, 0.25f, 0.25f, 1f);
    [SerializeField] SpriteRenderer plateRenderer;
    [SerializeField] Color plateOnColor = new Color(0.45f, 0.5f, 0.55f, 1f);
    [SerializeField] Color plateOffColor = new Color(0.3f, 0.32f, 0.36f, 1f);

    [Header("音效")]
    [SerializeField] AudioSource sfxSource;
    [SerializeField] AudioClip pressClip;
    [SerializeField] AudioClip releaseClip;
    [SerializeField, Range(0f, 1f)] float sfxVolume = 0.8f;

    [Header("清理")]
    [Tooltip("定期清理已销毁的占用碰撞体，避免计数卡死")]
    [SerializeField, Min(0.1f)] float cleanupInterval = 0.5f;

    readonly HashSet<Collider2D> occupants = new HashSet<Collider2D>();
    readonly List<Collider2D> cleanupBuffer = new List<Collider2D>();
    Vector3 plateVisualRestLocalPos;
    float nextCleanupTime;

    public bool IsOn => isOn;

    void Awake()
    {
        var col = GetComponent<Collider2D>();
        if (col != null)
            col.isTrigger = true;

        if (sfxSource == null)
            sfxSource = GetComponent<AudioSource>();
        if (sfxSource != null)
        {
            sfxSource.playOnAwake = false;
            sfxSource.loop = false;
            sfxSource.spatialBlend = 0f;
        }

        if (plateVisual != null)
            plateVisualRestLocalPos = plateVisual.localPosition;

        ApplyVisual();
        SyncTargets();
    }

    void Update()
    {
        if (Time.time < nextCleanupTime)
            return;

        nextCleanupTime = Time.time + cleanupInterval;
        CleanupInvalidOccupants();
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (!IsValidOccupant(other))
            return;

        if (!occupants.Add(other))
            return;

        RefreshState(playSfx: true);
    }

    void OnTriggerExit2D(Collider2D other)
    {
        if (other == null)
            return;

        if (!occupants.Remove(other))
            return;

        RefreshState(playSfx: true);
    }

    void RefreshState(bool playSfx)
    {
        CleanupInvalidOccupants();
        SetOn(occupants.Count > 0, playSfx);
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
            PlaySfx(isOn ? pressClip : releaseClip);
    }

    void SyncTargets()
    {
        if (targets != null)
        {
            for (int i = 0; i < targets.Length; i++)
            {
                if (targets[i] != null)
                    targets[i].SetFromSwitch(isOn);
            }
        }

        if (laserGateTargets == null)
            return;

        for (int i = 0; i < laserGateTargets.Length; i++)
        {
            if (laserGateTargets[i] != null)
                laserGateTargets[i].SetFromSwitch(isOn);
        }
    }

    void ApplyVisual()
    {
        if (plateVisual != null)
            plateVisual.localPosition = isOn
                ? plateVisualRestLocalPos + pressedLocalOffset
                : plateVisualRestLocalPos;

        if (lamp != null)
            lamp.color = isOn ? lampOnColor : lampOffColor;

        if (plateRenderer != null)
            plateRenderer.color = isOn ? plateOnColor : plateOffColor;
    }

    void PlaySfx(AudioClip clip)
    {
        if (sfxSource == null || clip == null)
            return;

        sfxSource.PlayOneShot(clip, sfxVolume);
    }

    void CleanupInvalidOccupants()
    {
        if (occupants.Count == 0)
            return;

        cleanupBuffer.Clear();
        foreach (Collider2D col in occupants)
        {
            if (col == null || !col.enabled || !col.gameObject.activeInHierarchy)
                cleanupBuffer.Add(col);
        }

        if (cleanupBuffer.Count == 0)
            return;

        for (int i = 0; i < cleanupBuffer.Count; i++)
            occupants.Remove(cleanupBuffer[i]);

        bool shouldBeOn = occupants.Count > 0;
        if (shouldBeOn != isOn)
            SetOn(shouldBeOn, playSfx: false);
    }

    static bool IsValidOccupant(Collider2D col)
    {
        if (col == null)
            return false;

        Transform t = col.transform;
        while (t != null)
        {
            for (int i = 0; i < OccupantTags.Length; i++)
            {
                if (t.CompareTag(OccupantTags[i]))
                    return true;
            }

            t = t.parent;
        }

        return false;
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        if (plateVisual != null && !Application.isPlaying)
            plateVisualRestLocalPos = plateVisual.localPosition;

        ApplyVisual();
    }
#endif
}
