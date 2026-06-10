using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public interface ISaveable
{
    DataDefination GetDataID();
    void RegisterSaveData() => DataManager.instance.RegisterSaveData(this);// 向 DataManager 注册
    void UnregisterSaveData() => DataManager.instance.UnRegisterSaveData(this);// 从 DataManager 注销
    
    void GetSaveData(Data data);// 将当前状态写入存档数据
    void LoadSaveData(Data data);// 从存档数据恢复状态
}
