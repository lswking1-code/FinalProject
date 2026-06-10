using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

[CreateAssetMenu(menuName = "Event/CharacterEventSO")]
public class CharacterEventSO : ScriptableObject
{
    public UnityAction<Character> OnEventRaised;// 角色相关事件

    public void RaiseEvent(Character character)// 广播事件
    {
        OnEventRaised?.Invoke(character);
    }
}
