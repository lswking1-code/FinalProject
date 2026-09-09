using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// AbilityCD：按当前角色切换 Ability2 图标与冷却。
/// 机械师未召唤显示机器人图标，已召唤显示钩锁 CD；枪手显示翻滚 CD；近战隐藏整块视觉。
/// </summary>
public class PullCooldownUI : MonoBehaviour
{
    [SerializeField] Image cooldownFill;
    [SerializeField] Image icon;
    [SerializeField] GameObject visualRoot;
    [SerializeField] Sprite hookIcon;
    [SerializeField] Sprite robotIcon;
    [SerializeField] Sprite rollIcon;

    SceneLoader sceneLoader;
    Sprite fallbackIcon;

    void Awake()
    {
        if (visualRoot == null && transform.childCount > 0)
            visualRoot = transform.GetChild(0).gameObject;

        if (icon != null)
        {
            fallbackIcon = icon.sprite;
            if (hookIcon == null)
                hookIcon = icon.sprite;
        }

        ConfigureCooldownFill();
        Refresh();
    }

    void LateUpdate()
    {
        if (sceneLoader == null)
            sceneLoader = FindFirstObjectByType<SceneLoader>();

        Refresh();
    }

    void ConfigureCooldownFill()
    {
        if (cooldownFill == null)
            return;

        cooldownFill.type = Image.Type.Filled;
        cooldownFill.fillMethod = Image.FillMethod.Radial360;
        cooldownFill.fillOrigin = (int)Image.Origin360.Top;
        cooldownFill.fillClockwise = true;
        cooldownFill.raycastTarget = false;
    }

    void Refresh()
    {
        Transform player = sceneLoader != null ? sceneLoader.playerTrans : null;
        PlayerAbilities abilities = player != null ? player.GetComponent<PlayerAbilities>() : null;
        PlayerRoll roll = player != null ? player.GetComponent<PlayerRoll>() : null;

        if (abilities != null && abilities.isActiveAndEnabled)
        {
            ShowVisuals(true);
            bool hasRobot = abilities.HasRobot;
            SetIcon(hasRobot ? hookIcon : robotIcon);
            ApplyCooldown(hasRobot ? abilities.PullCooldownNormalized : 0f);
            return;
        }

        if (roll != null && roll.isActiveAndEnabled)
        {
            ShowVisuals(true);
            SetIcon(rollIcon);
            ApplyCooldown(roll.CooldownNormalized);
            return;
        }

        ShowVisuals(false);
    }

    void ShowVisuals(bool visible)
    {
        if (visualRoot != null && visualRoot.activeSelf != visible)
            visualRoot.SetActive(visible);
    }

    void SetIcon(Sprite sprite)
    {
        if (icon == null)
            return;

        Sprite next = sprite != null ? sprite : fallbackIcon;
        if (icon.sprite != next)
            icon.sprite = next;
    }

    void ApplyCooldown(float cooldown)
    {
        if (cooldownFill == null)
            return;

        cooldownFill.fillAmount = cooldown;
        cooldownFill.enabled = cooldown > 0f;
    }
}
