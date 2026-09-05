using UnityEngine;

/// <summary>
/// 可被击退的场景物（箱子等），无生命值，仅响应 Attack 击退。
/// 站在顶部时向玩家暴露刚体速度，供移动平台携带。
/// Blast 命中后按固定速度滑过一段距离，不受质量/阻力/地面摩擦限制；贴身推仍用常态高阻尼。
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
public class PushableProp : MonoBehaviour, IKnockbackable, IPlatformVelocityProvider
{
    [Tooltip("击退阻力，越大越难推；默认高于敌人(1)。仅用于非 Blast 击退")]
    [SerializeField] float knockbackResistance = 2.5f;
    [Tooltip("Blast 滑行速度（单位/秒）。角色宽约 1，10 约每秒 10 个身位")]
    [SerializeField] float blastSlideSpeed = 10f;
    [Tooltip("Blast 滑行距离。8 ≈ 七八个身位")]
    [SerializeField] float blastSlideDistance = 8f;
    [Tooltip("非 Blast 轻推冷却，避免机枪连发叠成大推")]
    [SerializeField] float lightPushCooldown = 0.15f;

    Rigidbody2D rb;
    Collider2D[] colliders;
    PhysicsMaterial2D[] restColliderMaterials;
    float restLinearDamping;
    PhysicsMaterial2D restBodyMaterial;
    PhysicsMaterial2D blastSlideMaterial;
    bool blastSliding;
    float blastSlideDir = 1f;
    float blastSlideOriginX;
    float blastSlideUntil;
    float nextLightPushTime;

    public float KnockbackResistance => Mathf.Max(1f, knockbackResistance);

    public Vector2 PlatformVelocity => rb != null ? rb.linearVelocity : Vector2.zero;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        rb.constraints = RigidbodyConstraints2D.FreezeRotation;
        rb.sleepMode = RigidbodySleepMode2D.NeverSleep;
        if (rb.bodyType == RigidbodyType2D.Kinematic)
            rb.bodyType = RigidbodyType2D.Dynamic;

        restLinearDamping = rb.linearDamping;
        restBodyMaterial = rb.sharedMaterial;
        colliders = GetComponentsInChildren<Collider2D>(true);
        restColliderMaterials = new PhysicsMaterial2D[colliders.Length];
        for (int i = 0; i < colliders.Length; i++)
            restColliderMaterials[i] = colliders[i] != null ? colliders[i].sharedMaterial : null;

        blastSlideMaterial = new PhysicsMaterial2D("PushablePropBlastSlide")
        {
            friction = 0f,
            bounciness = 0f
        };
    }

    void OnDisable()
    {
        EndBlastSlide();
    }

    void FixedUpdate()
    {
        if (!blastSliding || rb == null)
            return;

        rb.linearVelocity = new Vector2(blastSlideDir * blastSlideSpeed, rb.linearVelocity.y);

        float traveled = Mathf.Abs(rb.position.x - blastSlideOriginX);
        if (traveled >= blastSlideDistance || Time.time >= blastSlideUntil)
            EndBlastSlide();
    }

    public void ApplyKnockback(Attack attacker)
    {
        if (rb == null)
            return;

        Vector2 dir = Attack.ResolveKnockbackDir(attacker, transform.position);

        if (attacker != null && Attack.HasBlastTag(attacker.transform))
        {
            BeginBlastSlide(dir.x);
            return;
        }

        if (Time.time < nextLightPushTime)
            return;

        float force = Attack.EffectivePropKnockbackForce(attacker, KnockbackResistance);
        if (force <= 0f)
            return;

        rb.WakeUp();
        rb.AddForce(dir * force, ForceMode2D.Impulse);
        nextLightPushTime = Time.time + Mathf.Max(0f, lightPushCooldown);
    }

    void BeginBlastSlide(float dirX)
    {
        blastSlideDir = Mathf.Abs(dirX) > 0.01f ? Mathf.Sign(dirX) : 1f;
        blastSlideOriginX = rb.position.x;
        float duration = blastSlideDistance / Mathf.Max(0.01f, blastSlideSpeed);
        blastSlideUntil = Time.time + duration + 0.15f;
        blastSliding = true;

        rb.WakeUp();
        rb.linearDamping = 0f;
        rb.sharedMaterial = blastSlideMaterial;
        for (int i = 0; i < colliders.Length; i++)
        {
            if (colliders[i] != null)
                colliders[i].sharedMaterial = blastSlideMaterial;
        }

        rb.linearVelocity = new Vector2(blastSlideDir * blastSlideSpeed, rb.linearVelocity.y);
    }

    void EndBlastSlide()
    {
        if (!blastSliding)
            return;

        blastSliding = false;
        if (rb == null)
            return;

        rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
        rb.linearDamping = restLinearDamping;
        rb.sharedMaterial = restBodyMaterial;
        for (int i = 0; i < colliders.Length; i++)
        {
            if (colliders[i] != null)
                colliders[i].sharedMaterial = restColliderMaterials[i];
        }
    }
}
