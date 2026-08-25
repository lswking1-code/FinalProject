using System;
using UnityEngine;

[CreateAssetMenu(menuName = "UI/Game Language")]
public class GameLanguageSO : ScriptableObject
{
    [Tooltip("勾选后，选人描述与教学关引导显示英文")]
    public bool useEnglish;

    public event Action Changed;

    public void SetUseEnglish(bool value)
    {
        if (useEnglish == value)
            return;

        useEnglish = value;
        Changed?.Invoke();
    }

    public void NotifyChanged() => Changed?.Invoke();
}
