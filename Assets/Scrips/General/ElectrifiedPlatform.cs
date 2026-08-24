using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 通电平台：开启时对重叠的 Player/Enemy 按间隔造成无硬直陷阱伤害。
/// 碰撞体应为 Trigger 且不放在 Ground 层，避免被踩踏；绝缘箱等实体若把角色隔开则不会受伤。
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
    [SerializeField] Animator electricAnimator;
    [SerializeField] string idleStateName = "Idle";
    [SerializeField] string activateStateName = "Activate";
    [SerializeField] string inactivateStateName = "Inactivate";
    [SerializeField] string inactivateIdleStateName = "Inactivate_Idle";

    [Header("受击反馈（玩家）")]
    [SerializeField] Color playerShockFlashColor = new Color(0.55f, 0.85f, 1f, 1f);
    [SerializeField, Min(0.02f)] float playerShockFlashDuration = 0.12f;

    [Header("音效")]
    [SerializeField] AudioSource buzzSource;
    [SerializeField] AudioClip buzzClip;
    [SerializeField, Range(0f, 1f)] float buzzVolume = 0.6f;

    public bool IsOn => isOn;

    /// <summary>仅在通电且未进入关闭过渡时造成伤害。</summary>
    bool CanDealDamage => isOn && !shuttingDown;

    readonly Dictionary<int, float> nextHitTime = new();
    readonly Dictionary<int, Character> overlapTargets = new();
    readonly Dictionary<int, float> lingerUntil = new();
    readonly Dictionary<int, Character> lingerTargets = new();
    readonly List<int> lingerIdBuffer = new();
    readonly Dictionary<int, Coroutine> flashRoutines = new();
    Attack runtimeAttack;
    Coroutine visualRoutine;
    bool transitionSeen;
    bool shuttingDown;

    void Awake()
    {
        DisableStrayAttackOnSelf();
        if (buzzSource == null)
            buzzSource = GetComponent<AudioSource>();
        if (buzzSource != null)
        {
            buzzSource.playOnAwake = false;
            buzzSource.loop = true;
            buzzSource.spatialBlend = 0f;
        }

        shuttingDown = !isOn;
        ApplyAudio(isOn);
        PlaySteadyState(isOn);
    }

    void OnDisable()
    {
        StopVisualRoutine();
        StopBuzz();
        overlapTargets.Clear();
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
            return;

        if (powered)
        {
            shuttingDown = false;
            isOn = true;
        }
        else
        {
            // 一进入关闭过渡就停伤，不等 Inactivate 播完。
            shuttingDown = true;
            isOn = false;
            StopHazard();
        }

        ApplyAudio(isOn);
        PlayPoweredVisual(isOn, instant: false);
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
        if (!CanDealDamage)
            return;

        ShockOverlaps();
        ShockLinger();
    }

    void OnCollisionEnter2D(Collision2D collision) => HandleContact(collision);

    void OnCollisionStay2D(Collision2D collision) => HandleContact(collision);

    void OnTriggerEnter2D(Collider2D other) => HandleTrigger(other);

    void OnTriggerStay2D(Collider2D other) => HandleTrigger(other);

    void HandleContact(Collision2D collision)
    {
        if (!CanDealDamage || collision == null || collision.collider == null)
            return;

        if (collision.rigidbody != null)
            collision.rigidbody.WakeUp();

        BeginOverlap(ResolveDamageTarget(collision.collider));
    }

    void HandleTrigger(Collider2D other)
    {
        if (!CanDealDamage || other == null)
            return;

        if (other.attachedRigidbody != null)
            other.attachedRigidbody.WakeUp();

        BeginOverlap(ResolveDamageTarget(other));
    }

    void OnCollisionExit2D(Collision2D collision)
    {
        EndOverlap(collision?.collider);
    }

    void OnTriggerExit2D(Collider2D other)
    {
        EndOverlap(other);
    }

    void BeginOverlap(Character character)
    {
        if (character == null)
            return;

        int id = character.GetInstanceID();
        overlapTargets[id] = character;
        lingerUntil.Remove(id);
        lingerTargets.Remove(id);
        TryShock(character);
    }

    void EndOverlap(Collider2D col)
    {
        if (!CanDealDamage || col == null)
            return;

        Character character = ResolveDamageTarget(col);
        if (character == null)
            return;

        int id = character.GetInstanceID();
        overlapTargets.Remove(id);
        lingerUntil[id] = Time.time + contactLinger;
        lingerTargets[id] = character;
    }

    void ShockOverlaps()
    {
        if (overlapTargets.Count == 0)
            return;

        lingerIdBuffer.Clear();
        lingerIdBuffer.AddRange(overlapTargets.Keys);
        for (int i = 0; i < lingerIdBuffer.Count; i++)
        {
            int id = lingerIdBuffer[i];
            if (!overlapTargets.TryGetValue(id, out Character character) || character == null)
            {
                overlapTargets.Remove(id);
                continue;
            }

            TryShock(character);
        }
    }

    void ShockLinger()
    {
        if (lingerTargets.Count == 0)
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

    Character ResolveDamageTarget(Collider2D col)
    {
        if (!IsDamageTag(col))
            return null;

        return col.GetComponentInParent<Character>();
    }

    void TryShock(Character character)
    {
        if (!CanDealDamage || character == null)
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
        if (killed && character.CompareTag("Player"))
            PlaySessionRecorder.Instance?.RecordSceneHazardDeath("ElectrifiedPlatform");
        if (!killed)
            PlayHitFeedback(character);
    }

    void StopHazard()
    {
        nextHitTime.Clear();
        overlapTargets.Clear();
        ClearLinger();
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
        {
            attackSource.enabled = false;
            return;
        }

        DisableStrayAttackOnSelf();

        const string childName = "HazardAttackSource";
        Transform existing = transform.Find(childName);
        GameObject host = existing != null ? existing.gameObject : new GameObject(childName);
        host.transform.SetParent(transform, false);
        // 先藏起来再 AddComponent，避免 Attack.OnEnable 立刻 ProcessOverlapHits。
        host.SetActive(false);

        runtimeAttack = host.GetComponent<Attack>();
        if (runtimeAttack == null)
            runtimeAttack = host.AddComponent<Attack>();

        runtimeAttack.damage = damage;
        runtimeAttack.attackRate = 0f;
        runtimeAttack.attackType = AttackType.Melee;
        runtimeAttack.enableKnockback = false;
        runtimeAttack.enabled = false;
        host.SetActive(true);
        attackSource = runtimeAttack;
    }

    /// <summary>
    /// 旧逻辑把 Attack 挂在平台本体上；本体有 BoxCollider2D，OnEnable 会扫重叠并无视通电状态扣血。
    /// </summary>
    void DisableStrayAttackOnSelf()
    {
        Attack stray = gameObject.GetComponent<Attack>();
        if (stray != null)
            stray.enabled = false;
    }

    void PlayPoweredVisual(bool on, bool instant)
    {
        StopVisualRoutine();
        if (instant || !Application.isPlaying)
        {
            PlaySteadyState(on);
            return;
        }

        visualRoutine = StartCoroutine(PlayTransitionThenIdle(on));
    }

    void PlaySteadyState(bool on)
    {
        shuttingDown = !on;
        PlayState(on ? idleStateName : inactivateIdleStateName);
    }

    IEnumerator PlayTransitionThenIdle(bool on)
    {
        if (!on)
            shuttingDown = true;

        string transition = on ? activateStateName : inactivateStateName;
        string idle = on ? idleStateName : inactivateIdleStateName;
        transitionSeen = false;
        PlayState(transition);

        float waited = 0f;
        const float enterTimeout = 0.25f;
        while (!IsInState(transition) && waited < enterTimeout)
        {
            waited += Time.deltaTime;
            yield return null;
        }

        while (!HasFinishedState(transition))
            yield return null;

        PlayState(idle);
        visualRoutine = null;
    }

    void PlayState(string stateName)
    {
        EnsureAnimator();
        if (electricAnimator == null || string.IsNullOrEmpty(stateName))
            return;

        electricAnimator.Play(stateName, 0, 0f);
        electricAnimator.Update(0f);
    }

    bool IsInState(string stateName)
    {
        if (electricAnimator == null)
            return false;

        var info = electricAnimator.GetCurrentAnimatorStateInfo(0);
        return info.IsName(stateName) || info.IsName("Base Layer." + stateName);
    }

    bool HasFinishedState(string stateName)
    {
        if (electricAnimator == null || !electricAnimator.isActiveAndEnabled)
            return true;

        if (!IsInState(stateName))
            return transitionSeen;

        transitionSeen = true;
        var info = electricAnimator.GetCurrentAnimatorStateInfo(0);
        if (info.length <= 0f)
            return true;

        return info.normalizedTime >= 1f && !electricAnimator.IsInTransition(0);
    }

    void EnsureAnimator()
    {
        if (electricAnimator == null)
            electricAnimator = GetComponent<Animator>();
    }

    void StopVisualRoutine()
    {
        if (visualRoutine == null)
            return;

        StopCoroutine(visualRoutine);
        visualRoutine = null;
    }

    void ApplyAudio(bool on)
    {
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

    /// <summary>笔刷绘制时写入默认通电状态并刷新外观；编辑模式不播放循环电流音。</summary>
    public void ApplyEditorPaintDefaults(bool powered)
    {
        isOn = powered;
        shuttingDown = !powered;
        PlayPoweredVisual(isOn, instant: true);
        StopBuzz();
        UnityEditor.EditorUtility.SetDirty(this);
    }
#endif
}
