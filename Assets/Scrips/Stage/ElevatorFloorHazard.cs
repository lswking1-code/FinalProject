using System.Collections;
using UnityEngine;

/// <summary>
/// 电梯二阶段底板：间歇随机点亮若干通电平台。
/// </summary>
public class ElevatorFloorHazard : MonoBehaviour
{
    [SerializeField] ElectrifiedPlatform[] segments;
    [SerializeField, Min(0.2f)] float offMin = 1.2f;
    [SerializeField, Min(0.2f)] float offMax = 2.8f;
    [SerializeField, Min(0.2f)] float onMin = 0.7f;
    [SerializeField, Min(0.2f)] float onMax = 1.6f;

    Coroutine cycleRoutine;
    bool phase2;

    void Awake()
    {
        SetAllPowered(false);
    }

    void OnDisable()
    {
        StopCycle();
        SetAllPowered(false);
    }

    public void SetPhase2Active(bool active)
    {
        if (phase2 == active)
            return;

        phase2 = active;
        if (!phase2)
        {
            StopCycle();
            SetAllPowered(false);
            return;
        }

        if (isActiveAndEnabled)
            cycleRoutine = StartCoroutine(CycleRoutine());
    }

    IEnumerator CycleRoutine()
    {
        while (phase2)
        {
            yield return new WaitForSeconds(Random.Range(offMin, offMax));
            if (!phase2)
                yield break;

            ElectrifiedPlatform pick = PickSegment();
            if (pick != null)
                pick.SetPowered(true);

            yield return new WaitForSeconds(Random.Range(onMin, onMax));
            SetAllPowered(false);
        }
    }

    ElectrifiedPlatform PickSegment()
    {
        if (segments == null || segments.Length == 0)
            return null;

        int live = 0;
        for (int i = 0; i < segments.Length; i++)
        {
            if (segments[i] != null)
                live++;
        }

        if (live <= 0)
            return null;

        int chosen = Random.Range(0, live);
        for (int i = 0; i < segments.Length; i++)
        {
            if (segments[i] == null)
                continue;
            if (chosen == 0)
                return segments[i];
            chosen--;
        }

        return null;
    }

    void StopCycle()
    {
        if (cycleRoutine != null)
        {
            StopCoroutine(cycleRoutine);
            cycleRoutine = null;
        }
    }

    void SetAllPowered(bool on)
    {
        if (segments == null)
            return;

        for (int i = 0; i < segments.Length; i++)
        {
            if (segments[i] != null)
                segments[i].SetPowered(on);
        }
    }
}
