using System.Collections;
using System.Collections.Generic;
using UnityEngine;
public class WinZone : MonoBehaviour,IInteractable
{
    public VoidEventSO gameClearEvent;
    
    public void TriggerAction()
    {
        Debug.Log("Finish");
        gameClearEvent.RaiseEvent();
    }
}
