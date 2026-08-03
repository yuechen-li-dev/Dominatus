namespace Dominatus.OptFlow;

/// <summary>Marks a declaration-only partial method whose ordinary OptFlow construction is generated at compile time.</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class DominatusFlowAttribute(string id) : Attribute
{
    public string Id { get; } = id;
    public bool KeepRootFrame { get; init; }
    public float InterruptScanIntervalSeconds { get; init; }
    public float TransitionScanIntervalSeconds { get; init; }
}

/// <summary>Marks an explicit durable state identity owned by a generated OptFlow factory.</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class DominatusStateAttribute(string id) : Attribute
{
    public string Id { get; } = id;
    public bool Root { get; init; }
}
