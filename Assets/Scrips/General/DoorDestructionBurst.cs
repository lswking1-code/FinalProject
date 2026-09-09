using UnityEngine;

/// <summary>Finite sprite-only effect; deliberately has no attack or physics components.</summary>
public class DoorDestructionBurst : MonoBehaviour
{
    DoorDestructionEffect effect;
    SpriteRenderer[] bursts;
    float age;

    public void Initialize(DoorDestructionEffect profile, Bounds bounds)
    {
        effect = profile;
        bool vertical = bounds.size.y >= bounds.size.x;
        float length = vertical ? bounds.size.y : bounds.size.x;
        int count = Mathf.Clamp(Mathf.CeilToInt(length / Mathf.Max(0.1f, effect.burstSpacing)),
            1, Mathf.Clamp(effect.maximumBursts, 1, 12));
        bursts = new SpriteRenderer[count];
        for (int i = 0; i < count; i++)
        {
            var child = new GameObject("Explosion " + (i + 1));
            child.transform.SetParent(transform, false);
            float offset = ((i + 0.5f) / count - 0.5f) * length;
            child.transform.localPosition = vertical ? new Vector3(0, offset, 0) : new Vector3(offset, 0, 0);
            SpriteRenderer renderer = child.AddComponent<SpriteRenderer>();
            if (effect.material != null) renderer.sharedMaterial = effect.material;
            renderer.sortingLayerName = effect.sortingLayer;
            renderer.sortingOrder = effect.sortingOrder + i;
            bursts[i] = renderer;
        }
        RenderAtAge(0f);
    }

    void Update()
    {
        age += Time.deltaTime;
        if (!RenderAtAge(age)) Destroy(gameObject);
    }

    // Returns false once the last delayed burst has finished its smoke frames.
    internal bool RenderAtAge(float elapsed)
    {
        if (effect == null || effect.frames == null || effect.frames.Length == 0 || bursts == null)
            return false;
        bool pending = false;
        float rate = Mathf.Max(1f, effect.framesPerSecond);
        for (int i = 0; i < bursts.Length; i++)
        {
            float localAge = elapsed - i * Mathf.Max(0f, effect.burstDelay);
            int frame = Mathf.FloorToInt(localAge * rate);
            bool visible = frame >= 0 && frame < effect.frames.Length;
            if (bursts[i] != null)
            {
                bursts[i].enabled = visible;
                if (visible) bursts[i].sprite = effect.frames[frame];
            }
            pending |= frame < effect.frames.Length;
        }
        return pending;
    }
}
