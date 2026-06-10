using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public interface ISaveable
{
    DataDefination GetDataID();
    void RegisterSaveData() => DataManager.instance.RegisterSaveData(this);//注册
    void UnregisterSaveData() => DataManager.instance.UnRegisterSaveData(this);//注销
    
    void GetSaveData(Data data);//通过该接口储存
    void LoadSaveData(Data data);//加载储存数据
}
