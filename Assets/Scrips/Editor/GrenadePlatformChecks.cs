using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Uses an isolated physics scene; does not simulate or save the user's scene.</summary>
public static class GrenadePlatformChecks
{
    const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    [MenuItem("Tools/Gunner/验证手雷单向平台", true)]
    static bool CanRun() => !EditorApplication.isPlayingOrWillChangePlaymode;

    [MenuItem("Tools/Gunner/验证手雷单向平台")]
    public static void Run()
    {
        SidePass(-1, 6f);
        SidePass(1, 6f);
        SidePass(-1, 80f);
        using (var test = new Fixture())
        {
            test.Place(new Vector2(0f, -0.1f), Vector2.down);
            test.Step(10);
            Check(test.Body.position.y < -0.25f, "Must fall through when turning inside the platform");
            test.Place(new Vector2(0f, 0.8f), Vector2.down * 3f);
            test.Step(20);
            Check(test.Body.position.y >= 0.32f, "Must land after fully clearing the top");
            Check(!test.Ignored, "Top landing must restore collision");
        }
        using (var test = new Fixture())
        {
            test.Place(new Vector2(0f, -0.8f), Vector2.up * 3f);
            test.Step(25);
            Check(test.Body.position.y > 0.6f, "Must rise through the platform");
            test.Body.linearVelocity = Vector2.down * 3f;
            test.Step(20);
            Check(test.Body.position.y >= 0.32f, "Must land after rising through");
        }
        using (var test = new Fixture())
        {
            test.Platform.transform.rotation = Quaternion.Euler(0f, 0f, 25f);
            Vector2 top = test.Platform.transform.TransformPoint(Vector2.up * 0.2f);
            Vector2 normal = test.Platform.transform.up;
            test.Place(top + normal * 0.2f, Vector2.zero);
            test.Update();
            Check(test.Ignored, "Rotated platform must ignore an embedded grenade");
            test.Place(top + normal * 0.4f, Vector2.zero);
            test.Update();
            Check(!test.Ignored, "Rotated top must use its surface, not AABB height");
        }
        using (var test = new Fixture())
        {
            test.Place(new Vector2(0f, -0.1f), Vector2.zero);
            test.Update();
            Check(test.Ignored, "Spawn inside must ignore immediately");
            Invoke(test.Grenade, "OnDisable");
            Check(!test.Ignored, "Disable must restore ignored pairs");
            test.Update();
            test.Place(new Vector2(10f, 0f), Vector2.zero);
            test.Update();
            Check(!test.Ignored, "Leaving the scan must restore ignored pairs");
            test.Platform.GetComponent<PlatformEffector2D>().useOneWay = false;
            test.Place(new Vector2(0f, -0.1f), Vector2.zero);
            test.Update();
            Check(!test.Ignored, "Solid platforms must retain collision");
        }
        Debug.Log("GrenadePlatformChecks: all checks passed.");
    }

    static void SidePass(int side, float speed)
    {
        using (var test = new Fixture())
        {
            test.Place(new Vector2(side * 2.5f, -0.1f), new Vector2(-side * speed, 0f));
            test.Step(speed > 10f ? 2 : 12);
            Check(side * test.Body.position.x < 1.2f, "Side approach must pass without blocking");
            Check(-side * test.Body.linearVelocity.x > speed * 0.95f, "Side must not remove horizontal speed");
        }
    }

    static void Invoke(PlayerGrenade grenade, string method) =>
        typeof(PlayerGrenade).GetMethod(method, PrivateInstance).Invoke(grenade, null);

    static void Check(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException("GrenadePlatformChecks: " + message);
    }

    sealed class Fixture : IDisposable
    {
        readonly Scene scene;
        readonly PhysicsScene2D physics;
        public readonly BoxCollider2D Platform;
        public readonly PlayerGrenade Grenade;
        public readonly Rigidbody2D Body;
        readonly CircleCollider2D circle;
        public bool Ignored => Physics2D.GetIgnoreCollision(circle, Platform);

        public Fixture()
        {
            scene = SceneManager.CreateScene("Grenade checks " + Guid.NewGuid(),
                new CreateSceneParameters(LocalPhysicsMode.Physics2D));
            physics = scene.GetPhysicsScene2D();
            var platformObject = new GameObject("Test platform");
            SceneManager.MoveGameObjectToScene(platformObject, scene);
            platformObject.layer = LayerMask.NameToLayer("Platform");
            platformObject.transform.position = new Vector3(0f, -0.2f);
            Platform = platformObject.AddComponent<BoxCollider2D>();
            Platform.size = new Vector2(4f, 0.4f);
            Platform.usedByEffector = true;
            var effector = platformObject.AddComponent<PlatformEffector2D>();
            effector.useOneWay = true;
            effector.surfaceArc = 180f;
            effector.useSideFriction = false;
            effector.useSideBounce = false;
            var grenadeObject = new GameObject("Test grenade");
            SceneManager.MoveGameObjectToScene(grenadeObject, scene);
            // Default layer avoids modifying the project's collision matrix during checks.
            Body = grenadeObject.AddComponent<Rigidbody2D>();
            circle = grenadeObject.AddComponent<CircleCollider2D>();
            circle.radius = 0.35f;
            grenadeObject.AddComponent<Animator>();
            Grenade = grenadeObject.AddComponent<PlayerGrenade>();
            Invoke(Grenade, "Awake");
            Body.gravityScale = 0f;
            Body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        }

        public void Place(Vector2 position, Vector2 velocity)
        {
            Body.position = position;
            Body.linearVelocity = velocity;
            Physics2D.SyncTransforms();
        }

        public void Update() => Invoke(Grenade, "UpdatePlatformCollisions");

        public void Step(int count)
        {
            for (int i = 0; i < count; i++)
            {
                Update();
                physics.Simulate(Time.fixedDeltaTime);
            }
        }

        public void Dispose()
        {
            Invoke(Grenade, "OnDisable");
            var material = circle.sharedMaterial;
            EditorSceneManager.CloseScene(scene, true);
            if (material != null)
                Object.DestroyImmediate(material);
        }
    }
}
