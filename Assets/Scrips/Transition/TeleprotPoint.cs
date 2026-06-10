using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class TeleprotPoint : MonoBehaviour, IInteractable
{
    public SceneLoadEventSO loadEventSO;
    
    public GameSceneSO sceneToGo;
    public Vector3 positionToGo;//下一个场景玩家出生的默认位置

    public void TriggerAction()
    {
        Debug.Log("GO");

        loadEventSO.RaiseLoadRequestEvent(sceneToGo, positionToGo, true);
    }
}
