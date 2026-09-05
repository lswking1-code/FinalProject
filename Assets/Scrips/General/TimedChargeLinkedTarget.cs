using System;
using UnityEngine;

[Serializable]
public class TimedChargeLinkedTarget
{
    public enum TargetKind
    {
        ElectrifiedPlatform,
        ActuatedGate,
        ReciprocatingPlatform,
    }

    public enum ActivatedState
    {
        Off = 0,
        On = 1,
    }

    [SerializeField] TargetKind targetKind;
    [SerializeField] MonoBehaviour target;
    [SerializeField] ActivatedState activatedState = ActivatedState.On;

    bool initialStateCaptured;
    bool initialState;

    public bool IsValid => ResolveTarget() != null;

    public void CaptureInitialState()
    {
        var resolved = ResolveTarget();
        if (resolved == null)
            return;

        initialState = ReadState(resolved);
        initialStateCaptured = true;
    }

    public void ApplyActivatedState()
    {
        var resolved = ResolveTarget();
        if (resolved == null)
            return;

        WriteState(resolved, activatedState == ActivatedState.On);
    }

    public void RestoreInitialState()
    {
        var resolved = ResolveTarget();
        if (resolved == null)
            return;

        if (!initialStateCaptured)
            CaptureInitialState();

        WriteState(resolved, initialState);
    }

    object ResolveTarget()
    {
        if (target == null)
            return null;

        switch (targetKind)
        {
            case TargetKind.ElectrifiedPlatform:
                return target as ElectrifiedPlatform;
            case TargetKind.ActuatedGate:
                return target as ActuatedGate;
            case TargetKind.ReciprocatingPlatform:
                return target as ReciprocatingPlatform;
            default:
                return null;
        }
    }

    bool ReadState(object resolvedTarget)
    {
        switch (resolvedTarget)
        {
            case ElectrifiedPlatform platform:
                return platform.IsOn;
            case ActuatedGate gate:
                return gate.IsOpen;
            case ReciprocatingPlatform reciprocal:
                return reciprocal.IsRunning;
            default:
                return false;
        }
    }

    void WriteState(object resolvedTarget, bool state)
    {
        switch (resolvedTarget)
        {
            case ElectrifiedPlatform platform:
                platform.SetPowered(state);
                break;
            case ActuatedGate gate:
                gate.SetOpen(state);
                break;
            case ReciprocatingPlatform reciprocal:
                reciprocal.SetRunning(state);
                break;
        }
    }
}
