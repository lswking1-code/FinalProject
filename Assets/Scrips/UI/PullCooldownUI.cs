using UnityEngine;
using UnityEngine.UI;

public class PullCooldownUI : MonoBehaviour
{
    [SerializeField] Image cooldownFill;
    [SerializeField] Image icon;
    [SerializeField, Range(0f, 1f)] float unavailableIconAlpha = 0.35f;

    PlayerAbilities playerAbilities;
    float availableIconAlpha = 1f;

    void Awake()
    {
        if (icon != null)
            availableIconAlpha = icon.color.a;

        ConfigureCooldownFill();
        Refresh();
    }

    void LateUpdate()
    {
        if (playerAbilities == null || !playerAbilities.isActiveAndEnabled)
            playerAbilities = FindFirstObjectByType<PlayerAbilities>();

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
        bool hasRobot = playerAbilities != null && playerAbilities.HasRobot;
        float cooldown = hasRobot ? playerAbilities.PullCooldownNormalized : 0f;

        if (cooldownFill != null)
        {
            cooldownFill.fillAmount = cooldown;
            cooldownFill.enabled = cooldown > 0f;
        }

        if (icon != null)
        {
            Color color = icon.color;
            color.a = hasRobot ? availableIconAlpha : unavailableIconAlpha;
            icon.color = color;
        }
    }
}
