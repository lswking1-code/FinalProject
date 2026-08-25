using UnityEngine;

[CreateAssetMenu(menuName = "UI/Game Language")]
public class GameLanguageSO : ScriptableObject
{
    [Tooltip("勾选后，选人描述与教学关引导显示英文")]
    public bool useEnglish;
}
