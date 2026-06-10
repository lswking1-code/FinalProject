using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Attack : MonoBehaviour
{
    public int damage;

    public float attackRange;// 攻击范围
    public float attackRate;// 攻击频率

    private void OnTriggerStay2D(Collider2D collision)
    {
        // 空条件运算符：若对方没有 Character 组件则不调用 TakeDamage
        collision.GetComponent<Character>()?.TakeDamage(this);
    }
}
