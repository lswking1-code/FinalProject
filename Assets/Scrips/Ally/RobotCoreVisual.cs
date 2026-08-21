using System;
using FMODUnity;
using UnityEngine;

/// <summary>
/// RobotCore 演出：召唤时播 Open 后自毁；收回时播 Close → Idle，再飞回玩家后销毁。
/// </summary>
[RequireComponent(typeof(Animator))]
public class RobotCoreVisual : MonoBehaviour
{
    const string OpenStateName = "RobotCore_Open";
    const string CloseStateName = "RobotCore_Close";
    const string IdleStateName = "RobotCore_Idle";

    [Header("音效")]
    [SerializeField] EventReference openEvent;
    [SerializeField] EventReference closeEvent;

    enum Phase
    {
        None,
        PlayingOpen,
        PlayingClose,
        Flying,
    }

    Animator anim;
    Phase phase;
    bool animSeen;
    Transform returnTarget;
    Vector3 returnOffset;
    float flySpeed;
    float arriveThreshold;
    Action onArrived;
    bool finishing;

    void Awake()
    {
        anim = GetComponent<Animator>();
    }

    public void PlayOpenThenDestroy()
    {
        phase = Phase.PlayingOpen;
        animSeen = false;
        PlayState(OpenStateName);
        FmodAudio.Play(openEvent);
    }

    public void PlayCloseThenReturn(
        Transform target,
        Vector3 targetOffset,
        float speed,
        float arriveThreshold,
        Action onArrived)
    {
        returnTarget = target;
        returnOffset = targetOffset;
        flySpeed = Mathf.Max(0f, speed);
        this.arriveThreshold = Mathf.Max(0.01f, arriveThreshold);
        this.onArrived = onArrived;
        phase = Phase.PlayingClose;
        animSeen = false;
        PlayState(CloseStateName);
        FmodAudio.Play(closeEvent);
    }

    public void CancelAndDestroy()
    {
        if (finishing)
            return;

        finishing = true;
        onArrived = null;
        Destroy(gameObject);
    }

    void Update()
    {
        switch (phase)
        {
            case Phase.PlayingOpen:
                if (HasFinishedState(OpenStateName))
                    Destroy(gameObject);
                break;
            case Phase.PlayingClose:
                if (HasFinishedState(CloseStateName))
                    BeginFly();
                break;
            case Phase.Flying:
                TickFly();
                break;
        }
    }

    void PlayState(string stateName)
    {
        if (anim == null)
            anim = GetComponent<Animator>();
        if (anim == null)
            return;

        anim.Play(stateName, 0, 0f);
    }

    bool HasFinishedState(string stateName)
    {
        if (anim == null || !anim.isActiveAndEnabled)
            return true;

        var info = anim.GetCurrentAnimatorStateInfo(0);
        if (info.IsName(stateName))
        {
            animSeen = true;
            return info.normalizedTime >= 1f;
        }

        return animSeen;
    }

    void BeginFly()
    {
        phase = Phase.Flying;
        animSeen = false;
        PlayState(IdleStateName);
        TickFly();
    }

    void TickFly()
    {
        if (returnTarget == null)
        {
            FinishReturn();
            return;
        }

        Vector3 target = returnTarget.position + returnOffset;
        float step = flySpeed * Time.deltaTime;
        transform.position = Vector3.MoveTowards(transform.position, target, step);

        if (Vector3.Distance(transform.position, target) <= arriveThreshold)
            FinishReturn();
    }

    void FinishReturn()
    {
        if (finishing)
            return;

        finishing = true;
        var callback = onArrived;
        onArrived = null;
        callback?.Invoke();
        Destroy(gameObject);
    }
}
