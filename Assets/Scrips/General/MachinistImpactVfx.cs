using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Cosmetic-only, scene-local pool. No colliders, damage, time-scale changes or gameplay random calls.
/// Ballistic hits use original V2 art; melee retains its independently configured animation.
/// </summary>
public class MachinistImpactVfx : MonoBehaviour
{
    /// <summary>Temporary: set true to restore Slash / BlastSlash hit VFX.</summary>
    const bool EnableMeleeSlashImpact = false;

    sealed class Burst
    {
        public Transform root;
        public SpriteRenderer core;
        public SpriteRenderer accent;
        public SpriteRenderer debris;
        public bool debrisActive;
        public float debrisScale;
        public float debrisFrameRate;
        public MaterialPropertyBlock debrisProperties;
        public Sprite[] frames;
        public Sprite[] accentFrames;
        public float started;
        public float duration;
        public float scale;
        public float frameRate;
        public float pixelsPerUnit;
        public Color tint;
        public bool ballistic;
        public bool legacyPixelGrid;
        public MaterialPropertyBlock accentProperties;
        public MaterialPropertyBlock properties;
        public float pixelStep;
        public bool slash;
        public bool active;
    }

    static MachinistImpactVfx instance;
    static bool warned;
    MachinistImpactLibrary library;
    Material material;
    Material ballisticMaterial;
    Burst[] pool;
    int cursor;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { instance = null; warned = false; }

    public static MachinistImpactKind ResolveKind(Attack attack)
    {
        if (attack == null) return MachinistImpactKind.None;
        if (attack.impactKind != MachinistImpactKind.Auto) return attack.impactKind;

        bool melee = attack.GetComponentInParent<MachinistShooting>(true) != null
            || attack.GetComponentInParent<AllyRobot>(true) != null;
        bool ammo = attack.GetComponentInParent<PlayerMNormalBullet>(true) != null
            || attack.GetComponentInParent<PlayerMChargeBullet>(true) != null
            || attack.GetComponentInParent<PlayerMLChargeBullet>(true) != null
            || attack.GetComponentInParent<PlayerMSustainBullet>(true) != null;
        bool bomb = attack.GetComponentInParent<EnemyMarkBomb>(true) != null
            || attack.GetComponentInParent<BombBlastExplosion>(true) != null;
        if (!melee && !ammo && !bomb) return MachinistImpactKind.None;

        for (Transform t = attack.transform; t != null; t = t.parent)
            if (t.CompareTag("Electric")) return MachinistImpactKind.Electric;
        if (melee) return Attack.HasBlastTag(attack.transform)
            ? MachinistImpactKind.BlastSlash : MachinistImpactKind.Slash;
        if (bomb || Attack.HasBlastTag(attack.transform) || attack.damage >= 25
            || attack.GetComponentInParent<PlayerMChargeBullet>(true) != null
            || attack.GetComponentInParent<PlayerMLChargeBullet>(true) != null)
            return MachinistImpactKind.Heavy;
        return MachinistImpactKind.Bullet;
    }

    /// <summary>Contact on the target surface, including overlapping/fast projectile centres.</summary>
    public static Vector2 ContactPoint(Attack attack, Collider2D target)
    {
        if (target == null) return attack.transform.position;
        // Collider distance keeps contacts local on large/concave terrain instead of choosing
        // the far edge of the entire level's bounds. It also handles overlapping melee boxes.
        var hitCollider = attack.GetComponent<Collider2D>();
        if (hitCollider != null && hitCollider.enabled && target.enabled
            && hitCollider.gameObject.activeInHierarchy && target.gameObject.activeInHierarchy)
        {
            ColliderDistance2D contact = hitCollider.Distance(target);
            if (contact.isValid) return contact.pointB;
        }
        Vector2 incoming = attack.transform.right;
        var body = attack.GetComponent<Rigidbody2D>();
        if (body != null && body.linearVelocity.sqrMagnitude > 0.001f)
            incoming = body.linearVelocity.normalized;
        Vector2 source = attack.transform.position;
        if (attack.GetComponentInParent<MachinistShooting>(true) != null
            || attack.GetComponentInParent<AllyRobot>(true) != null)
        {
            var hitbox = attack.GetComponent<Collider2D>();
            if (hitbox != null) source = hitbox.bounds.center;
        }
        // Start outside the target so ClosestPoint cannot return an interior point.
        float reach = target.bounds.extents.magnitude * 2f + 0.25f;
        return target.ClosestPoint(source - incoming.normalized * reach);
    }

    public static void Play(MachinistImpactKind kind, Vector3 point, Vector2 direction, float size = 1f,
        MachinistImpactKind sourceKind = MachinistImpactKind.Auto)
    {
        if (!Application.isPlaying || kind == MachinistImpactKind.None || kind == MachinistImpactKind.Auto)
            return;
        if (!EnableMeleeSlashImpact
            && (kind == MachinistImpactKind.Slash || kind == MachinistImpactKind.BlastSlash))
            return;
        if (instance == null)
        {
            var data = Resources.Load<MachinistImpactLibrary>("MachinistImpactLibrary");
            if (data == null || data.shader == null)
            {
                if (!warned) Debug.LogWarning("Machinist impact library/shader is missing.");
                warned = true;
                return;
            }
            var go = new GameObject("Machinist Impact Pool");
            instance = go.AddComponent<MachinistImpactVfx>();
            instance.Initialize(data);
        }
        instance.Spawn(kind, point, direction, size, sourceKind);
    }

    void Initialize(MachinistImpactLibrary data)
    {
        library = data;
        material = new Material(data.shader) { name = "Machinist Impact (runtime)" };
        if (data.ballisticShader != null)
            ballisticMaterial = new Material(data.ballisticShader) { name = "Machinist Ballistic V2 (runtime)" };
        pool = new Burst[Mathf.Clamp(data.poolCapacity, 8, 96)];
        SceneManager.activeSceneChanged += OnSceneChanged;
    }

    void OnSceneChanged(Scene previous, Scene next)
    {
        // Also clears when the active scene is replaced additively under a persistent gameplay scene.
        if (pool == null) return;
        foreach (var burst in pool)
            if (burst != null) { burst.active = false; burst.root.gameObject.SetActive(false); }
    }

    Burst CreateBurst()
    {
        var go = new GameObject("Impact");
        go.transform.SetParent(transform, false);
        return new Burst { root = go.transform, core = CreateRenderer(go.transform, "Flash", 0),
            accent = CreateRenderer(go.transform, "Sparks or slash", 1),
            debris = CreateRenderer(go.transform, "Ballistic overlay", -1) };
    }

    SpriteRenderer CreateRenderer(Transform parent, string label, int order)
    {
        var go = new GameObject(label);
        go.transform.SetParent(parent, false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sharedMaterial = material;
        sr.sortingLayerName = library.sortingLayer;
        sr.sortingOrder = library.sortingOrder + order;
        return sr;
    }

    void Spawn(MachinistImpactKind kind, Vector3 point, Vector2 direction, float size, MachinistImpactKind sourceKind)
    {
        Sprite[] frames = library.Frames(kind);
        if (frames == null || frames.Length == 0) return;
        // Reuse a free slot, or replace the oldest burst under extreme fire.
        int slot = cursor;
        for (int i = 0; i < pool.Length; i++)
        {
            int candidate = (cursor + i) % pool.Length;
            if (pool[candidate] == null || !pool[candidate].active) { slot = candidate; break; }
        }
        Burst b = pool[slot] ?? (pool[slot] = CreateBurst());
        cursor = (slot + 1) % pool.Length;
        b.frames = frames;
        b.ballistic = library.UsesBallistic(kind);
        b.legacyPixelGrid = kind == MachinistImpactKind.Heavy;
        b.slash = kind == MachinistImpactKind.Slash || kind == MachinistImpactKind.BlastSlash;
        bool heavy = kind == MachinistImpactKind.Heavy || kind == MachinistImpactKind.BlastSlash;
        b.accentFrames = b.ballistic || b.slash ? null : heavy ? library.ring : null;
        b.frameRate = b.ballistic ? library.ballisticFramesPerSecond * (heavy ? 0.85f : 1f)
            : library.framesPerSecond;
        b.frameRate = Mathf.Max(1f, b.frameRate);
        b.pixelsPerUnit = b.ballistic ? Mathf.Max(1f, library.ballisticPixelsPerUnit) : 24f;
        b.scale = Mathf.Clamp(size, 0.1f, 4f) * library.size
            * (heavy ? 1.1f : kind == MachinistImpactKind.Surface ? 0.5f
                : kind == MachinistImpactKind.Bullet ? 0.65f
                : kind == MachinistImpactKind.Shield ? 0.75f : 1f);
        if (b.ballistic) b.scale *= library.ballisticSize;
        if (b.slash) b.scale *= 1.15f;
        b.pixelStep = Mathf.Max(1f, library.ballisticWorldPixelSize * b.pixelsPerUnit / b.scale);
        b.duration = Mathf.Max(frames.Length, b.accentFrames == null ? 0 : b.accentFrames.Length) / b.frameRate;
        b.debrisActive = kind == MachinistImpactKind.Heavy && ballisticMaterial != null
            && library.ballistic != null && library.ballistic.Length > 0;
        b.debrisScale = b.scale * library.ballisticSize;
        b.debrisFrameRate = Mathf.Max(1f, library.ballisticFramesPerSecond * 0.85f);
        if (b.debrisActive)
        {
            b.debris.sharedMaterial = ballisticMaterial;
            b.duration = Mathf.Max(b.duration, library.ballistic.Length / b.debrisFrameRate);
        }
        b.started = Time.time;
        b.active = true;
        b.root.position = point;
        float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
        b.root.rotation = Quaternion.Euler(0f, 0f, angle);
        b.root.localScale = Vector3.one;
        b.core.color = kind == MachinistImpactKind.Electric ? library.energy
            : kind == MachinistImpactKind.Heavy ? library.heavyColor
            : kind == MachinistImpactKind.Shield || kind == MachinistImpactKind.Surface ? library.metal : library.warm;
        b.core.sharedMaterial = b.ballistic ? ballisticMaterial : material;
        if (b.ballistic)
        {
            // Surface/Shield determine the contact size, but retain the ammunition's palette.
            var palette = (kind == MachinistImpactKind.Surface || kind == MachinistImpactKind.Shield)
                && sourceKind != MachinistImpactKind.Auto ? sourceKind : kind;
            b.core.color = palette == MachinistImpactKind.Electric ? library.energy
                : palette == MachinistImpactKind.Heavy ? library.heavyColor : Color.white;
        }
        if (b.slash) b.core.color = new Color(1f, 0.9f, 0.65f, 1f);
        b.core.transform.localRotation = Quaternion.Euler(0f, 0f, b.slash ? -35f : 0f);
        b.tint = b.core.color;
        b.accent.color = b.slash ? new Color(1f, 0.9f, 0.65f, 1f) : b.core.color;
        b.accent.transform.localRotation = Quaternion.Euler(0f, 0f, b.slash ? -35f : 0f);
        b.root.gameObject.SetActive(true);
        Draw(b, 0f);
    }

    void Update()
    {
        if (pool == null) return;
        foreach (Burst b in pool)
        {
            if (b == null || !b.active) continue;
            float age = Time.time - b.started;
            if (age >= b.duration)
            {
                b.active = false;
                b.root.gameObject.SetActive(false);
            }
            else Draw(b, age);
        }
    }

    void Draw(Burst b, float age)
    {
        float position = Mathf.Max(0f, age) * b.frameRate;
        int frame = Mathf.FloorToInt(position);
        Color tint = b.tint;
        if (b.ballistic) tint.a *= 1f - Mathf.SmoothStep(0f, 1f,
            Mathf.InverseLerp(b.frames.Length - 2f, b.frames.Length, position));
        b.core.color = tint;
        SetFrame(b.core, b.frames, frame, b.scale, b.pixelsPerUnit);
        if ((b.ballistic || b.legacyPixelGrid) && b.core.enabled)
        {
            if (b.properties == null) b.properties = new MaterialPropertyBlock();
            Sprite sprite = b.core.sprite;
            // Anchor the coarse grid at contact, even when animation frames use different pivots.
            Vector2 pivot = sprite.rect.position + sprite.pivot;
            b.properties.SetFloat("_PixelStep", b.pixelStep);
            b.properties.SetVector("_PixelPivot", new Vector4(pivot.x, pivot.y, 0f, 0f));
            b.core.SetPropertyBlock(b.properties);
        }
        else if (!b.ballistic && !b.legacyPixelGrid) b.core.SetPropertyBlock(null);
        SetFrame(b.accent, b.accentFrames, frame, b.scale * (b.slash ? 1.15f : 1.1f), 24f);
        if (b.legacyPixelGrid && b.accent.enabled)
        {
            if (b.accentProperties == null) b.accentProperties = new MaterialPropertyBlock();
            Vector2 pivot = b.accent.sprite.rect.position + b.accent.sprite.pivot;
            b.accentProperties.SetFloat("_PixelStep", Mathf.Max(1f,
                library.ballisticWorldPixelSize * 24f / (b.scale * 1.1f)));
            b.accentProperties.SetVector("_PixelPivot", new Vector4(pivot.x, pivot.y, 0f, 0f));
            b.accent.SetPropertyBlock(b.accentProperties);
        }
        else if (!b.legacyPixelGrid) b.accent.SetPropertyBlock(null);
        b.debris.enabled = false;
        if (b.debrisActive)
        {
            float debrisPosition = Mathf.Max(0f, age) * b.debrisFrameRate;
            float ppu = Mathf.Max(1f, library.ballisticPixelsPerUnit);
            SetFrame(b.debris, library.ballistic, Mathf.FloorToInt(debrisPosition), b.debrisScale, ppu);
            if (b.debris.enabled)
            {
                Color debrisTint = b.tint;
                debrisTint.a *= 1f - Mathf.SmoothStep(0f, 1f,
                    Mathf.InverseLerp(library.ballistic.Length - 2f, library.ballistic.Length, debrisPosition));
                b.debris.color = debrisTint;
                if (b.debrisProperties == null) b.debrisProperties = new MaterialPropertyBlock();
                Vector2 pivot = b.debris.sprite.rect.position + b.debris.sprite.pivot;
                b.debrisProperties.SetFloat("_PixelStep", Mathf.Max(1f,
                    library.ballisticWorldPixelSize * ppu / b.debrisScale));
                b.debrisProperties.SetVector("_PixelPivot", new Vector4(pivot.x, pivot.y, 0f, 0f));
                b.debris.SetPropertyBlock(b.debrisProperties);
            }
        }
    }

    static void SetFrame(SpriteRenderer renderer, Sprite[] frames, int frame, float scale, float pixelsPerUnit)
    {
        renderer.enabled = frames != null && frame < frames.Length && frames[frame] != null;
        if (!renderer.enabled) return;
        renderer.sprite = frames[frame];
        // Fixed pixel density preserves frame alignment and works with tight or full-rect imports.
        renderer.transform.localScale = Vector3.one * (renderer.sprite.pixelsPerUnit / pixelsPerUnit * scale);
    }

    void OnDestroy()
    {
        SceneManager.activeSceneChanged -= OnSceneChanged;
        if (material != null) Destroy(material);
        if (ballisticMaterial != null) Destroy(ballisticMaterial);
        if (instance == this) instance = null;
    }
}
