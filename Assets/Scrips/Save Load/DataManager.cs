using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.InputSystem;

[DefaultExecutionOrder(-100)]
public class DataManager : MonoBehaviour
{
    public static DataManager instance;

    [Header("事件监听")]
    public VoidEventSO saveDataEvent;
    public VoidEventSO loadDataEvent;
    public VoidEventSO newGameEvent;

    readonly List<ISaveable> saveableList = new List<ISaveable>();

    Data saveData;
    string jsonFolder;

    public Data CurrentData => saveData;
    public bool HasSaveFile => !string.IsNullOrEmpty(jsonFolder) && File.Exists(SaveFilePath);

    string SaveFilePath => jsonFolder + "data.sav";

    void Awake()
    {
        if (instance == null)
            instance = this;
        else
            Destroy(gameObject);

        jsonFolder = Application.persistentDataPath + "/SAVE DATA/";
        saveData = CreateEmptyData();
        ReadSavedData();
        EnemyDeathProgress.RestoreSessionFrom(saveData);
    }

    /// <summary>
    /// 供场景加载后新注册的 ISaveable（遭遇区等）立刻套用内存中的存档。
    /// </summary>
    public void ApplyLoadedData(ISaveable saveable)
    {
        if (saveable == null || saveData == null)
            return;

        saveable.LoadSaveData(saveData);
    }

    void OnEnable()
    {
        if (saveDataEvent != null)
            saveDataEvent.OnEventRaised += Save;
        if (loadDataEvent != null)
            loadDataEvent.OnEventRaised += Load;
        if (newGameEvent != null)
            newGameEvent.OnEventRaised += ClearForNewGame;
    }

    void OnDisable()
    {
        if (saveDataEvent != null)
            saveDataEvent.OnEventRaised -= Save;
        if (loadDataEvent != null)
            loadDataEvent.OnEventRaised -= Load;
        if (newGameEvent != null)
            newGameEvent.OnEventRaised -= ClearForNewGame;
    }

    void Update()
    {
        if (Keyboard.current != null && Keyboard.current.lKey.wasPressedThisFrame)
            Load();
    }

    public void RegisterSaveData(ISaveable saveable)
    {
        if (!saveableList.Contains(saveable))
            saveableList.Add(saveable);
    }

    public void UnRegisterSaveData(ISaveable saveable)
    {
        saveableList.Remove(saveable);
    }

    public void Save()
    {
        foreach (var saveable in saveableList)
            saveable.GetSaveData(saveData);

        EnemyDeathProgress.CopySessionKillsTo(saveData);
        SyncLifePointsToData();
        WriteSaveFile();
    }

    public void Load()
    {
        EnemyDeathProgress.RestoreSessionFrom(saveData);
        foreach (var saveable in saveableList)
            saveable.LoadSaveData(saveData);
    }

    public bool HasPlayerCheckpoint(string playerId)
    {
        return saveData != null
            && !string.IsNullOrEmpty(playerId)
            && saveData.characterPosDict != null
            && saveData.characterPosDict.ContainsKey(playerId);
    }

    /// <summary>
    /// 只更新生命点并写盘，不采集 ISaveable（避免把死亡位置存成存档点）。
    /// </summary>
    public void PersistLifePoints() => PersistTransientProgress();

    /// <summary>
    /// 把当前内存存档写盘，不重新采集角色位置等 ISaveable。
    /// 用于生命点、已拾取道具等不应随读档回滚的状态。
    /// </summary>
    public void PersistTransientProgress()
    {
        if (saveData == null)
            return;

        SyncLifePointsToData();

        bool hasProgress = saveData.characterPosDict != null && saveData.characterPosDict.Count > 0;
        if (!hasProgress && !File.Exists(SaveFilePath))
            return;

        WriteSaveFile();
    }

    /// <summary>
    /// 新游戏：清空内存进度并删除存档文件，避免关卡物体套用上一局状态。
    /// </summary>
    public void ClearForNewGame()
    {
        saveData = CreateEmptyData();
        EnemyDeathProgress.ClearSession();

        if (File.Exists(SaveFilePath))
            File.Delete(SaveFilePath);

        PlayerLifePoints.Instance?.ResetToDefault();
    }

    void SyncLifePointsToData()
    {
        if (saveData == null)
            return;

        saveData.lifePoints = PlayerLifePoints.Instance != null
            ? PlayerLifePoints.Instance.Current
            : saveData.lifePoints;
    }

    void WriteSaveFile()
    {
        if (!Directory.Exists(jsonFolder))
            Directory.CreateDirectory(jsonFolder);

        File.WriteAllText(SaveFilePath, JsonConvert.SerializeObject(saveData));
    }

    void ReadSavedData()
    {
        if (!File.Exists(SaveFilePath))
            return;

        var stringData = File.ReadAllText(SaveFilePath);
        var jsonData = JsonConvert.DeserializeObject<Data>(stringData);
        saveData = jsonData ?? CreateEmptyData();
        EnsureDataCollections(saveData);
    }

    static Data CreateEmptyData()
    {
        var data = new Data();
        EnsureDataCollections(data);
        return data;
    }

    static void EnsureDataCollections(Data data)
    {
        if (data.characterPosDict == null)
            data.characterPosDict = new Dictionary<string, SerializeVector3>();
        if (data.floatSavedData == null)
            data.floatSavedData = new Dictionary<string, float>();
        if (data.boolSavedData == null)
            data.boolSavedData = new Dictionary<string, bool>();
        if (data.intListSavedData == null)
            data.intListSavedData = new Dictionary<string, List<int>>();
    }
}
