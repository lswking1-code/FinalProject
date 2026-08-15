using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 通电平台：开启时对接触的 Player/Enemy 按间隔造成无硬直陷阱伤害；
/// 站在绝缘箱等实体物上不接触本碰撞体时不会受伤。
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class ElectrifiedPlatform : MonoBehaviour
{
    [Header("通电状态")]
    [SerializeField] bool isOn;
    [Tooltip("开关 ON 时平台是否通电；若为 false 则开关亮灯时平台断电")]
    [SerializeField] bool poweredWhenSwitchOn = true;

    [Header("伤害")]
    [SerializeField, Min(1)] int damage = 1;
    [SerializeField, Min(0.05f)] float damageInterval = 0.4f;
    [Tooltip("离台后仍尝试扣血的时长，对齐玩家土狼，避免连跳漏伤")]
    [SerializeField, Min(0f)] float contactLinger = 0.12f;
    [Tooltip("可选伤害源；为空则运行时创建临时 Attack（无击退）")]
    [SerializeField] Attack attackSource;
    [SerializeField] string[] damageTags = { "Player", "Enemy", "AirEnemy" };

    [Header("视觉")]
    [SerializeField] SpriteRenderer baseRenderer;
    [SerializeField] Color offBaseColor = new Color(0.22f, 0.24f, 0.28f, 1f);
    [SerializeField] Color onBaseColor = new Color(0.35f, 0.4f, 0.5f, 1f);
    [SerializeField] GameObject electricFx;
    [SerializeField] SpriteRenderer electricFxRenderer;
    [SerializeField] Animator electricAnimator;
    [SerializeField] string electricOnStateName = "On";
    [SerializeField] string electricOffStateName = "Off";
    [SerializeField] Color electricFlashA = new Color(0.55f, 0.85f, 1f, 0.95f);
    [SerializeField] Color electricFlashB = new Color(1f, 1f, 1f, 0.75f);
    [SerializeField, Min(0.1f)] float electricFlashFrequency = 8f;

    [Header("受击反馈（玩家）")]
    [SerializeField] Color playerShockFlashColor = new Color(0.55f, 0.85f, 1f, 1f);
    [SerializeField, Min(0.02f)] float playerShockFlashDuration = 0.12f;

    [Header("音效")]
    [SerializeField] AudioSource buzzSource;
    [SerializeField] AudioClip buzzClip;
    [SerializeField, Range(0f, 1f)] float buzzVolume = 0.6f;

    public bool IsOn => isOn;

    readonly Dictionary<int, float> nextHitTime = new();
    readonly Dictionary<int, float> lingerUntil = new();
    readonly Dictionary<int, Character> lingerTargets = new();
    readonly List<int> lingerIdBuffer = new();
    readonly Dictionary<int, Coroutine> flashRoutines = new();
    Attack runtimeAttack;
    float electricPulseTimer;

    void Awake()
    {
        EnsureAttackSource();
        if (buzzSource == null)
            buzzSource = GetComponent<AudioSource>();
        if (buzzSource != null)
        {
            buzzSource.playOnAwake = false;
            buzzSource.loop = true;
            buzzSource.spatialBlend = 0f;
        }

        if (electricFxRenderer == null && electricFx != null)
            electricFxRenderer = electricFx.GetComponentInChildren<SpriteRenderer>(true);

        ApplyVisualAndAudio(isOn);
    }

    void Update()
    {
        if (!isOn || electricFxRenderer == null || electricAnimator != null)
            return;

        electricPulseTimer += Time.deltaTime * electricFlashFrequency;
        float t = (Mathf.Sin(electricPulseTimer * Mathf.PI * 2f) + 1f) * 0.5f;
        electricFxRenderer.color = Color.Lerp(electricFlashA, electricFlashB, t);
    }

    void OnDisable()
    {
        StopBuzz();
        ClearLinger();
        foreach (var kv in flashRoutines)
        {
            if (kv.Value != null)
                StopCoroutine(kv.Value);
        }
        flashRoutines.Clear();
    }

    public void SetPowered(bool powered)
    {
        if (isOn == powered)
        {
            ApplyVisualAndAudio(isOn);
            return;
        }

        isOn = powered;
        if (!isOn)
        {
            nextHitTime.Clear();
            ClearLinger();
        }
        ApplyVisualAndAudio(isOn);
    }

    /// <summary>由拉杆调用：开关状态映射到平台通电。</summary>
    public void SetFromSwitch(bool switchOn)
    {
        SetPowered(poweredWhenSwitchOn ? switchOn : !switchOn);
    }

    public void NotifyStanding(Character character)
    {
        TryShock(character);
    }

    void FixedUpdate()
    {
        if (!isOn || lingerTargets.Count == 0)
            return;

        lingerIdBuffer.Clear();
        lingerIdBuffer.AddRange(lingerTargets.Keys);
        float now = Time.time;
        for (int i = 0; i < lingerIdBuffer.Count; i++)
        {
            int id = lingerIdBuffer[i];
            if (!lingerUntil.TryGetValue(id, out float until) || now >= until)
            {
                lingerUntil.Remove(id);
                lingerTargets.Remove(id);
                continue;
            }

            if (!lingerTargets.TryGetValue(id, out Character character) || character == null)
            {
                lingerUntil.Remove(id);
                lingerTargets.Remove(id);
                continue;
            }

            TryShock(character);
        }
    }

    void OnCollisionEnter2D(Collision2D collision) => HandleContact(collision);

    void OnCollisionStay2D(Collision2D collision) => HandleContact(collision);

    void HandleContact(Collision2D collision)
    {
        if (!isOn || collision == null || collision.collider == null)
            return;

        if (collision.rigidbody != null)
            collision.rigidbody.WakeUp();

        Character character = ResolveDamageTarget(collision.collider);
        if (character == null)
            return;

        int id = character.GetInstanceID();
        lingerUntil.Remove(id);
        lingerTargets.Remove(id);
        TryShock(character);
    }

    void OnCollisionExit2D(Collision2D collision)
    {
        if (!isOn || collision?.collider == null)
            return;

        Character character = ResolveDamageTarget(collision.collider);
        if (character == null)
            return;

        int id = character.GetInstanceID();
        lingerUntil[id] = Time.time + contactLinger;
        lingerTargets[id] = character;
    }

    Character ResolveDamageTarget(Collider2D col)
    {
        if (!IsDamageTag(col))
            return null;

        return col.GetComponentInParent<Character>();
    }

    void TryShock(Character character)
    {
        if (!isOn || character == null)
            return;

        int id = character.GetInstanceID();
        if (nextHitTime.TryGetValue(id, out float next) && Time.time < next)
            return;

        EnsureAttackSource();
        attackSource.damage = damage;
        attackSource.enableKnockback = false;

        bool damaged = character.TakeHazardDamage(attackSource, out bool killed);
        if (!damaged)
            return;

        nextHitTime[id] = Time.time + damageInterval;
        if (!killed)
            PlayHitFeedback(character);
    }

    void ClearLinger()
    {
        lingerUntil.Clear();
        lingerTargets.Clear();
    }

    bool IsDamageTag(Collider2D col)
    {
        if (damageTags == null || damageTags.Length == 0)
            return HasTagInHierarchy(col, "Player") || HasTagInHierarchy(col, "Enemy");

        for (int i = 0; i < damageTags.Length; i++)
        {
            string tag = damageTags[i];
            if (!string.IsNullOrEmpty(tag) && HasTagInHierarchy(col, tag))
                return true;
        }

        return false;
    }

    static bool HasTagInHierarchy(Collider2D col, string tag)
    {
        if (col == null || string.IsNullOrEmpty(tag))
            return false;

        Transform t = col.transform;
        while (t != null)
        {
            if (t.CompareTag(tag))
                return true;
            t = t.parent;
        }

        return false;
    }

    void PlayHitFeedback(Character character)
    {
        var enemy = character.GetComponent<Enemy>();
        if (enemy != null)
        {
            enemy.PlayHitFeedbackNoStun();
            return;
        }

        var anim = PlayerAnimBase.Resolve(character.gameObject);
        anim?.PlayHurtAnim();

        int id = character.GetInstanceID();
        if (flashRoutines.TryGetValue(id, out var running) && running != null)
            StopCoroutine(running);
        flashRoutines[id] = StartCoroutine(FlashCharacter(character, id));
    }

    IEnumerator FlashCharacter(Character character, int id)
    {
        var renderers = character.GetComponentsInChildren<SpriteRenderer>(true);
        if (renderers == null || renderers.Length == 0)
        {
            flashRoutines.Remove(id);
            yield break;
        }

        var originals = new Color[renderers.Length];
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] == null)
                continue;
            originals[i] = renderers[i].color;
            renderers[i].color = playerShockFlashColor;
        }

        yield return new WaitForSeconds(playerShockFlashDuration);

        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] != null)
                renderers[i].color = originals[i];
        }

        flashRoutines.Remove(id);
    }

    void EnsureAttackSource()
    {
        if (attackSource != null)
            return;

        runtimeAttack = gameObject.GetComponent<Attack>();
        if (runtimeAttack == null)
            runtimeAttack = gameObject.AddComponent<Attack>();

        runtimeAttack.damage = damage;
        runtimeAttack.attackRate = 0f;
        runtimeAttack.attackType = AttackType.Melee;
        runtimeAttack.enableKnockback = false;
        runtimeAttack.enabled = false;
        attackSource = runtimeAttack;
    }

    void ApplyVisualAndAudio(bool on)
    {
        if (baseRenderer != null)
            baseRenderer.color = on ? onBaseColor : offBaseColor;

        if (electricFx != null)
            electricFx.SetActive(on);

        if (electricAnimator != null)
        {
            string state = on ? electricOnStateName : electricOffStateName;
            if (!string.IsNullOrEmpty(state))
                electricAnimator.Play(state, 0, 0f);
        }

        if (on)
            StartBuzz();
        else
            StopBuzz();
    }

    void StartBuzz()
    {
        if (buzzSource == null)
            return;

        if (buzzClip != null)
            buzzSource.clip = buzzClip;
        if (buzzSource.clip == null)
            return;

        buzzSource.volume = buzzVolume;
        buzzSource.loop = true;
        if (!buzzSource.isPlaying)
            buzzSource.Play();
    }

    void StopBuzz()
    {
        if (buzzSource != null && buzzSource.isPlaying)
            buzzSource.Stop();
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        damageInterval = Mathf.Max(0.05f, damageInterval);
        contactLinger = Mathf.Max(0f, contactLinger);
        damage = Mathf.Max(1, damage);
    }
#endif
}
