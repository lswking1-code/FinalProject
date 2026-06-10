using UnityEngine;
using UnityEngine.Events;
[CreateAssetMenu(menuName = "Event/SceneLoadEventSO")]
public class SceneLoadEventSO: ScriptableObject
{
    public UnityAction<GameSceneSO, Vector3, bool> LoadRequestEvent;

   /// <summary>
   /// 场景加载请求
   /// </summary>
   /// <param name="locationToLoad">要加载的场景</param>
   /// <param name="posToGo">玩家目标坐标</param>
   /// <param name="fadeScreen">是否使用渐入渐出</param>
    public void RaiseLoadRequestEvent(GameSceneSO locationToLoad,Vector3 posToGo,bool fadeScreen)
    {
        LoadRequestEvent?.Invoke(locationToLoad, posToGo, fadeScreen);
    }
}