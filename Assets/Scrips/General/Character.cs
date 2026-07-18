using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

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

    [Header("受伤无敌")]
    public float invulnerableDuration;

    private float invulnerableCounter;// 无敌剩余时间
    public bool invulnerable;
    bool forcedInvulnerable;
    bool isDead;

    public bool IsDead => isDead;
    public bool IsForcedInvulnerable => forcedInvulnerable;

    public void SetForcedInvulnerable(bool value) => forcedInvulnerable = value;

    public UnityEvent<Character> OnHealthChange;

    public UnityEvent<Transform> OnTakeDamage;// 受伤时广播，参数为攻击者 Transform
    public UnityEvent OnDie;

    // 初始化时设置满血，使敌人也能在 Start 时获得正确血量
    private void Start()
    {
        currentHealth = maxHealth;
    }
    private void NewGame()
    {
        isDead = false;
        forcedInvulnerable = false;
        invulnerable = false;
        currentHealth = maxHealth;
        currentPower = maxPower;
        AbilityPower = maxAbilityPower;
        NotifyStatsChanged();
    }

    private void OnEnable()
    {
        newGameEvent.OnEventRaised += NewGame;
        ISaveable saveable = this;
        saveable.RegisterSaveData();
    }
    private void OnDisable()
    {
        newGameEvent.OnEventRaised -= NewGame;
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
            Die();
    }

    public void TakeDamage(Attack attacker)
    {
        if (isDead || invulnerable || forcedInvulnerable)
            return;

        if (currentHealth - attacker.damage > 0)
        {
            currentHealth -= attacker.damage;
            triggerInvulnerable();
            OnTakeDamage?.Invoke(attacker.transform);
            NotifyStatsChanged();
        }
        else
        {
            Die();
        }
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

    public void Revive()
    {
        isDead = false;
        forcedInvulnerable = false;
        invulnerable = false;
        invulnerableCounter = 0f;
    }

    /// <summary>
    /// 触发受伤后的短暂无敌
    /// </summary>
    public void HealthRecover(float HP)
    {
        currentHealth += HP;
        NotifyStatsChanged();
    }
    private void triggerInvulnerable()
    {
        if (!invulnerable)
        {
            invulnerable = true;
            invulnerableCounter = invulnerableDuration;
        }
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

    public void GetSaveData(Data data)
    {
        if (data.characterPosDict.ContainsKey(GetDataID().ID))
        {
            data.characterPosDict[GetDataID().ID] = new SerializeVector3(transform.position);
            data.floatSavedData[GetDataID().ID + "health"] = this.currentHealth;
            data.floatSavedData[GetDataID().ID + "power"] = this.currentPower;
        }
        else
        {
            data.characterPosDict.Add(GetDataID().ID, new SerializeVector3(transform.position));
            data.floatSavedData.Add(GetDataID().ID + "health", this.currentHealth);
            data.floatSavedData.Add(GetDataID().ID + "power", this.currentPower);
        }
    }

    public void LoadSaveData(Data data)
    {
        if (data.characterPosDict.ContainsKey(GetDataID().ID))
        {
            isDead = false;
            forcedInvulnerable = false;
            this.currentHealth = data.floatSavedData[GetDataID().ID + "health"];
            this.currentPower = data.floatSavedData[GetDataID().ID + "power"];
            transform.position = data.characterPosDict[GetDataID().ID].ToVector3();

            // 通知 UI 更新血条
            NotifyStatsChanged();
        }
    }
}
