/// <summary>
/// 敌人 AI 状态基类，定义状态进入、更新、退出与物理更新的接口。
/// </summary>
public abstract class BaseState
{
    protected Enemy currentEnemy;

    public abstract void OnEnter(Enemy enemy);
    public abstract void LogicUpdate();
    public abstract void PhysicsUpdate();
    public abstract void OnExit();
}
