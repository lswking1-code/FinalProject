using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Visual-only explosion sequence shared by destructible doors.</summary>
[CreateAssetMenu(menuName = "Effects/Door Destruction Effect")]
public class DoorDestructionEffect : ScriptableObject
{
    [Header("开关")]
    [Tooltip("关闭后所有使用本特效的门都不再播放爆炸，仍保留帧与参数")]
    public bool playEffect = true;

    public Sprite[] frames;
    public Material material;
    [Min(1f)] public float framesPerSecond = 16f;
    [Min(0.1f)] public float burstSpacing = 1.75f;
    [Min(0f)] public float burstDelay = 0.045f;
    [Range(1, 12)] public int maximumBursts = 8;
    public string sortingLayer = "Bullet";
    public int sortingOrder = 20;

    public DoorDestructionBurst Play(Transform target)
    {
        if (!playEffect || target == null || frames == null || frames.Length == 0)
            return null;

        Bounds bounds = new Bounds(target.position, Vector3.one);
        bool found = false;
        foreach (SpriteRenderer renderer in target.GetComponentsInChildren<SpriteRenderer>())
        {
            if (!renderer.enabled || renderer.sprite == null) continue;
            if (!found) { bounds = renderer.bounds; found = true; }
            else bounds.Encapsulate(renderer.bounds);
        }
        // Include the blocking panel even if its artwork covers only part of the collider.
        foreach (Collider2D collider in target.GetComponentsInChildren<Collider2D>())
        {
            if (!collider.enabled || collider.isTrigger) continue;
            if (!found) { bounds = collider.bounds; found = true; }
            else bounds.Encapsulate(collider.bounds);
        }

        // Independent of the door hierarchy, but owned by the same scene for unloading.
        var root = new GameObject("Door Destruction VFX");
        if (target.gameObject.scene.IsValid() && target.gameObject.scene.isLoaded)
            SceneManager.MoveGameObjectToScene(root, target.gameObject.scene);
        root.transform.position = bounds.center;
        var burst = root.AddComponent<DoorDestructionBurst>();
        burst.Initialize(this, bounds);
        return burst;
    }
}
