/// <summary>敌人 AI 状态枚举</summary>
public enum NPCState
{
    Patrol,      // 巡逻 / 远程站岗 Idle
    Chase,       // 追击
    Skill,       // 技能（近战飞扑预留等）
    GetClose,    // 靠近玩家
    Shot,        // 射击
    Move,        // 随机移动
    Crouch,      // 蹲伏
    CrouchShoot, // 蹲射
    Reload,      // 换弹冷却
    Jump,        // 跃起（精英能力）
    Return,      // 脱战后返回出生点
    MeleeAttack  // 近战挥刀（前摇 / 挥砍 / 后摇）
}

/// <summary>敌人 Action 类型，用于概率记录（Reload 不参与权重）</summary>
public enum EnemyAction
{
    Shot,
    Move,
    Crouch,
    CrouchShoot,
    Jump
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