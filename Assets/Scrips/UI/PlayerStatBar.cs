using UnityEngine;
using UnityEngine.UI;

public class PlayerStatBar : MonoBehaviour
{
    Character currentCharacter;
    public Image healthImage;
    public Image healthDelayImage;
    public Image powerImage;
    public Image ApImage;

    [SerializeField, Tooltip("受伤后 Delay 条先停住的时间（秒）")]
    float delayHoldTime = 0.35f;
    [SerializeField, Tooltip("Delay 条每秒下降的 fillAmount，1 表示一秒内从满条掉到空")]
    float delayDropSpeed = 0.4f;

    bool isRecovering;
    float delayHoldTimer;

    void Update()
    {
        if (healthDelayImage != null && healthImage != null &&
            healthDelayImage.fillAmount > healthImage.fillAmount)
        {
            if (delayHoldTimer > 0f)
                delayHoldTimer -= Time.deltaTime;
            else
                healthDelayImage.fillAmount = Mathf.MoveTowards(
                    healthDelayImage.fillAmount,
                    healthImage.fillAmount,
                    delayDropSpeed * Time.deltaTime);
        }

        if (isRecovering && powerImage != null && currentCharacter != null)
        {
            float persentage = currentCharacter.currentPower / currentCharacter.maxPower;
            powerImage.fillAmount = persentage;

            if (persentage >= 1)
                isRecovering = false;
        }

        if (currentCharacter != null)
            SyncApImage();
    }

    public void OnHealthChange(float persentage)
    {
        if (healthImage != null)
        {
            if (persentage < healthImage.fillAmount)
                delayHoldTimer = delayHoldTime;

            healthImage.fillAmount = persentage;
        }

        // 回血/读档/满血重置时 Delay 必须立刻跟上，否则会一直低于当前血量，之后受伤看不到红条
        if (healthDelayImage != null && healthDelayImage.fillAmount < persentage)
            healthDelayImage.fillAmount = persentage;
    }

    public void OnPowerChange(Character character)
    {
        isRecovering = true;
        currentCharacter = character;
    }

    public void OnAPChange(Character character)
    {
        currentCharacter = character;
        SyncApImage();
    }

    void SyncApImage()
    {
        if (ApImage == null || currentCharacter == null)
            return;

        if (currentCharacter.maxAbilityPower <= 0f)
        {
            ApImage.fillAmount = 0f;
            return;
        }

        ApImage.fillAmount = currentCharacter.AbilityPower / currentCharacter.maxAbilityPower;
    }
}
