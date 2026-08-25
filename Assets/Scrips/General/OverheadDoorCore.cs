using System.IO;
using UnityEngine;

/// <summary>
/// 升降门核心：可受击、永不击破；将 Attack.damage 转交给 OverheadDoor。
/// </summary>
public class OverheadDoorCore : MonoBehaviour, IHitCountable
{
    // #region agent log
    const string DebugLogPath = "D:/Github/FinalProject/debug-a85fa1.log";
    void AgentLog(string hypothesisId, string location, string message, string dataJson)
    {
        try
        {
            long ts = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            File.AppendAllText(DebugLogPath,
                "{\"sessionId\":\"a85fa1\",\"hypothesisId\":\"" + hypothesisId +
                "\",\"location\":\"" + location + "\",\"message\":\"" + message +
                "\",\"data\":" + dataJson + ",\"timestamp\":" + ts + "}\n");
        }
        catch { }
    }
    // #endregion

    [Header("目标")]
    [Tooltip("留空则在父级查找 OverheadDoor")]
    [SerializeField] OverheadDoor door;

    [Header("命中")]
    [Tooltip("同一攻击实例同一帧内去重（Bob Trigger + Overlap 双路径）")]
    [SerializeField] bool dedupeSameAttackSameFrame = true;

    [Header("受击反馈")]
    [Tooltip("受击闪烁颜色；a=0 则关闭")]
    [SerializeField] Color hitFlashColor = new Color(1f, 0.35f, 0.35f, 1f);
    [SerializeField, Min(0f)] float hitFlashDuration = 0.08f;

    Attack lastHitAttacker;
    int lastHitFrame = -1;

    SpriteRenderer[] spriteRenderers;
    Color[] originalColors;
    float flashTimer;
    bool flashing;

    void Awake()
    {
        if (door == null)
            door = GetComponentInParent<OverheadDoor>();

        spriteRenderers = GetComponentsInChildren<SpriteRenderer>(true);
        originalColors = new Color[spriteRenderers.Length];
        for (int i = 0; i < spriteRenderers.Length; i++)
        {
            if (spriteRenderers[i] != null)
                originalColors[i] = spriteRenderers[i].color;
        }

        var selfCol2d = GetComponents<Collider2D>();
        var childCol2d = GetComponentsInChildren<Collider2D>(true);
        // #region agent log
        AgentLog("C", "OverheadDoorCore.Awake", "core_collider_snapshot",
            "{\"name\":\"" + name +
            "\",\"layer\":" + gameObject.layer +
            ",\"doorNull\":" + (door == null ? "true" : "false") +
            ",\"selfCol2d\":" + selfCol2d.Length +
            ",\"childCol2d\":" + childCol2d.Length + "}");
        // #endregion
    }

    void Update()
    {
        if (!flashing)
            return;

        flashTimer -= Time.deltaTime;
        if (flashTimer > 0f)
            return;

        flashing = false;
        RestoreOriginalColors();
    }

    public bool RegisterHit(Attack attacker)
    {
        if (door == null)
            return false;

        if (dedupeSameAttackSameFrame
            && attacker != null
            && attacker == lastHitAttacker
            && lastHitFrame == Time.frameCount)
            return true;

        lastHitAttacker = attacker;
        lastHitFrame = Time.frameCount;

        int damage = attacker != null ? attacker.damage : 0;
        door.ApplyDamage(damage);
        BeginHitFlash();
        return true;
    }

    void BeginHitFlash()
    {
        if (hitFlashDuration <= 0f || hitFlashColor.a <= 0f || spriteRenderers == null)
            return;

        flashing = true;
        flashTimer = hitFlashDuration;
        for (int i = 0; i < spriteRenderers.Length; i++)
        {
            if (spriteRenderers[i] != null)
                spriteRenderers[i].color = hitFlashColor;
        }
    }

    void RestoreOriginalColors()
    {
        if (spriteRenderers == null)
            return;

        for (int i = 0; i < spriteRenderers.Length; i++)
        {
            if (spriteRenderers[i] == null)
                continue;
            spriteRenderers[i].color = originalColors[i];
        }
    }
}
