using Dominatus.Core.Blackboard;
using Dominatus.Core.Nodes.Steps;
using Dominatus.Core.Runtime;

namespace Dominatus.Llm.OptFlow;

/// <summary>
/// Minimal OptFlow helpers for composing LLM-driven tool calls through Dominatus actuation.
/// </summary>
public static class llm
{
    /// <summary>
    /// Creates a tool invocation command envelope suitable for routing by a custom actuator handler.
    /// </summary>
    public static LlmToolCall Tool(string name, string? inputJson = null)
        => new(name, inputJson);

    /// <summary>
    /// Dispatches an arbitrary actuation command from an LLM planner step.
    /// </summary>
    public static Act Call(IActuationCommand command, BbKey<ActuationId>? storeIdAs = null)
        => new(command, storeIdAs);

    /// <summary>
    /// Convenience overload that dispatches a standard LLM tool envelope.
    /// </summary>
    public static Act Call(string toolName, string? inputJson = null, BbKey<ActuationId>? storeIdAs = null)
        => new(new LlmToolCall(toolName, inputJson), storeIdAs);

    /// <summary>
    /// Waits for completion of a previously dispatched actuation id.
    /// </summary>
    public static AwaitActuation Await(BbKey<ActuationId> idKey)
        => new(idKey);

    /// <summary>
    /// Waits for typed completion payload and optionally stores it into blackboard.
    /// </summary>
    public static AwaitActuation<T> Await<T>(BbKey<ActuationId> idKey, BbKey<T>? storePayloadAs = null)
        => new(idKey, storePayloadAs);

    /// <summary>
    /// Inference-friendly typed await overload.
    /// </summary>
    public static AwaitActuation<T> Await<T>(BbKey<ActuationId> idKey, BbKey<T> storePayloadAs)
        => new(idKey, storePayloadAs);
}

/// <summary>
/// Minimal tool-call envelope command for future LLM adapters.
/// </summary>
public sealed record LlmToolCall(string Name, string? InputJson = null) : IActuationCommand;
