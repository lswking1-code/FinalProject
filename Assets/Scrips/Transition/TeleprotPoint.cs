using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class TeleprotPoint : MonoBehaviour, IInteractable
{
    public SceneLoadEventSO loadEventSO;
    
    public GameSceneSO sceneToGo;
    public Vector3 positionToGo;// 玩家进入目标场景后的出生位置

    public void TriggerAction()
    {
        Debug.Log("GO");

        loadEventSO.RaiseLoadRequestEvent(sceneToGo, positionToGo, true);
    }
}
