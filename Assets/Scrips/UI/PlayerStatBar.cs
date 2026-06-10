using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class PlayerStatBar : MonoBehaviour
{
    private Character currentCharacter;
    public Image healthImage;
    public Image healthDelayImage;
    public Image powerImage;
    public Image ApImage;

    private bool isRecovering;
    private bool isAPRecovering;

    private void Update()
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
            {
                isRecovering = false;
                return;
            }
        }

        if (isAPRecovering && ApImage != null && currentCharacter != null)
        {
            float APpersentage = currentCharacter.AbilityPower / currentCharacter.maxAbilityPower;
            ApImage.fillAmount = APpersentage;

            if (APpersentage >= 1)
            {
                isAPRecovering = false;
                return;
            }
        }


    }
    /// <summary>
    /// ????Health????????
    /// </summary>
    /// <param name="persentage"></param>
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
        isAPRecovering = true;
        currentCharacter = character;
    }

}
