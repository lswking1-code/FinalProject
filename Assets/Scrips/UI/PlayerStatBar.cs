using UnityEngine;
using UnityEngine.UI;

public class PlayerStatBar : MonoBehaviour
{
    Character currentCharacter;
    public Image healthImage;
    public Image healthDelayImage;
    public Image powerImage;
    public Image ApImage;

    bool isRecovering;

    void Update()
    {
        if (healthDelayImage != null && healthImage != null &&
            healthDelayImage.fillAmount > healthImage.fillAmount)
        {
            healthDelayImage.fillAmount -= Time.deltaTime;
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
            healthImage.fillAmount = persentage;
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
