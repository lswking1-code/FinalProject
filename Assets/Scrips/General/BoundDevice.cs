using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 绑定装置：所有链接充能机关同时已充能后进入待激活，保持亮灯；
/// 玩家回到装置附近后才永久激活（开门/过场），之后不再关闭。
/// </summary>
public class BoundDevice : MonoBehaviour, ISaveable
{
    [Header("链接")]
    [SerializeField] EnergyNode[] nodes;
    [Tooltip("与 nodes 一一对应的指示灯")]
    [SerializeField] SpriteRenderer[] lamps;
    [Tooltip("可选的未充能／已充能图；两张均设置时使用原色显示")]
    [SerializeField] Sprite lampOffSprite;
    [SerializeField] Sprite lampOnSprite;
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
    string saveKey;
    bool restoring;

    public bool IsPermanentlyActive => permanentlyActive;
    public bool IsPendingActivate => pendingActivate;

    void Awake()
    {
        // 缓存场景层级标识，避免门位移或其他物体销毁影响存档定位。
        string path = "";
        for (Transform current = transform; current != null; current = current.parent)
            path = $"/{current.name}[{current.GetSiblingIndex()}]" + path;
        saveKey = $"BoundDevice:{gameObject.scene.name}:{path}";
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
        destroyOnComplete?.EnableStateRestoration();
    }

    void OnEnable()
    {
        ((ISaveable)this).RegisterSaveData();
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
        ((ISaveable)this).UnregisterSaveData();
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
        if (restoring)
            return;
        Refresh();
    }

    void Start()
    {
        ((ISaveable)this).RegisterSaveData();
        DataManager.instance?.ApplyLoadedData(this);
    }

    public DataDefination GetDataID() => GetComponent<DataDefination>();

    public void GetSaveData(Data data)
    {
        data.boolSavedData[saveKey + ":pending"] = pendingActivate;
        data.boolSavedData[saveKey + ":open"] = permanentlyActive;
        if (nodes == null)
            return;
        for (int i = 0; i < nodes.Length; i++)
        {
            if (nodes[i] == null)
                continue;
            data.floatSavedData[$"{saveKey}:node:{i}:remain"] = nodes[i].RemainingChargeTime;
            data.boolSavedData[$"{saveKey}:node:{i}:held"] = nodes[i].IsHeld;
        }
    }

    public void LoadSaveData(Data data)
    {
        if (data == null)
            return;
        restoring = true;
        permanentlyActive = data.boolSavedData.TryGetValue(saveKey + ":open", out bool open) && open;
        pendingActivate = !permanentlyActive
            && data.boolSavedData.TryGetValue(saveKey + ":pending", out bool pending) && pending;
        if (nodes != null)
        {
            for (int i = 0; i < nodes.Length; i++)
            {
                if (nodes[i] == null)
                    continue;
                data.floatSavedData.TryGetValue($"{saveKey}:node:{i}:remain", out float remaining);
                bool held = permanentlyActive || pendingActivate
                    || (data.boolSavedData.TryGetValue($"{saveKey}:node:{i}:held", out bool savedHeld) && savedHeld);
                nodes[i].RestoreChargeState(remaining, held);
            }
        }
        destroyOnComplete?.RestoreOpenState(permanentlyActive);
        SyncLamps();
        restoring = false;
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
                SetLampVisual(lamps[i], true);
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
            SetLampVisual(lamps[i], on);
        }
    }

    void SetLampVisual(SpriteRenderer lamp, bool on)
    {
        if (lampOffSprite != null && lampOnSprite != null)
        {
            lamp.sprite = on ? lampOnSprite : lampOffSprite;
            lamp.color = Color.white;
        }
        else
        {
            lamp.color = on ? lampOnColor : lampOffColor;
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
