using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public interface ISaveable
{
    DataDefination GetDataID();
    void RegisterSaveData()
    {
        // Persistent 未加载或 DataManager 尚未 Awake 时跳过，避免 NRE
        if (DataManager.instance != null)
            DataManager.instance.RegisterSaveData(this);
    }
    void UnregisterSaveData()
    {
        if (DataManager.instance != null)
            DataManager.instance.UnRegisterSaveData(this);
    }

    void GetSaveData(Data data);// 将当前状态写入存档数据
    void LoadSaveData(Data data);// 从存档数据恢复状态
}
