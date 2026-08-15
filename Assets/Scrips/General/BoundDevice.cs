using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 绑定装置：所有链接充能机关同时已充能后进入待激活，保持亮灯；
/// 玩家回到装置附近后才永久激活（开门/过场），之后不再关闭。
/// </summary>
public class BoundDevice : MonoBehaviour
{
    [Header("链接")]
    [SerializeField] EnergyNode[] nodes;
    [Tooltip("与 nodes 一一对应的指示灯")]
    [SerializeField] SpriteRenderer[] lamps;
    [SerializeField] Color lampOnColor = new Color(0.35f, 0.75f, 1f, 1f);
    [SerializeField] Color lampOffColor = new Color(0.25f, 0.25f, 0.25f, 1f);

    [Header("玩家靠近后才执行")]
    [Tooltip("全亮后玩家需进入该半径才会开门。装置在门上时按门中心计算")]
    [SerializeField, Min(0.1f)] float playerArriveRadius = 5f;
    [SerializeField] string playerTag = "Player";
    [Tooltip("检测中心；留空则用本物体")]
    [SerializeField] Transform detectOrigin;

    [Header("完成")]
    [SerializeField] UnityEvent onActivated;
    [Tooltip("拖入门上的 AnimatedDestroy。玩家靠近后播开门动画；门上请勾选 hideWhenFinished")]
    [SerializeField] AnimatedDestroy destroyOnComplete;

    [Header("音效")]
    [SerializeField] AudioSource sfxSource;
    [SerializeField] AudioClip activateClip;
    [SerializeField, Range(0f, 1f)] float activateVolume = 0.8f;

    readonly List<Collider2D> overlapBuffer = new();

    bool pendingActivate;
    bool permanentlyActive;

    public bool IsPermanentlyActive => permanentlyActive;
    public bool IsPendingActivate => pendingActivate;

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

        if (destroyOnComplete == null)
            destroyOnComplete = GetComponent<AnimatedDestroy>();
    }

    void OnEnable()
    {
        if (nodes == null)
            return;

        for (int i = 0; i < nodes.Length; i++)
        {
            if (nodes[i] != null)
                nodes[i].OnChargeChanged += HandleNodeChargeChanged;
        }

        Refresh();
    }

    void OnDisable()
    {
        if (nodes == null)
            return;

        for (int i = 0; i < nodes.Length; i++)
        {
            if (nodes[i] != null)
                nodes[i].OnChargeChanged -= HandleNodeChargeChanged;
        }
    }

    void Update()
    {
        if (permanentlyActive || !pendingActivate)
            return;

        if (IsPlayerNearby())
            LockActivate();
    }

    void OnTriggerEnter2D(Collider2D other) => TryActivateFromCollider(other);

    void OnTriggerStay2D(Collider2D other) => TryActivateFromCollider(other);

    void HandleNodeChargeChanged(EnergyNode node, bool charged)
    {
        Refresh();
    }

    void Refresh()
    {
        if (permanentlyActive)
            return;

        if (pendingActivate)
        {
            ApplyPendingLampVisual();
            return;
        }

        SyncLamps();

        if (AllNodesCharged())
            ArmPending();
    }

    void ArmPending()
    {
        if (pendingActivate || permanentlyActive)
            return;

        pendingActivate = true;
        HoldLinkedNodes();
        ApplyPendingLampVisual();

        if (IsPlayerNearby())
            LockActivate();
    }

    void HoldLinkedNodes()
    {
        if (nodes == null)
            return;

        for (int i = 0; i < nodes.Length; i++)
        {
            if (nodes[i] != null)
                nodes[i].HoldCharged();
        }
    }

    bool AllNodesCharged()
    {
        if (nodes == null || nodes.Length == 0)
            return false;

        for (int i = 0; i < nodes.Length; i++)
        {
            if (nodes[i] == null || !nodes[i].IsCharged)
                return false;
        }

        return true;
    }

    void TryActivateFromCollider(Collider2D other)
    {
        if (permanentlyActive || !pendingActivate || other == null)
            return;

        if (!IsPlayerCollider(other))
            return;

        LockActivate();
    }

    bool IsPlayerNearby()
    {
        Vector2 origin = detectOrigin != null ? detectOrigin.position : transform.position;
        var filter = new ContactFilter2D
        {
            useTriggers = true,
            useLayerMask = false,
            useDepth = false,
        };

        overlapBuffer.Clear();
        int count = Physics2D.OverlapCircle(origin, playerArriveRadius, filter, overlapBuffer);
        for (int i = 0; i < count; i++)
        {
            if (IsPlayerCollider(overlapBuffer[i]))
                return true;
        }

        return false;
    }

    bool IsPlayerCollider(Collider2D col)
    {
        if (col == null)
            return false;

        if (col.CompareTag(playerTag))
            return true;

        var character = col.GetComponentInParent<Character>();
        return character != null && character.CompareTag(playerTag);
    }

    void LockActivate()
    {
        if (permanentlyActive)
            return;

        permanentlyActive = true;
        pendingActivate = false;
        PlaySfx(activateClip, activateVolume);
        onActivated?.Invoke();
        if (destroyOnComplete != null)
            destroyOnComplete.BeginDestroy();
    }

    void ApplyPendingLampVisual()
    {
        if (lamps == null)
            return;

        for (int i = 0; i < lamps.Length; i++)
        {
            if (lamps[i] != null)
                lamps[i].color = lampOnColor;
        }
    }

    void SyncLamps()
    {
        if (lamps == null)
            return;

        int count = lamps.Length;
        int nodeCount = nodes != null ? nodes.Length : 0;

        for (int i = 0; i < count; i++)
        {
            if (lamps[i] == null)
                continue;

            bool on = i < nodeCount && nodes[i] != null && nodes[i].IsCharged;
            lamps[i].color = on ? lampOnColor : lampOffColor;
        }
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
        if (!Application.isPlaying)
            SyncLamps();
    }

    void OnDrawGizmosSelected()
    {
        Vector3 origin = detectOrigin != null ? detectOrigin.position : transform.position;
        Gizmos.color = pendingActivate ? Color.cyan : new Color(0.35f, 0.75f, 1f, 0.6f);
        Gizmos.DrawWireSphere(origin, playerArriveRadius);
    }
#endif
}
