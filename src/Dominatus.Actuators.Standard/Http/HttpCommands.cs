using Dominatus.Core.Runtime;

namespace Dominatus.Actuators.Standard;

public sealed record HttpGetTextCommand(
    string Endpoint,
    string Path,
    IReadOnlyDictionary<string, string>? Query = null,
    IReadOnlyDictionary<string, string>? Headers = null) : IActuationCommand;

public sealed record HttpPostJsonCommand(
    string Endpoint,
    string Path,
    string Json,
    IReadOnlyDictionary<string, string>? Query = null,
    IReadOnlyDictionary<string, string>? Headers = null) : IActuationCommand;

public sealed record HttpPostTextCommand(
    string Endpoint,
    string Path,
    string Text,
    string ContentType = "text/plain",
    IReadOnlyDictionary<string, string>? Query = null,
    IReadOnlyDictionary<string, string>? Headers = null) : IActuationCommand;
