using UnityEngine;

/// <summary>Displays mechanism state without changing hit handling, timers or linked targets.</summary>
[ExecuteAlways, DisallowMultipleComponent]
public sealed class MechanismVisual : MonoBehaviour
{
    [SerializeField] SpriteRenderer artwork;
    [Tooltip("Charge: dormant/full/half/low. Core: intact/cracked/critical/broken. Switch: off/on.")]
    [SerializeField] Sprite[] stateSprites;

    EnergyNode energy;
    TimedChargeNode timed;
    ToggleSwitch toggle;
    BreakableProp core;

    public SpriteRenderer Artwork => artwork;
    public Sprite[] StateSprites => stateSprites;

    void OnEnable() => RefreshVisual();
    void OnValidate() => RefreshVisual();
    void LateUpdate() => RefreshVisual();

    public void Configure(SpriteRenderer renderer, Sprite[] sprites)
    {
        artwork = renderer;
        stateSprites = sprites;
        RefreshVisual();
    }

    public void RefreshVisual()
    {
        if (artwork == null || stateSprites == null || stateSprites.Length == 0)
            return;

        if (energy == null && timed == null && toggle == null && core == null)
        {
            energy = GetComponent<EnergyNode>();
            timed = GetComponent<TimedChargeNode>();
            toggle = GetComponent<ToggleSwitch>();
            core = GetComponent<BreakableProp>();
        }

        int state = 0;
        if (energy != null)
            state = ChargeState(energy.IsCharged, energy.IsHeld ? 1f : energy.ChargeNormalized);
        else if (timed != null)
            state = ChargeState(timed.IsActive, timed.RemainingNormalized);
        else if (toggle != null)
            state = toggle.IsOn ? 1 : 0;
        else if (core != null)
        {
            if (core.IsBroken)
                state = 3;
            else if (core.CurrentHits > 0)
                state = core.CurrentHits * 2 >= core.HitsToBreak ? 2 : 1;
        }

        Sprite next = stateSprites[Mathf.Clamp(state, 0, stateSprites.Length - 1)];
        if (next != null && artwork.sprite != next)
            artwork.sprite = next;
        // Color belongs to the existing damage-flash code; do not reset it here.
    }

    static int ChargeState(bool active, float remaining)
    {
        if (!active) return 0;
        if (remaining <= 0.25f) return 3;
        return remaining <= 0.5f ? 2 : 1;
    }
}
