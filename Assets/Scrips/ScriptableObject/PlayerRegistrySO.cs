using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Player/Player Registry SO")]
public class PlayerRegistrySO : ScriptableObject
{
    public List<PlayerCharacterSO> characters = new List<PlayerCharacterSO>();
    public PlayerCharacterSO defaultCharacter;

    public PlayerCharacterSO GetByIndex(int index)
    {
        if (index < 0 || index >= characters.Count)
            return null;

        return characters[index];
    }

    public int IndexOf(PlayerCharacterSO character)
    {
        return characters.IndexOf(character);
    }
}
