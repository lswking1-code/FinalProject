using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

[CreateAssetMenu(menuName = "Event/CharacterEventSO")]
public class CharacterEventSO : ScriptableObject
{
    public UnityAction<Character> OnEventRaised;//¶©ÔÄÊÂ¼ş

    public void RaiseEvent(Character character)//ºô½ĞÆôÓÃ
    {
        OnEventRaised?.Invoke(character);
    }
}
