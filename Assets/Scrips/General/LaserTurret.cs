using System.Collections;
using UnityEngine;

/// <summary>
/// 镭射炮塔：激活时按间隔朝面向方向发射敌人子弹。
/// 接线方式：1) Inspector 拖入开关/压力板；2) 开关 onToggled → SetFromSwitch / SetActive。
/// </summary>
public class LaserTurret : MonoBehaviour
{
    [Header("激活状态")]
    [SerializeField] bool isActive = true;
    [Tooltip("开关 ON 时炮塔是否激活；默认 false 表示开关 ON 时关闭炮塔")]
    [SerializeField] bool activeWhenSwitchOn;

    [Header("开关（可选）")]
    [Tooltip("有引用时订阅 onToggled；数组为空则保持默认激活状态")]
    [SerializeField] ToggleSwitch[] activationSwitches;
    [SerializeField] PressurePlate[] activationPlates;
    [SerializeField] bool listenToSwitch = true;

    [Header("开火")]
    [SerializeField, Min(0.05f)] float fireInterval = 0.35f;
    [SerializeField] EnemyProjectile projectilePrefab;
    [Tooltip("为空则在自身位置发射；朝向取 firePoint（或自身）的 up（炮口朝向）")]
    [SerializeField] Transform firePoint;
    [Tooltip("子弹精灵默认朝向相对 +X 的角度偏移；贴图朝下时为 90")]
    [SerializeField] float projectileSpriteAngleOffset = 90f;

    [Header("视觉")]
    [SerializeField] Animator turretAnimator;
    [SerializeField] string idleStateName = "Idle";
    [SerializeField] string activateStateName = "Activate";
    [SerializeField] string inactivateStateName = "Inactivate";
    [SerializeField] string inactivateIdleStateName = "Inactivate_Idle";

    float fireCooldown;
    Coroutine visualRoutine;
    bool transitionSeen;

    public bool IsActive => isActive;
    public bool IsOn => isActive;

    void Awake()
    {
        EnsureAnimator();
        PlaySteadyState(isActive);
    }

    void OnEnable()
    {
        if (!listenToSwitch)
            return;

        SubscribeInputs();
    }

    void Start()
    {
        if (listenToSwitch && HasAnyInput())
            SetFromSwitch(EvaluateInputs());
    }

    void OnDisable()
    {
        UnsubscribeInputs();
        StopVisualRoutine();
    }

    void Update()
    {
        if (!isActive || projectilePrefab == null)
            return;

        fireCooldown -= Time.deltaTime;
        if (fireCooldown > 0f)
            return;

        fireCooldown = fireInterval;
        Fire();
    }

    public void SetActive(bool active)
    {
        if (isActive == active)
            return;

        isActive = active;
        if (!isActive)
            fireCooldown = 0f;

        PlayPoweredVisual(isActive, instant: false);
    }

    public void SetPowered(bool powered) => SetActive(powered);

    /// <summary>由拉杆 / 压力板 / UnityEvent 调用：开关状态映射到炮塔激活。</summary>
    public void SetFromSwitch(bool switchOn)
    {
        SetActive(activeWhenSwitchOn ? switchOn : !switchOn);
    }

    void Fire()
    {
        Transform origin = firePoint != null ? firePoint : transform;
        Vector2 direction = GetFireDirection(origin);
        if (direction.sqrMagnitude < 0.0001f)
            direction = Vector2.up;

        float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg + projectileSpriteAngleOffset;
        var projectile = Instantiate(projectilePrefab, origin.position, Quaternion.Euler(0f, 0f, angle));
        EnemySceneCleanup.PlaceInSourceScene(projectile.gameObject, this);
        projectile.Init(direction);
    }

    Vector2 GetFireDirection(Transform origin)
    {
        // 炮口沿 transform.up；场景旋转炮塔时方向随之改变
        Vector2 direction = origin.up;
        if (direction.sqrMagnitude < 0.0001f)
            return Vector2.up;
        return direction.normalized;
    }

    void OnInputChanged(bool _)
    {
        SetFromSwitch(EvaluateInputs());
    }

    bool HasAnyInput()
    {
        if (activationSwitches != null)
        {
            for (int i = 0; i < activationSwitches.Length; i++)
            {
                if (activationSwitches[i] != null)
                    return true;
            }
        }

        if (activationPlates != null)
        {
            for (int i = 0; i < activationPlates.Length; i++)
            {
                if (activationPlates[i] != null)
                    return true;
            }
        }

        return false;
    }

    bool EvaluateInputs()
    {
        bool anyOn = false;

        if (activationSwitches != null)
        {
            for (int i = 0; i < activationSwitches.Length; i++)
            {
                ToggleSwitch sw = activationSwitches[i];
                if (sw != null && sw.IsOn)
                    anyOn = true;
            }
        }

        if (activationPlates != null)
        {
            for (int i = 0; i < activationPlates.Length; i++)
            {
                PressurePlate plate = activationPlates[i];
                if (plate != null && plate.IsOn)
                    anyOn = true;
            }
        }

        return anyOn;
    }

    void SubscribeInputs()
    {
        if (activationSwitches != null)
        {
            for (int i = 0; i < activationSwitches.Length; i++)
            {
                if (activationSwitches[i] != null)
                    activationSwitches[i].onToggled.AddListener(OnInputChanged);
            }
        }

        if (activationPlates != null)
        {
            for (int i = 0; i < activationPlates.Length; i++)
            {
                if (activationPlates[i] != null)
                    activationPlates[i].onToggled.AddListener(OnInputChanged);
            }
        }
    }

    void UnsubscribeInputs()
    {
        if (activationSwitches != null)
        {
            for (int i = 0; i < activationSwitches.Length; i++)
            {
                if (activationSwitches[i] != null)
                    activationSwitches[i].onToggled.RemoveListener(OnInputChanged);
            }
        }

        if (activationPlates != null)
        {
            for (int i = 0; i < activationPlates.Length; i++)
            {
                if (activationPlates[i] != null)
                    activationPlates[i].onToggled.RemoveListener(OnInputChanged);
            }
        }
    }

    void PlayPoweredVisual(bool on, bool instant)
    {
        StopVisualRoutine();
        if (instant || !Application.isPlaying)
        {
            PlaySteadyState(on);
            return;
        }

        visualRoutine = StartCoroutine(PlayTransitionThenIdle(on));
    }

    void PlaySteadyState(bool on)
    {
        PlayState(on ? idleStateName : inactivateIdleStateName);
    }

    IEnumerator PlayTransitionThenIdle(bool on)
    {
        string transition = on ? activateStateName : inactivateStateName;
        string idle = on ? idleStateName : inactivateIdleStateName;
        transitionSeen = false;
        PlayState(transition);

        float waited = 0f;
        const float enterTimeout = 0.25f;
        while (!IsInState(transition) && waited < enterTimeout)
        {
            waited += Time.deltaTime;
            yield return null;
        }

        while (!HasFinishedState(transition))
            yield return null;

        PlayState(idle);
        visualRoutine = null;
    }

    void PlayState(string stateName)
    {
        EnsureAnimator();
        if (turretAnimator == null
            || string.IsNullOrEmpty(stateName)
            || turretAnimator.runtimeAnimatorController == null
            || !turretAnimator.isActiveAndEnabled)
            return;

        turretAnimator.Play(stateName, 0, 0f);
        turretAnimator.Update(0f);
    }

    bool IsInState(string stateName)
    {
        if (turretAnimator == null)
            return false;

        var info = turretAnimator.GetCurrentAnimatorStateInfo(0);
        return info.IsName(stateName) || info.IsName("Base Layer." + stateName);
    }

    bool HasFinishedState(string stateName)
    {
        if (turretAnimator == null || !turretAnimator.isActiveAndEnabled)
            return true;

        if (!IsInState(stateName))
            return transitionSeen;

        transitionSeen = true;
        var info = turretAnimator.GetCurrentAnimatorStateInfo(0);
        if (info.length <= 0f)
            return true;

        return info.normalizedTime >= 1f && !turretAnimator.IsInTransition(0);
    }

    void EnsureAnimator()
    {
        if (turretAnimator == null)
            turretAnimator = GetComponent<Animator>();
    }

    void StopVisualRoutine()
    {
        if (visualRoutine == null)
            return;

        StopCoroutine(visualRoutine);
        visualRoutine = null;
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        EnsureAnimator();
        // 不在 OnValidate 里 Play：Prefab 导入时物体可能未激活，会刷 “animator is inactive”
    }

    void OnDrawGizmosSelected()
    {
        Transform origin = firePoint != null ? firePoint : transform;
        Vector2 direction = GetFireDirection(origin);

        Gizmos.color = isActive ? Color.red : Color.gray;
        Vector3 start = origin.position;
        Gizmos.DrawLine(start, start + (Vector3)(direction * 2f));
        Gizmos.DrawWireSphere(start, 0.08f);
    }
#endif
}
