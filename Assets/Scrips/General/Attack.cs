using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Attack : MonoBehaviour
{
    public int damage;

    public float attackRange;//攻击范围
    public float attackRate;//攻击频率

    private void OnTriggerStay2D(Collider2D collision)
    {
        collision.GetComponent<Character>()?.TakeDamage(this);//问号用法：判断对方身上是否有该代码，如没有则不执行之后的代码
    }
}
