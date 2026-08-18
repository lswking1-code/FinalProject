using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 敌人战斗实体、抛射物与运行时掉落物清理：确保实例落在关卡场景内，并在切场景时销毁残留。
/// </summary>
public static class EnemySceneCleanup
{
    /// <summary>
    /// 将实例移到与来源相同的场景，避免落到 Persistent 后随关卡卸载残留。
    /// </summary>
    public static void PlaceInSourceScene(GameObject instance, Component source)
    {
        if (instance == null || source == null)
            return;

        var sourceScene = source.gameObject.scene;
        if (!sourceScene.IsValid() || !sourceScene.isLoaded)
            return;

        if (instance.scene != sourceScene)
            SceneManager.MoveGameObjectToScene(instance, sourceScene);
    }

    /// <summary>
    /// 销毁场景中所有敌人本体 / 子弹 / 导弹 / 手雷 / 爆炸特效 / 弹药包 / 血包。
    /// 这些对象若 Instantiated 到 Persistent，卸关卡时不会随场景消失，需在此一并清掉。
    /// </summary>
    public static void ClearAll()
    {
        DestroyAll<Enemy>();
        DestroyAll<EnemyProjectile>();
        DestroyAll<EnemyMissile>();
        DestroyAll<EnemyGrenade>();
        DestroyAll<EnemyGrenadeExplosion>();
        DestroyAll<BulletBox>();
        DestroyAll<HealthPack>();
    }

    static void DestroyAll<T>() where T : Component
    {
        var items = Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < items.Length; i++)
        {
            if (items[i] != null)
                Object.Destroy(items[i].gameObject);
        }
    }
}
