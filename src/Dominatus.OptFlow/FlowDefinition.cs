using System.Collections.ObjectModel;
using Dominatus.Core;
using Dominatus.Core.Hfsm;
using Dominatus.Core.Nodes;

namespace Dominatus.OptFlow;

/// <summary>The explicitly authored kind of a flow state. No value represents a generated state.</summary>
public enum FlowStateKind
{
    Authored,
    Steady
}

/// <summary>Immutable authored state data. It has no runtime behaviour beyond its supplied node.</summary>
public sealed record FlowState
{
    public required StateId Id { get; init; }
    public required AiNode Node { get; init; }
    public FlowStateKind Kind { get; init; } = FlowStateKind.Authored;
}

/// <summary>Immutable snapshot of the HFSM options supported by a flow definition.</summary>
public sealed record FlowRuntimeOptions(bool KeepRootFrame, float InterruptScanIntervalSeconds, float TransitionScanIntervalSeconds)
{
    public static FlowRuntimeOptions From(HfsmOptions? options) => new(
        options?.KeepRootFrame ?? false,
        options?.InterruptScanIntervalSeconds ?? 0f,
        options?.TransitionScanIntervalSeconds ?? 0f);

    public HfsmOptions CreateOptions() => new()
    {
        KeepRootFrame = KeepRootFrame,
        InterruptScanIntervalSeconds = InterruptScanIntervalSeconds,
        TransitionScanIntervalSeconds = TransitionScanIntervalSeconds
    };
}

public enum FlowValidationCode
{
    BlankDefinitionId,
    NullStatesCollection,
    EmptyStatesCollection,
    NullState,
    BlankStateId,
    NullNode,
    DuplicateStateId,
    MissingRoot,
    RootObjectIdInconsistency,
    InvalidInterruptScanInterval,
    InvalidTransitionScanInterval,
    InvalidStateKind
}

public sealed record FlowValidationDiagnostic(
    FlowValidationCode Code,
    string Message,
    int? AuthoredIndex = null,
    StateId? RelatedState = null);

public sealed class FlowValidationReport
{
    private readonly ReadOnlyCollection<FlowValidationDiagnostic> _diagnostics;
    public FlowValidationReport(IEnumerable<FlowValidationDiagnostic> diagnostics) =>
        _diagnostics = Array.AsReadOnly(diagnostics.ToArray());
    public IReadOnlyList<FlowValidationDiagnostic> Diagnostics => _diagnostics;
    public bool IsValid => _diagnostics.Count == 0;
}

public sealed class FlowDefinitionValidationException : ArgumentException
{
    public FlowDefinitionValidationException(FlowValidationReport report)
        : base("The flow definition is invalid.") => Report = report;
    public FlowValidationReport Report { get; }
}

public sealed record FlowStateInspection(StateId Id, int AuthoredIndex, FlowStateKind Kind, bool IsRoot);
public sealed record FlowGeneratedArtifactInspection(string Kind, string Description);

public sealed record FlowInspection(
    string Id,
    StateId Root,
    FlowRuntimeOptions Options,
    IReadOnlyList<FlowStateInspection> States,
    IReadOnlyList<FlowGeneratedArtifactInspection> GeneratedArtifacts,
    IReadOnlyList<FlowValidationDiagnostic> Diagnostics);

/// <summary>
/// An immutable, inspectable recipe for building a normal Dominatus HFSM graph.
/// Each build creates a new graph and one state definition for every authored state.
/// </summary>
public sealed class FlowDefinition
{
    private readonly ReadOnlyCollection<FlowState> _states;
    private readonly FlowInspection _inspection;

    internal FlowDefinition(string id, FlowState root, IEnumerable<FlowState> states, FlowRuntimeOptions options)
    {
        Id = id;
        Root = root;
        Options = options;
        _states = Array.AsReadOnly(states.ToArray());
        _inspection = new FlowInspection(
            Id, Root.Id, Options,
            Array.AsReadOnly(_states.Select((state, index) => new FlowStateInspection(state.Id, index, state.Kind, state.Id.Equals(Root.Id))).ToArray()),
            Array.AsReadOnly(Array.Empty<FlowGeneratedArtifactInspection>()),
            Array.AsReadOnly(Array.Empty<FlowValidationDiagnostic>()));
    }

    public string Id { get; }
    public FlowState Root { get; }
    public IReadOnlyList<FlowState> States => _states;
    public FlowRuntimeOptions Options { get; }
    public FlowInspection Inspect() => _inspection;

    public HfsmGraph BuildGraph()
    {
        var graph = new HfsmGraph { Root = Root.Id };
        foreach (var state in _states)
            graph.Add(new HfsmStateDef { Id = state.Id, Node = state.Node });
        return graph;
    }

    public HfsmInstance CreateBrain() => new(BuildGraph(), Options.CreateOptions());
}

/// <summary>Concise construction and validation helpers for explicit OptFlow definitions.</summary>
public static class Flow
{
    public static FlowState State(string id, AiNode node) => new() { Id = StateId.Of(id), Node = node };
    public static FlowState State(StateId id, AiNode node) => new() { Id = id, Node = node };

    public static FlowState Steady(string id, string? reason = null) =>
        new() { Id = StateId.Of(id), Node = _ => SteadyNode(reason), Kind = FlowStateKind.Steady };

    public static FlowDefinition Define(string id, FlowState root, IEnumerable<FlowState?>? states, HfsmOptions? options = null)
    {
        var report = Validate(id, root, states, options);
        if (!report.IsValid) throw new FlowDefinitionValidationException(report);
        return new FlowDefinition(id, root, states!.Cast<FlowState>(), FlowRuntimeOptions.From(options));
    }

    public static FlowValidationReport Validate(string? id, FlowState? root, IEnumerable<FlowState?>? states, HfsmOptions? options = null)
    {
        var diagnostics = new List<FlowValidationDiagnostic>();
        if (string.IsNullOrWhiteSpace(id)) diagnostics.Add(new(FlowValidationCode.BlankDefinitionId, "Definition id must be non-empty."));
        var snapshot = states?.ToArray();
        if (snapshot is null) diagnostics.Add(new(FlowValidationCode.NullStatesCollection, "States collection must not be null."));
        else if (snapshot.Length == 0) diagnostics.Add(new(FlowValidationCode.EmptyStatesCollection, "States collection must not be empty."));

        var seen = new HashSet<StateId>();
        if (snapshot is not null)
            for (var i = 0; i < snapshot.Length; i++)
            {
                var state = snapshot[i];
                if (state is null) { diagnostics.Add(new(FlowValidationCode.NullState, "State entry must not be null.", i)); continue; }
                if (string.IsNullOrWhiteSpace(state.Id.Value)) diagnostics.Add(new(FlowValidationCode.BlankStateId, "State id must be non-empty.", i));
                if (state.Node is null) diagnostics.Add(new(FlowValidationCode.NullNode, "State node must not be null.", i, state.Id));
                if (!Enum.IsDefined(state.Kind)) diagnostics.Add(new(FlowValidationCode.InvalidStateKind, "State kind is invalid.", i, state.Id));
                if (!seen.Add(state.Id)) diagnostics.Add(new(FlowValidationCode.DuplicateStateId, "State id is duplicated.", i, state.Id));
            }

        if (root is null) diagnostics.Add(new(FlowValidationCode.MissingRoot, "Root state must be supplied."));
        else if (snapshot is null || !snapshot.Any(s => s is not null && s.Id.Equals(root.Id))) diagnostics.Add(new(FlowValidationCode.MissingRoot, "Root state must appear in states.", RelatedState: root.Id));
        else if (!snapshot.Any(s => ReferenceEquals(s, root))) diagnostics.Add(new(FlowValidationCode.RootObjectIdInconsistency, "Root must be the same authored state object contained in states.", RelatedState: root.Id));

        var runtimeOptions = FlowRuntimeOptions.From(options);
        if (!IsValidInterval(runtimeOptions.InterruptScanIntervalSeconds)) diagnostics.Add(new(FlowValidationCode.InvalidInterruptScanInterval, "Interrupt scan interval must be finite and non-negative."));
        if (!IsValidInterval(runtimeOptions.TransitionScanIntervalSeconds)) diagnostics.Add(new(FlowValidationCode.InvalidTransitionScanInterval, "Transition scan interval must be finite and non-negative."));
        return new FlowValidationReport(diagnostics);
    }

    private static bool IsValidInterval(float value) => float.IsFinite(value) && value >= 0f;
    private static IEnumerator<AiStep> SteadyNode(string? reason) { while (true) yield return Ai.Steady(reason); }
}
