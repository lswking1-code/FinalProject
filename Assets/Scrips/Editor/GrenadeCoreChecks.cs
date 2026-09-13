using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Play Mode checks in isolated physics scenes; never edits or saves the current level.</summary>
public static class GrenadeCoreChecks
{
    const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;

    [MenuItem("Tools/Gunner/验证手雷核心命中", true)]
    static bool CanRun() => EditorApplication.isPlaying;

    [MenuItem("Tools/Gunner/验证手雷核心命中")]
    public static void Run()
    {
        Check(EditorApplication.isPlaying, "Run these checks in Play Mode");
        foreach (int variant in new[] { 0, 1, 2 }) // Thrown, rolling (same component), straight.
        foreach (bool trigger in new[] { false, true })
        foreach (bool lift in new[] { false, true })
        {
            bool bullet = variant == 2;
            float speed = variant == 1 ? 4.5f : 6.5f;
            using var test = new Fixture(lift, trigger);
            var grenade = test.Fire(bullet, speed);
            test.Step();
            Check((bool)Get(grenade, "hasExploded"), "Core contact must detonate: " + (bullet, trigger, lift));
            Check(test.Explosions().Length == 1, "One projectile must produce exactly one explosion");
            var explosion = test.Explosions()[0];
            Check(explosion.transform.position.y > 9f, "High core contact must not snap down to the floor");
            test.AssertHits(1);
            // Repeated overlap passes and both child colliders must share Attack's deduplication.
            explosion.GetComponent<Attack>().ProcessOverlapHits();
            explosion.GetComponent<Attack>().ProcessOverlapHits();
            test.AssertHits(1);
            test.Fire(bullet, speed);
            test.Step();
            test.AssertHits(2);
            if (!lift)
            {
                test.Fire(bullet, speed);
                test.Step();
                Check(test.Prop.IsBroken, "Third grenade must break the three-hit core");
            }
            else test.AssertDoorCloses();
        }

        using (var test = new Fixture(false, true))
        {
            var neighbour = test.AddCountCore(new Vector3(0.3f, 10f), true);
            test.Fire(false);
            test.Step();
            test.AssertHits(1);
            Check(neighbour.CurrentHits == 1, "One blast must hit each overlapping core once");
        }

        using (var test = new Fixture(false, true))
        {
            Set(test.Prop, "isCore", false);
            var grenade = test.Fire(false);
            test.Step();
            Check(!(bool)Get(grenade, "hasExploded"), "Ordinary props must not gain contact detonation");
            // Fuse uses the existing no-argument entry point, independently of core contact.
            typeof(PlayerGrenade).GetMethod("Explode", Fields).Invoke(grenade, null);
            Check((bool)Get(grenade, "hasExploded"), "Fuse detonation must remain available");
        }
        Debug.Log("GrenadeCoreChecks: all checks passed (solid/trigger, thrown/straight, count/lift, high contact, deduplication, multiple targets and fuse).");
    }

    static void Set(object target, string name, object value) =>
        target.GetType().GetField(name, Fields).SetValue(target, value);

    static object Get(object target, string name) => target.GetType().GetField(name, Fields).GetValue(target);

    static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("GrenadeCoreChecks: " + message);
    }

    sealed class Fixture : IDisposable
    {
        readonly Scene scene;
        readonly Scene previous;
        readonly GrenadeExplosion template;
        readonly OverheadDoor door;
        readonly System.Collections.Generic.List<PhysicsMaterial2D> materials = new();
        public readonly BreakableProp Prop;

        public Fixture(bool lift, bool trigger)
        {
            previous = SceneManager.GetActiveScene();
            scene = SceneManager.CreateScene("Grenade core checks " + Guid.NewGuid(),
                new CreateSceneParameters(LocalPhysicsMode.Physics2D));
            SceneManager.SetActiveScene(scene);

            var floor = Create("Floor below high core", new Vector3(0f, 8.8f));
            floor.layer = LayerMask.NameToLayer("Ground");
            floor.AddComponent<BoxCollider2D>().size = new Vector2(8f, 0.2f);
            floor.SetActive(true);

            if (lift)
            {
                var gate = Create("Lift", new Vector3(10f, 10f));
                door = gate.AddComponent<OverheadDoor>();
                Set(door, "damageToFullyOpen", 100);
                gate.SetActive(true);
                var core = Create("Lift core", new Vector3(0f, 10f));
                Set(core.AddComponent<OverheadDoorCore>(), "door", door);
                AddColliders(core, trigger);
                core.SetActive(true);
            }
            else Prop = AddCountCore(new Vector3(0f, 10f), trigger);

            var blast = Create("Explosion template", new Vector3(1000f, 1000f));
            blast.layer = LayerMask.NameToLayer("PlayerBullet");
            var circle = blast.AddComponent<CircleCollider2D>();
            circle.radius = 0.8f;
            circle.isTrigger = true;
            var attack = blast.AddComponent<Attack>();
            attack.impactKind = MachinistImpactKind.None;
            template = blast.AddComponent<GrenadeExplosion>();
            blast.SetActive(true);
        }

        static GameObject Create(string name, Vector3 position)
        {
            var go = new GameObject(name);
            go.SetActive(false);
            go.transform.position = position;
            return go;
        }

        static void AddColliders(GameObject core, bool trigger)
        {
            // Untagged Enemy-layer child colliders match the shipped core prefabs.
            for (int i = 0; i < 2; i++)
            {
                var child = new GameObject("Core collider " + i);
                child.layer = LayerMask.NameToLayer("Enemy");
                child.transform.SetParent(core.transform, false);
                var circle = child.AddComponent<CircleCollider2D>();
                circle.radius = 0.5f;
                circle.isTrigger = trigger;
            }
        }

        public BreakableProp AddCountCore(Vector3 position, bool trigger)
        {
            var core = Create("Count core", position);
            var prop = core.AddComponent<BreakableProp>();
            Set(prop, "isCore", true);
            Set(prop, "hitsToBreak", 3);
            AddColliders(core, trigger);
            core.SetActive(true);
            return prop;
        }

        public MonoBehaviour Fire(bool bullet, float speed = 6.5f)
        {
            var go = Create("Test grenade", new Vector3(-1.1f, 10f));
            go.layer = LayerMask.NameToLayer("PlayerBullet");
            var body = go.AddComponent<Rigidbody2D>();
            go.AddComponent<CircleCollider2D>().radius = 0.35f;
            MonoBehaviour grenade = bullet ? (MonoBehaviour)go.AddComponent<PlayerGrenadeBullet>()
                : go.AddComponent<PlayerGrenade>();
            Set(grenade, "explosionPrefab", template);
            if (!bullet) Set(grenade, "groundLayer", (LayerMask)LayerMask.GetMask("Ground"));
            go.SetActive(true);
            body.gravityScale = 0f;
            body.linearVelocity = Vector2.right * speed;
            body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            if (!bullet) materials.Add(body.sharedMaterial);
            return grenade;
        }

        public void Step()
        {
            Physics2D.SyncTransforms();
            for (int i = 0; i < 8; i++) scene.GetPhysicsScene2D().Simulate(0.02f);
        }

        public GrenadeExplosion[] Explosions() => Array.FindAll(
            Object.FindObjectsByType<GrenadeExplosion>(FindObjectsSortMode.None),
            explosion => explosion.gameObject.scene == scene && explosion != template);

        public void AssertHits(int hits)
        {
            if (door != null)
                Check(Mathf.Approximately(door.Progress, Mathf.Clamp01(hits * 0.4f)), "Lift must apply 40 damage once per grenade");
            else Check(Prop.CurrentHits == hits, "Count core must count each grenade once");
        }

        public void AssertDoorCloses()
        {
            Set(door, "lastHitTime", float.NegativeInfinity);
            float before = door.Progress;
            typeof(OverheadDoor).GetMethod("Update", Fields).Invoke(door, null);
            Check(door.Progress < before, "Lift must resume closing after its idle delay");
        }

        public void Dispose()
        {
            SceneManager.SetActiveScene(previous);
            foreach (var root in scene.GetRootGameObjects()) Object.DestroyImmediate(root);
            foreach (var material in materials)
                if (material != null) Object.DestroyImmediate(material);
            SceneManager.UnloadSceneAsync(scene);
        }
    }
}
