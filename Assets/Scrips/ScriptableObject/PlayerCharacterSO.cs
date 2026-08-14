using UnityEngine;

[CreateAssetMenu(menuName = "Player/Player Character SO")]
public class PlayerCharacterSO : ScriptableObject
{
    public string displayName;
    [Tooltip("HUD 角色头像（PlayerIcon）")]
    public Sprite icon;
}
