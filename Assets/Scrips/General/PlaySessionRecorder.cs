using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

/// <summary>
/// 可开关的单局游玩数据记录。每局单独写一份 JSON，互不覆盖。
/// 文件目录：Application.persistentDataPath/PLAY SESSION/
/// </summary>
[DefaultExecutionOrder(-50)]
public class PlaySessionRecorder : MonoBehaviour
{
    public static PlaySessionRecorder Instance { get; private set; }

    const string FolderName = "/PLAY SESSION/";
    const string TimeStampFormat = "yyyy-MM-dd HH:mm:ss";

    [Header("开关")]
    [Tooltip("关闭后完全不记录、不写文件。打包前请确认是否勾选。")]
    [SerializeField] bool enableRecording = true;

    [Header("事件监听")]
    [SerializeField] VoidEventSO newGameEvent;
    [SerializeField] VoidEventSO gameClearEvent;
    [SerializeField] VoidEventSO gameOverEvent;
    [SerializeField] VoidEventSO backToMenuEvent;

    SceneLoader sceneLoader;
    bool sessionActive;
    string pendingFilePath;
    DateTime sessionStartedAt;
    float elapsedSeconds;
    string characterName;

    int furthestRank;
    string furthestProgress;
    float furthestReachedAtSeconds;
    float? gameClearSeconds;
    float? tutorialClearSeconds;
    bool wasInTutorial;

    int deathOrFailCount;
    int robotSummonCount;
    float robotAliveSeconds;
    int ammoUsedS;
    int ammoUsedM;
    int ammoUsedL;
    int hookUseCount;
    int lifePointCollected;
    int ability1UseCount;
    int ability2UseCount;
    int weaponSwitchCount;
    int sceneHazardDeathCount;
    int meleeJumpCount;
    int meleeDoubleJumpCount;
    int meleeSlideCount;
    readonly Dictionary<string, int> damageTakenBySource = new Dictionary<string, int>();
    readonly Dictionary<string, int> sceneHazardDeathsBySource = new Dictionary<string, int>();

    public bool IsRecording => enableRecording && sessionActive;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }

        Instance = this;
        sceneLoader = GetComponent<SceneLoader>();
    }

    void OnEnable()
    {
        if (newGameEvent != null)
            newGameEvent.OnEventRaised += OnNewGameEvent;
        if (gameClearEvent != null)
            gameClearEvent.OnEventRaised += OnGameClearEvent;
        if (gameOverEvent != null)
            gameOverEvent.OnEventRaised += OnGameOverEvent;
        if (backToMenuEvent != null)
            backToMenuEvent.OnEventRaised += OnBackToMenuEvent;
    }

    void OnDisable()
    {
        if (newGameEvent != null)
            newGameEvent.OnEventRaised -= OnNewGameEvent;
        if (gameClearEvent != null)
            gameClearEvent.OnEventRaised -= OnGameClearEvent;
        if (gameOverEvent != null)
            gameOverEvent.OnEventRaised -= OnGameOverEvent;
        if (backToMenuEvent != null)
            backToMenuEvent.OnEventRaised -= OnBackToMenuEvent;
    }

    void OnDestroy()
    {
        EndSession("Quit");
        if (Instance == this)
            Instance = null;
    }

    void Update()
    {
        if (!sessionActive)
            return;

        elapsedSeconds += Time.deltaTime;
    }

    void OnApplicationQuit() => EndSession("Quit");

    void OnNewGameEvent() => BeginNewSession("NewGame");

    void OnGameClearEvent()
    {
        if (sessionActive)
            gameClearSeconds = elapsedSeconds;
        EndSession("GameClear");
    }

    void OnGameOverEvent() => EndSession("GameOver");

    void OnBackToMenuEvent() => EndSession("BackToMenu");

    /// <summary>
    /// 开新一局。若上一局尚未写盘，先以 reason 结束上一局。
    /// </summary>
    public void BeginNewSession(string reason)
    {
        if (!enableRecording)
            return;

        if (sessionActive)
            EndSession(reason);

        ResetCounters();
        sessionActive = true;
        sessionStartedAt = DateTime.Now;
        characterName = ResolveCharacterName();
        pendingFilePath = AllocateFilePath(sessionStartedAt);
        Debug.Log($"PlaySessionRecorder: this session will be saved to {pendingFilePath}");
    }

    public void EndSession(string reason)
    {
        if (!enableRecording || !sessionActive)
            return;

        sessionActive = false;
        WriteFile(reason);
        pendingFilePath = null;
    }

    public void NotifySceneLoaded(GameSceneSO scene, bool isTutorial)
    {
        if (!sessionActive || scene == null)
            return;
        if (scene.sceneType != SceneType.Loaction)
            return;

        if (wasInTutorial && !isTutorial && !tutorialClearSeconds.HasValue)
            tutorialClearSeconds = elapsedSeconds;

        wasInTutorial = isTutorial;
        ResolveProgress(scene, isTutorial, out string label, out int rank);
        if (rank <= furthestRank)
            return;

        furthestRank = rank;
        furthestProgress = label;
        furthestReachedAtSeconds = elapsedSeconds;
    }

    public void RecordDeath()
    {
        if (!sessionActive)
            return;
        deathOrFailCount++;
    }

    public void RecordAmmo(AmmoType type, int amount)
    {
        if (!sessionActive || amount <= 0)
            return;

        switch (type)
        {
            case AmmoType.S:
                ammoUsedS += amount;
                break;
            case AmmoType.M:
                ammoUsedM += amount;
                break;
            case AmmoType.L:
                ammoUsedL += amount;
                break;
        }
    }

    public void RecordRobotSummon()
    {
        if (!sessionActive)
            return;
        robotSummonCount++;
    }

    public void AddRobotAliveTime(float delta)
    {
        if (!sessionActive || delta <= 0f)
            return;
        robotAliveSeconds += delta;
    }

    public void RecordHook()
    {
        if (!sessionActive)
            return;
        hookUseCount++;
    }

    public void RecordLifePoint(int amount)
    {
        if (!sessionActive || amount <= 0)
            return;
        lifePointCollected += amount;
    }

    public void RecordAbility1()
    {
        if (!sessionActive)
            return;
        ability1UseCount++;
    }

    public void RecordAbility2()
    {
        if (!sessionActive)
            return;
        ability2UseCount++;
    }

    public void RecordWeaponSwitch()
    {
        if (!sessionActive)
            return;
        weaponSwitchCount++;
    }

    public void RecordDamageTaken(Attack attacker)
    {
        if (!sessionActive || attacker == null)
            return;

        string source = ResolveDamageSource(attacker);
        if (!damageTakenBySource.TryGetValue(source, out int count))
            count = 0;
        damageTakenBySource[source] = count + 1;
    }

    public void RecordSceneHazardDeath(string source)
    {
        if (!sessionActive)
            return;

        sceneHazardDeathCount++;
        string key = string.IsNullOrEmpty(source) ? "Unknown" : source;
        if (!sceneHazardDeathsBySource.TryGetValue(key, out int count))
            count = 0;
        sceneHazardDeathsBySource[key] = count + 1;
    }

    public void RecordMeleeJump()
    {
        if (!sessionActive)
            return;
        meleeJumpCount++;
    }

    public void RecordMeleeDoubleJump()
    {
        if (!sessionActive)
            return;
        meleeDoubleJumpCount++;
    }

    public void RecordMeleeSlide()
    {
        if (!sessionActive)
            return;
        meleeSlideCount++;
    }

    static string ResolveDamageSource(Attack attacker)
    {
        if (attacker == null)
            return "Unknown";

        Transform root = attacker.transform;

        var projectile = root.GetComponentInParent<EnemyProjectile>();
        if (projectile != null)
            return "Bullet:" + CleanName(projectile.gameObject.name);

        var grenade = root.GetComponentInParent<EnemyGrenade>();
        if (grenade != null)
            return "Grenade:" + CleanName(grenade.gameObject.name);

        var missile = root.GetComponentInParent<EnemyMissile>();
        if (missile != null)
            return "Missile:" + CleanName(missile.gameObject.name);

        var homing = root.GetComponentInParent<EnemyHomingMissile>();
        if (homing != null)
            return "HomingMissile:" + CleanName(homing.gameObject.name);

        var explosion = root.GetComponentInParent<EnemyGrenadeExplosion>();
        if (explosion != null)
            return "Explosion:" + CleanName(explosion.gameObject.name);

        var enemy = root.GetComponentInParent<Enemy>();
        if (enemy != null)
        {
            string kind = attacker.attackType == AttackType.Melee ? "Melee" : "Enemy";
            return kind + ":" + enemy.GetType().Name;
        }

        return CleanName(attacker.gameObject.name);
    }

    static string CleanName(string name)
    {
        if (string.IsNullOrEmpty(name))
            return "Unknown";
        return name.Replace("(Clone)", "").Trim();
    }

    void ResetCounters()
    {
        elapsedSeconds = 0f;
        furthestRank = 0;
        furthestProgress = "None";
        furthestReachedAtSeconds = 0f;
        gameClearSeconds = null;
        tutorialClearSeconds = null;
        wasInTutorial = false;
        deathOrFailCount = 0;
        robotSummonCount = 0;
        robotAliveSeconds = 0f;
        ammoUsedS = 0;
        ammoUsedM = 0;
        ammoUsedL = 0;
        hookUseCount = 0;
        lifePointCollected = 0;
        ability1UseCount = 0;
        ability2UseCount = 0;
        weaponSwitchCount = 0;
        sceneHazardDeathCount = 0;
        meleeJumpCount = 0;
        meleeDoubleJumpCount = 0;
        meleeSlideCount = 0;
        damageTakenBySource.Clear();
        sceneHazardDeathsBySource.Clear();
    }

    string ResolveCharacterName()
    {
        if (sceneLoader == null)
            sceneLoader = GetComponent<SceneLoader>();
        if (sceneLoader == null || sceneLoader.selectedCharacter == null)
            return null;

        var character = sceneLoader.selectedCharacter;
        return string.IsNullOrEmpty(character.displayName) ? character.name : character.displayName;
    }

    static void ResolveProgress(GameSceneSO scene, bool isTutorial, out string label, out int rank)
    {
        if (isTutorial)
        {
            label = "Tutorial";
            rank = 1;
            return;
        }

        string name = scene != null ? scene.name : null;
        if (string.IsNullOrEmpty(name))
        {
            label = "Unknown";
            rank = 1;
            return;
        }

        if (name.IndexOf("Tutorial", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            label = "Tutorial";
            rank = 1;
            return;
        }

        if (name.IndexOf("Stage3", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            label = "Stage3";
            rank = 4;
            return;
        }

        if (name.IndexOf("Stage2", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            label = "Stage2";
            rank = 3;
            return;
        }

        if (name.IndexOf("Stage1", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            label = "Stage1";
            rank = 2;
            return;
        }

        label = name;
        rank = 2;
    }

    static string AllocateFilePath(DateTime startedAt)
    {
        string folder = Application.persistentDataPath + FolderName;
        if (!Directory.Exists(folder))
            Directory.CreateDirectory(folder);

        string stamp = startedAt.ToString("yyyyMMdd_HHmmss");
        string path = folder + $"session_{stamp}.json";
        int suffix = 2;
        while (File.Exists(path))
        {
            path = folder + $"session_{stamp}_{suffix}.json";
            suffix++;
        }

        return path;
    }

    void WriteFile(string reason)
    {
        if (string.IsNullOrEmpty(pendingFilePath))
            pendingFilePath = AllocateFilePath(sessionStartedAt);

        string folder = Path.GetDirectoryName(pendingFilePath);
        if (!string.IsNullOrEmpty(folder) && !Directory.Exists(folder))
            Directory.CreateDirectory(folder);

        var record = new PlaySessionRecord
        {
            sessionStartedAt = sessionStartedAt.ToString(TimeStampFormat),
            sessionEndedAt = DateTime.Now.ToString(TimeStampFormat),
            endedBy = reason,
            character = characterName,
            furthestProgress = furthestProgress,
            furthestReachedAt = FormatElapsed(furthestReachedAtSeconds),
            furthestReachedAtSeconds = RoundTime(furthestReachedAtSeconds),
            gameClearTime = gameClearSeconds.HasValue ? FormatElapsed(gameClearSeconds.Value) : null,
            gameClearSeconds = gameClearSeconds.HasValue ? RoundTime(gameClearSeconds.Value) : (float?)null,
            tutorialClearTime = tutorialClearSeconds.HasValue ? FormatElapsed(tutorialClearSeconds.Value) : null,
            tutorialClearSeconds = tutorialClearSeconds.HasValue ? RoundTime(tutorialClearSeconds.Value) : (float?)null,
            deathOrFailCount = deathOrFailCount,
            robotSummonCount = robotSummonCount,
            robotAliveSeconds = RoundTime(robotAliveSeconds),
            ammoUsedS = ammoUsedS,
            ammoUsedM = ammoUsedM,
            ammoUsedL = ammoUsedL,
            hookUseCount = hookUseCount,
            lifePointCollected = lifePointCollected,
            ability1UseCount = ability1UseCount,
            ability2UseCount = ability2UseCount,
            weaponSwitchCount = weaponSwitchCount,
            sceneHazardDeathCount = sceneHazardDeathCount,
            meleeJumpCount = meleeJumpCount,
            meleeDoubleJumpCount = meleeDoubleJumpCount,
            meleeSlideCount = meleeSlideCount,
            damageTakenBySource = new Dictionary<string, int>(damageTakenBySource),
            sceneHazardDeathsBySource = new Dictionary<string, int>(sceneHazardDeathsBySource)
        };

        File.WriteAllText(pendingFilePath, JsonConvert.SerializeObject(record, Formatting.Indented));
        Debug.Log($"PlaySessionRecorder: wrote {pendingFilePath}");
    }

    static float RoundTime(float seconds) => Mathf.Round(seconds * 10f) / 10f;

    static string FormatElapsed(float seconds)
    {
        int total = Mathf.Max(0, Mathf.FloorToInt(seconds));
        int minutes = total / 60;
        int secs = total % 60;
        return $"{minutes}:{secs:D2}";
    }

    class PlaySessionRecord
    {
        public string sessionStartedAt;
        public string sessionEndedAt;
        public string endedBy;
        public string character;
        public string furthestProgress;
        public string furthestReachedAt;
        public float furthestReachedAtSeconds;
        public string gameClearTime;
        public float? gameClearSeconds;
        public string tutorialClearTime;
        public float? tutorialClearSeconds;
        public int deathOrFailCount;
        public int robotSummonCount;
        public float robotAliveSeconds;
        public int ammoUsedS;
        public int ammoUsedM;
        public int ammoUsedL;
        public int hookUseCount;
        public int lifePointCollected;
        public int ability1UseCount;
        public int ability2UseCount;
        public int weaponSwitchCount;
        public int sceneHazardDeathCount;
        public int meleeJumpCount;
        public int meleeDoubleJumpCount;
        public int meleeSlideCount;
        public Dictionary<string, int> damageTakenBySource;
        public Dictionary<string, int> sceneHazardDeathsBySource;
    }
}
