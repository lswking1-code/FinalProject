using UnityEngine;

/// <summary>
/// 野猪敌人，在 Awake 中注册巡逻与追击状态。
/// </summary>
public class Boar : Enemy
{
    protected override void Awake()
    {
        base.Awake();
        patroState = new BoarPatrolState();
        chaseState = new BoarChaseState();
    }
}
