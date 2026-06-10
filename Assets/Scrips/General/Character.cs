using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

public class Character : MonoBehaviour,ISaveable
{
    [Header("事件监听")]
    public VoidEventSO newGameEvent;

    [Header("基础属性")]
    public float maxHealth;
    public float currentHealth;
    public float maxPower;
    public float currentPower;
    public float powerRecoverSpeed;
    public float maxAbilityPower;
    public float AbilityPower;// 能力值
    public float AbilityPowerRecoverSpeed;

    [Header("受伤无敌")]
    public float invulnerableDuration;

    private float invulnerableCounter;// 无敌剩余时间
    public bool invulnerable;

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
        currentHealth = maxHealth;
        currentPower = maxPower;
        AbilityPower = maxAbilityPower;
        OnHealthChange?.Invoke(this);
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
        if (AbilityPower < maxAbilityPower)// 自动回复能力值
        {
            AbilityPower += Time.deltaTime * AbilityPowerRecoverSpeed;
        }
    }

    private void OnTriggerStay2D(Collider2D other)
    {
        if (other.CompareTag("Water"))
        {
            if (currentHealth > 0)
            {
                // 溺水：清零血量并触发死亡
                currentHealth = 0;
                OnHealthChange?.Invoke(this);
                OnDie?.Invoke();
            }
            
        }
    }
    public void TakeDamage(Attack attacker)
    {
        if (invulnerable)
            return;
        //Debug.Log(attacker.damage);
        if(currentHealth-attacker.damage > 0)
        {
            currentHealth -= attacker.damage;
            triggerInvulnerable();
            // 执行受伤逻辑
            OnTakeDamage?.Invoke(attacker.transform);// 广播受伤，并传入攻击者位置
        }
        else 
        {
            // TODO: 修复重复触发死亡的问题（参考 Water 区域）
            currentHealth = 0;
            // 血量归零，触发死亡
            OnDie?.Invoke();
        }

        OnHealthChange?.Invoke(this);
    }

    /// <summary>
    /// 触发受伤后的短暂无敌
    /// </summary>
    public void HealthRecover(float HP)
    {
        currentHealth += HP;
        OnHealthChange?.Invoke(this);
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
        OnHealthChange?.Invoke(this);
    }
    public void OnAbility(int cost)
    {
        AbilityPower -= cost;
        OnHealthChange?.Invoke(this);
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
            this.currentHealth = data.floatSavedData[GetDataID().ID + "health"];
            this.currentPower = data.floatSavedData[GetDataID().ID + "power"];
            transform.position = data.characterPosDict[GetDataID().ID].ToVector3();

            // 通知 UI 更新血条
            OnHealthChange?.Invoke(this);
        }
    }
}
