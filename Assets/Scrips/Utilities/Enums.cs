/// <summary>敌人 AI 状态枚举</summary>
public enum NPCState
{
    Patrol,   // 巡逻
    Chase,    // 追击
    Skill,    // 技能（预留）
    GetClose, // 靠近玩家
    Shot,     // 射击
    Move      // 随机移动
}

/// <summary>敌人 Action 类型（Shot / Move），用于概率记录</summary>
public enum EnemyAction
{
    Shot,
    Move
}

/// <summary>场景类型</summary>
public enum SceneType
{
    Loaction, // 关卡场景
    Menu      // 菜单场景
}

/// <summary>存档持久化类型</summary>
public enum PersistentType
{
    ReadWrite,    // 读写存档
    DoNotPersist  // 不持久化
}