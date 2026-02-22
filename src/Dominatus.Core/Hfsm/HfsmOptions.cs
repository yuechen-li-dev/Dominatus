namespace Dominatus.Core.Hfsm;

public sealed class HfsmOptions
{
    public bool KeepRootFrame { get; init; } = false;

    /// <summary>
    /// If <= 0, scan every tick (current behavior).
    /// Otherwise, scan interrupts at this interval.
    /// </summary>
    public float InterruptScanIntervalSeconds { get; init; } = 0f;

    /// <summary>
    /// If <= 0, scan every tick (current behavior).
    /// Otherwise, scan normal transitions at this interval.
    /// </summary>
    public float TransitionScanIntervalSeconds { get; init; } = 0f;
}
