namespace Dominatus.Core.Hfsm;

public sealed class HfsmOptions
{
    /// <summary>
    /// If true, Root is never replaced by transitions/interrupts.
    /// Instead, Root remains on stack and the target state is pushed above it.
    /// </summary>
    public bool KeepRootFrame { get; init; } = false;
}
