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
    public float AbilityPower;//能力值
    public float AbilityPowerRecoverSpeed;

    [Header("受伤无敌")]
    public float invulnerableDuration;

    private float invulnerableCounter;//计数
    public bool invulnerable;

    public UnityEvent<Character> OnHealthChange;

    public UnityEvent<Transform> OnTakeDamage;//创建受伤事件
    public UnityEvent OnDie;

    //为了敌人能在开始获得血量
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
            invulnerableCounter -= Time.deltaTime;//计算时间流逝(无敌时间流逝)
            if(invulnerableCounter <= 0)
            {
                invulnerable = false;
            }
        }

        if(currentPower < maxPower)//回复体力
        {
            currentPower += Time.deltaTime * powerRecoverSpeed;
        }
        if (AbilityPower < maxAbilityPower)//回复能力值
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
                //死亡、更新血量
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
            //执行受伤
            OnTakeDamage?.Invoke(attacker.transform);//并获得敌人的位置
        }
        else 
        {
            //TODO:修复重复触发问题（类似Water）
            currentHealth = 0;
            //触发死亡
            OnDie?.Invoke();
        }

        OnHealthChange?.Invoke(this);
    }

    /// <summary>
    /// 触发受伤无敌
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

            //通知UI更新
            OnHealthChange?.Invoke(this);
        }
    }
}
