using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

public enum AmmoType { S, M, L }

public class Character : MonoBehaviour,ISaveable
{
    [Header("事件监听")]
    public VoidEventSO newGameEvent;
    public CharacterEventSO healthEvent;

    [Header("基础属性")]
    public float maxHealth;
    public float currentHealth;
    public float maxPower;
    public float currentPower;
    public float powerRecoverSpeed;
    public float maxAbilityPower;
    public float AbilityPower;// 能力值
    public float AbilityPowerRecoverSpeed;
    [HideInInspector] public bool pauseAbilityPowerRecover;

    [Header("弹药数量")]
    public int BulletS = 0;
    public int BulletM = 0;
    public int BulletL = 0;
    public int maxBulletS = 0;
    public int maxBulletM = 0;
    public int maxBulletL = 0;

    [Header("受伤无敌")]
    public float invulnerableDuration;

    [Header("击退")]
    [Tooltip("击退阻力，越大越难推（≥1）；敌人默认 1，重物可更大")]
    [SerializeField] float knockbackResistance = 1f;
    [Tooltip("开启后完全不受击退（装甲车等）")]
    [SerializeField] bool immuneToKnockback;

    private float invulnerableCounter;// 无敌剩余时间
    public bool invulnerable;
    bool forcedInvulnerable;
    bool isDead;
    bool skipDeathDelay;
    Coroutine knockbackRoutine;
    int initialBulletS;
    int initialBulletM;
    int initialBulletL;
    PlayerWeaponController weaponController;

    public float KnockbackResistance => Mathf.Max(1f, knockbackResistance);

    public bool IsDead => isDead;
    public bool IsForcedInvulnerable => forcedInvulnerable;
    /// <summary>逻辑死亡后仍允许受击（敌人濒死窗口）。</summary>
    [HideInInspector] public bool allowHitsWhileDead;
    public bool CanReceiveHits => !isDead || allowHitsWhileDead;
    public bool HasAnySpecialAmmo => BulletS > 0 || BulletM > 0 || BulletL > 0;

    public int GetAmmo(AmmoType type) => type switch
    {
        AmmoType.S => BulletS,
        AmmoType.M => BulletM,
        AmmoType.L => BulletL,
        _ => 0,
    };

    /// <summary>S→机枪(1)，M→电磁/镭射(2)，L→霰弹(3)。</summary>
    public static int WeaponIdFromAmmo(AmmoType type) => type switch
    {
        AmmoType.S => 1,
        AmmoType.M => 2,
        AmmoType.L => 3,
        _ => 0,
    };

    public static bool TryAmmoFromWeaponId(int weaponId, out AmmoType type)
    {
        switch (weaponId)
        {
            case 1:
                type = AmmoType.S;
                return true;
            case 2:
                type = AmmoType.M;
                return true;
            case 3:
                type = AmmoType.L;
                return true;
            default:
                type = default;
                return false;
        }
    }

    public int GetAmmoForWeapon(int weaponId) =>
        TryAmmoFromWeaponId(weaponId, out var type) ? GetAmmo(type) : 0;

    public void SetForcedInvulnerable(bool value) => forcedInvulnerable = value;

    public UnityEvent<Character> OnHealthChange;

    public UnityEvent<Transform> OnTakeDamage;// 受伤时广播，参数为攻击者 Transform
    public UnityEvent OnDie;

    // 敌人开局补满血；玩家血量由读档 / ResetForNewGame 负责，避免首次启用冲掉存档
    private void Start()
    {
        if (CompareTag("Player"))
            return;

        currentHealth = maxHealth;
        NotifyStatsChanged();
    }
    void Awake()
    {
        initialBulletS = BulletS;
        initialBulletM = BulletM;
        initialBulletL = BulletL;
        weaponController = GetComponent<PlayerWeaponController>();

        // 玩家回菜单时会被禁用，newGame 需在禁用期间仍能收到
        if (newGameEvent != null)
            newGameEvent.OnEventRaised += ResetForNewGame;
    }

    void OnDestroy()
    {
        if (newGameEvent != null)
            newGameEvent.OnEventRaised -= ResetForNewGame;
    }

    /// <summary>新游戏 / 本关重开：清空死亡/无敌并回满血、体力、能力值、初始弹药。</summary>
    public void ResetForNewGame()
    {
        isDead = false;
        skipDeathDelay = false;
        allowHitsWhileDead = false;
        forcedInvulnerable = false;
        invulnerable = false;
        invulnerableCounter = 0f;
        pauseAbilityPowerRecover = false;
        currentHealth = maxHealth;
        currentPower = maxPower;
        AbilityPower = maxAbilityPower;

        int beforeS = BulletS;
        int beforeM = BulletM;
        int beforeL = BulletL;
        BulletS = initialBulletS;
        BulletM = initialBulletM;
        BulletL = initialBulletL;
        NotifyAmmoChanged(AmmoType.S, beforeS, BulletS);
        NotifyAmmoChanged(AmmoType.M, beforeM, BulletM);
        NotifyAmmoChanged(AmmoType.L, beforeL, BulletL);

        NotifyStatsChanged();
    }

    private void OnEnable()
    {
        ISaveable saveable = this;
        saveable.RegisterSaveData();
        // 读档重载：补回淡出窗口里误扣的弹药。传送切关必须跳过，否则会套成上一关存档点的弹药。
        if (SceneLoader.ShouldApplySaveOnEnable())
            ApplyLoadedStats();
    }

    void ApplyLoadedStats()
    {
        if (DataManager.instance?.CurrentData == null)
            return;

        LoadSaveData(DataManager.instance.CurrentData, restorePosition: false);
    }

    private void OnDisable()
    {
        ISaveable saveable = this;
        saveable.UnregisterSaveData();
    }

    private void Update()
    {
        if (invulnerable)
        {
            invulnerableCounter -= Time.deltaTime;// 递减无敌剩余时间
            if(invulnerableCounter <= 0)
            {
                invulnerable = false;
            }
        }

        if(currentPower < maxPower)// 自动回复体力
        {
            currentPower += Time.deltaTime * powerRecoverSpeed;
        }
        if (!pauseAbilityPowerRecover && AbilityPower < maxAbilityPower)// 自动回复能力值
        {
            AbilityPower += Time.deltaTime * AbilityPowerRecoverSpeed;
        }
    }

    private void OnTriggerStay2D(Collider2D other)
    {
        if (forcedInvulnerable)
            return;

        if (other.CompareTag("Water"))
            Kill();
    }

    /// <returns>true 表示本次确实扣血或击杀；无敌/吸收等情况返回 false。</returns>
    public bool TakeDamage(Attack attacker)
    {
        if (invulnerable || forcedInvulnerable)
            return false;

        if (attacker == null)
            return false;

        if (isDead)
        {
            if (!allowHitsWhileDead)
                return false;

            triggerInvulnerable();
            OnTakeDamage?.Invoke(attacker.transform);
            ApplyKnockback(attacker);
            return true;
        }

        var absorb = GetComponentInChildren<IDamageAbsorb>();
        if (absorb != null && absorb.TryAbsorb(attacker))
            return false;

        if (currentHealth - attacker.damage > 0)
        {
            currentHealth -= attacker.damage;
            triggerInvulnerable();
            OnTakeDamage?.Invoke(attacker.transform);
            ApplyKnockback(attacker);
            NotifyStatsChanged();
            if (CompareTag("Player"))
                PlaySessionRecorder.Instance?.RecordDamageTaken(attacker);
            return true;
        }

        currentHealth = 0;
        triggerInvulnerable();
        OnTakeDamage?.Invoke(attacker.transform);
        ApplyKnockback(attacker);
        NotifyStatsChanged();
        if (CompareTag("Player"))
            PlaySessionRecorder.Instance?.RecordDamageTaken(attacker);
        Die();
        return true;
    }

    /// <summary>
    /// 环境/陷阱伤害：扣血 + 无敌帧；不击退、不广播 OnTakeDamage（避免敌人 isHurt 硬直）。
    /// </summary>
    /// <returns>true 表示本次确实扣血或击杀；无敌/吸收等情况返回 false。</returns>
    public bool TakeHazardDamage(Attack attacker, out bool killed)
    {
        killed = false;

        if (isDead || invulnerable || forcedInvulnerable)
            return false;

        if (attacker == null)
            return false;

        var absorb = GetComponentInChildren<IDamageAbsorb>();
        if (absorb != null && absorb.TryAbsorb(attacker))
            return false;

        if (currentHealth - attacker.damage > 0)
        {
            currentHealth -= attacker.damage;
            triggerInvulnerable();
            NotifyStatsChanged();
            if (CompareTag("Player"))
                PlaySessionRecorder.Instance?.RecordDamageTaken(attacker);
            return true;
        }

        skipDeathDelay = true;
        if (CompareTag("Player"))
            PlaySessionRecorder.Instance?.RecordDamageTaken(attacker);
        Die();
        killed = true;
        return true;
    }

    void ApplyKnockback(Attack attacker)
    {
        if (immuneToKnockback)
            return;

        float force = Attack.EffectiveKnockbackForce(attacker, KnockbackResistance);
        if (force <= 0f)
            return;

        var rb = GetComponent<Rigidbody2D>();
        if (rb == null)
            return;

        Vector2 dir = Attack.ResolveKnockbackDir(attacker, transform.position);
        float duration = Mathf.Max(0.05f, attacker.knockbackDuration);
        Vector2 impulse = dir * force;

        var movement = GetComponent<PlayerMovement>();
        if (movement != null)
        {
            movement.BeginKnockback(impulse, duration);
            return;
        }

        if (rb.bodyType == RigidbodyType2D.Kinematic)
        {
            if (knockbackRoutine != null)
                StopCoroutine(knockbackRoutine);
            knockbackRoutine = StartCoroutine(KnockbackKinematic(rb, dir, force, duration));
            return;
        }

        // 垂直分量也生效时清零线速度，避免叠加速度被 AI 移动残留干扰
        rb.linearVelocity = Vector2.zero;
        rb.AddForce(impulse, ForceMode2D.Impulse);
    }

    IEnumerator KnockbackKinematic(Rigidbody2D rb, Vector2 dir, float force, float duration)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            float dt = Time.deltaTime;
            elapsed += dt;
            transform.position += (Vector3)(dir * (force * dt / duration));
            if (rb.simulated)
                rb.MovePosition(transform.position);
            yield return null;
        }
        knockbackRoutine = null;
    }

    void Die()
    {
        if (isDead)
            return;

        isDead = true;
        currentHealth = 0;
        NotifyStatsChanged();
        OnDie?.Invoke();
    }

    /// <summary>立即死亡（无视无敌/护盾），已死亡则忽略。跳过敌人濒死窗口。</summary>
    public void Kill()
    {
        skipDeathDelay = true;
        if (isDead)
        {
            GetComponent<Enemy>()?.PlayDeathAnim();
            return;
        }

        Die();
    }

    /// <summary>供 Enemy.OnDie 读取后清除，避免残留到下次死亡。</summary>
    public bool ConsumeSkipDeathDelay()
    {
        bool skip = skipDeathDelay;
        skipDeathDelay = false;
        return skip;
    }

    public void Revive()
    {
        isDead = false;
        skipDeathDelay = false;
        allowHitsWhileDead = false;
        forcedInvulnerable = false;
        invulnerable = false;
        invulnerableCounter = 0f;
    }

    public void HealthRecover(float HP)
    {
        currentHealth += HP;
        NotifyStatsChanged();
    }

    /// <summary>
    /// 回复生命。无效、已死亡或已满血时返回 false。
    /// </summary>
    public bool TryHeal(float amount)
    {
        if (amount <= 0 || isDead || currentHealth >= maxHealth)
            return false;

        currentHealth = Mathf.Min(maxHealth, currentHealth + amount);
        NotifyStatsChanged();
        return true;
    }

    /// <summary>
    /// 回满血（死亡中不生效）。
    /// </summary>
    public void RestoreFullHealth()
    {
        if (isDead)
            return;

        currentHealth = maxHealth;
        NotifyStatsChanged();
    }

    /// <summary>
    /// 触发短暂无敌。duration &lt; 0 时使用 invulnerableDuration；已在无敌中则取剩余时间与新时长的较大值。
    /// </summary>
    public void TriggerInvulnerable(float duration = -1f)
    {
        float applyDuration = duration < 0f ? invulnerableDuration : duration;
        if (applyDuration <= 0f)
            return;

        if (!invulnerable)
        {
            invulnerable = true;
            invulnerableCounter = applyDuration;
        }
        else
        {
            invulnerableCounter = Mathf.Max(invulnerableCounter, applyDuration);
        }
    }

    private void triggerInvulnerable() => TriggerInvulnerable();

    /// <summary>
    /// 将 S/M/L 弹药全部填充至上限。
    /// </summary>
    public void FillAllAmmo()
    {
        int beforeS = BulletS;
        int beforeM = BulletM;
        int beforeL = BulletL;
        BulletS = maxBulletS;
        BulletM = maxBulletM;
        BulletL = maxBulletL;
        NotifyAmmoChanged(AmmoType.S, beforeS, BulletS);
        NotifyAmmoChanged(AmmoType.M, beforeM, BulletM);
        NotifyAmmoChanged(AmmoType.L, beforeL, BulletL);
    }

    /// <summary>
    /// 增加弹药。已达上限或 amount 无效时返回 false。
    /// </summary>
    public bool AddAmmo(AmmoType type, int amount)
    {
        if (amount <= 0)
            return false;

        int before = GetAmmo(type);
        switch (type)
        {
            case AmmoType.S:
                if (BulletS >= maxBulletS)
                    return false;
                BulletS = Mathf.Min(BulletS + amount, maxBulletS);
                break;
            case AmmoType.M:
                if (BulletM >= maxBulletM)
                    return false;
                BulletM = Mathf.Min(BulletM + amount, maxBulletM);
                break;
            case AmmoType.L:
                if (BulletL >= maxBulletL)
                    return false;
                BulletL = Mathf.Min(BulletL + amount, maxBulletL);
                break;
            default:
                return false;
        }

        NotifyAmmoChanged(type, before, GetAmmo(type));
        return true;
    }

    /// <summary>
    /// 尝试消耗弹药。数量不足时返回 false 且不扣减。
    /// </summary>
    public bool TrySpendAmmo(AmmoType type, int amount)
    {
        if (amount <= 0)
            return true;

        int before = GetAmmo(type);
        switch (type)
        {
            case AmmoType.S:
                if (BulletS < amount)
                    return false;
                BulletS -= amount;
                break;
            case AmmoType.M:
                if (BulletM < amount)
                    return false;
                BulletM -= amount;
                break;
            case AmmoType.L:
                if (BulletL < amount)
                    return false;
                BulletL -= amount;
                break;
            default:
                return false;
        }

        NotifyAmmoChanged(type, before, GetAmmo(type));
        if (CompareTag("Player"))
            PlaySessionRecorder.Instance?.RecordAmmo(type, amount);
        return true;
    }

    void NotifyAmmoChanged(AmmoType type, int before, int after)
    {
        if (before == after)
            return;
        if (weaponController == null)
            weaponController = GetComponent<PlayerWeaponController>();
        weaponController?.OnAmmoChanged(type, before, after);
    }

    public void OnSlide(int cost)
    {
        currentPower -= cost;
        NotifyStatsChanged();
    }
    public void OnAbility(int cost)
    {
        AbilityPower -= cost;
        NotifyStatsChanged();
    }

    public void DrainAbilityPower(float amount)
    {
        AbilityPower = Mathf.Max(0f, AbilityPower - amount);
        NotifyStatsChanged();
    }

    public void RestoreAbilityPower(float amount)
    {
        AbilityPower = Mathf.Min(maxAbilityPower, AbilityPower + amount);
        NotifyStatsChanged();
    }

    void NotifyStatsChanged()
    {
        OnHealthChange?.Invoke(this);
        healthEvent?.RaiseEvent(this);
    }

    public DataDefination GetDataID()
    {
        return GetComponent<DataDefination>();
    }

    bool TryGetPersistId(out string id)
    {
        var def = GetDataID();
        if (def == null
            || def.persistentType != PersistentType.ReadWrite
            || string.IsNullOrEmpty(def.ID))
        {
            id = null;
            return false;
        }

        id = def.ID;
        return true;
    }

    public void GetSaveData(Data data)
    {
        if (!TryGetPersistId(out string id))
            return;

        data.characterPosDict[id] = new SerializeVector3(transform.position);
        data.floatSavedData[id + "health"] = currentHealth;
        data.floatSavedData[id + "power"] = currentPower;
        data.floatSavedData[id + "abilityPower"] = AbilityPower;
        data.floatSavedData[id + "bulletS"] = BulletS;
        data.floatSavedData[id + "bulletM"] = BulletM;
        data.floatSavedData[id + "bulletL"] = BulletL;
    }

    public void LoadSaveData(Data data)
    {
        LoadSaveData(data, restorePosition: true);
    }

    void LoadSaveData(Data data, bool restorePosition)
    {
        if (!TryGetPersistId(out string id))
            return;

        if (!data.characterPosDict.ContainsKey(id))
            return;

        isDead = false;
        skipDeathDelay = false;
        allowHitsWhileDead = false;
        forcedInvulnerable = false;
        currentHealth = data.floatSavedData[id + "health"];
        currentPower = data.floatSavedData[id + "power"];
        if (restorePosition)
            transform.position = data.characterPosDict[id].ToVector3();

        if (data.floatSavedData.TryGetValue(id + "abilityPower", out float ap))
            AbilityPower = ap;

        int beforeS = BulletS;
        int beforeM = BulletM;
        int beforeL = BulletL;
        if (data.floatSavedData.TryGetValue(id + "bulletS", out float s))
            BulletS = Mathf.RoundToInt(s);
        if (data.floatSavedData.TryGetValue(id + "bulletM", out float m))
            BulletM = Mathf.RoundToInt(m);
        if (data.floatSavedData.TryGetValue(id + "bulletL", out float l))
            BulletL = Mathf.RoundToInt(l);

        NotifyAmmoChanged(AmmoType.S, beforeS, BulletS);
        NotifyAmmoChanged(AmmoType.M, beforeM, BulletM);
        NotifyAmmoChanged(AmmoType.L, beforeL, BulletL);

        NotifyStatsChanged();
    }
}
