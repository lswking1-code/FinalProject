using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 激光门：激活时以实体碰撞体挡路，并对重叠玩家按间隔造成高额陷阱伤害。
/// 可由拉杆、压力板、倒计时充能机关驱动开关；翻滚/钩爪期间由 Character 强制无敌免疫伤害。
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class LaserGate : MonoBehaviour
{
    [Header("激活状态")]
    [SerializeField] bool isActive = true;
    [Tooltip("开关 ON 时激光是否激活；默认 false 表示开关 ON 时关闭激光")]
    [SerializeField] bool activeWhenSwitchOn;

    [Header("碰撞")]
    [Tooltip("激活时启用、关闭时禁用的碰撞体；留空则取本物体及子物体全部 Collider2D")]
    [SerializeField] Collider2D[] colliders;

    [Header("伤害")]
    [SerializeField, Min(1)] int damage = 80;
    [SerializeField, Min(0.05f)] float damageInterval = 0.5f;
    [Tooltip("离碰后仍尝试扣血的时长，对齐玩家土狼，避免连跳漏伤")]
    [SerializeField, Min(0f)] float contactLinger = 0.12f;
    [Tooltip("可选伤害源；为空则运行时创建临时 Attack（无击退）")]
    [SerializeField] Attack attackSource;
    [SerializeField] string[] damageTags = { "Player" };

    [Header("视觉")]
    [SerializeField] Animator laserAnimator;
    [SerializeField] string idleStateName = "Idle";
    [SerializeField] string activateStateName = "Activate";
    [SerializeField] string inactivateStateName = "Inactivate";
    [SerializeField] string inactivateIdleStateName = "Inactivate_Idle";

    [Header("受击反馈（玩家）")]
    [SerializeField] Color playerShockFlashColor = new Color(1f, 0.35f, 0.35f, 1f);
    [SerializeField, Min(0.02f)] float playerShockFlashDuration = 0.12f;

    [Header("音效")]
    [SerializeField] AudioSource buzzSource;
    [SerializeField] AudioClip buzzClip;
    [SerializeField, Range(0f, 1f)] float buzzVolume = 0.6f;

    public bool IsActive => isActive;
    public bool IsOn => isActive;

    bool CanDealDamage => isActive && !shuttingDown;

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
        ResolveColliders();
        EnsureCollidersNonTrigger();

        if (buzzSource == null)
            buzzSource = GetComponent<AudioSource>();
        if (buzzSource != null)
        {
            buzzSource.playOnAwake = false;
            buzzSource.loop = true;
            buzzSource.spatialBlend = 0f;
        }

        shuttingDown = !isActive;
        ApplyColliderState(isActive);
        ApplyAudio(isActive);
        PlaySteadyState(isActive);
        RefreshGridSpanVisual();
    }

    void OnDisable()
    {
        StopVisualRoutine();
        StopBuzz();
        StopHazard();
        foreach (var kv in flashRoutines)
        {
            if (kv.Value != null)
                StopCoroutine(kv.Value);
        }
        flashRoutines.Clear();
    }

    public void SetActive(bool active)
    {
        if (isActive == active)
            return;

        if (active)
        {
            shuttingDown = false;
            isActive = true;
            ApplyColliderState(true);
        }
        else
        {
            shuttingDown = true;
            isActive = false;
            ApplyColliderState(false);
            StopHazard();
        }

        ApplyAudio(isActive);
        PlayPoweredVisual(isActive, instant: false);
        RefreshGridSpanVisual();
    }

    public void SetPowered(bool powered) => SetActive(powered);

    /// <summary>由拉杆/压力板调用：开关状态映射到激光激活。</summary>
    public void SetFromSwitch(bool switchOn)
    {
        SetActive(activeWhenSwitchOn ? switchOn : !switchOn);
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

    void HandleContact(Collision2D collision)
    {
        if (!CanDealDamage || collision == null || collision.collider == null)
            return;

        if (collision.rigidbody != null)
            collision.rigidbody.WakeUp();

        BeginOverlap(ResolveDamageTarget(collision.collider));
    }

    void OnCollisionExit2D(Collision2D collision) => EndOverlap(collision?.collider);

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
            PlaySessionRecorder.Instance?.RecordSceneHazardDeath("LaserGate");
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
            return HasTagInHierarchy(col, "Player");

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

    void DisableStrayAttackOnSelf()
    {
        Attack stray = GetComponent<Attack>();
        if (stray != null)
            stray.enabled = false;
    }

    void ResolveColliders()
    {
        if (colliders != null && colliders.Length > 0)
            return;

        colliders = GetComponentsInChildren<Collider2D>(true);
    }

    void EnsureCollidersNonTrigger()
    {
        if (colliders == null)
            return;

        for (int i = 0; i < colliders.Length; i++)
        {
            if (colliders[i] != null)
                colliders[i].isTrigger = false;
        }
    }

    void ApplyColliderState(bool active)
    {
        if (colliders == null)
            return;

        for (int i = 0; i < colliders.Length; i++)
        {
            if (colliders[i] != null)
                colliders[i].enabled = active;
        }
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
        RefreshGridSpanVisual();
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
        RefreshGridSpanVisual();
    }

    void PlayState(string stateName)
    {
        EnsureAnimator();
        if (laserAnimator == null || string.IsNullOrEmpty(stateName))
            return;

        laserAnimator.Play(stateName, 0, 0f);
        laserAnimator.Update(0f);
    }

    bool IsInState(string stateName)
    {
        if (laserAnimator == null)
            return false;

        var info = laserAnimator.GetCurrentAnimatorStateInfo(0);
        return info.IsName(stateName) || info.IsName("Base Layer." + stateName);
    }

    bool HasFinishedState(string stateName)
    {
        if (laserAnimator == null || !laserAnimator.isActiveAndEnabled)
            return true;

        if (!IsInState(stateName))
            return transitionSeen;

        transitionSeen = true;
        var info = laserAnimator.GetCurrentAnimatorStateInfo(0);
        if (info.length <= 0f)
            return true;

        return info.normalizedTime >= 1f && !laserAnimator.IsInTransition(0);
    }

    void EnsureAnimator()
    {
        if (laserAnimator == null)
            laserAnimator = GetComponent<Animator>();
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

    void RefreshGridSpanVisual()
    {
        var span = GetComponent<LaserGateGridSpan>();
        if (span != null)
            span.RefreshVisualState(isActive);
    }

    public void NotifyVisualLayoutChanged()
    {
        var span = GetComponent<LaserGateGridSpan>();
        if (span == null)
            return;

        span.RebuildVisualTiles();
        span.RefreshVisualState(isActive);
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        damageInterval = Mathf.Max(0.05f, damageInterval);
        contactLinger = Mathf.Max(0f, contactLinger);
        damage = Mathf.Max(1, damage);
    }

    public void ApplyEditorPaintDefaults(bool active)
    {
        isActive = active;
        shuttingDown = !active;
        ApplyColliderState(isActive);
        PlayPoweredVisual(isActive, instant: true);
        StopBuzz();
        RefreshGridSpanVisual();
        UnityEditor.EditorUtility.SetDirty(this);
    }
#endif
}
