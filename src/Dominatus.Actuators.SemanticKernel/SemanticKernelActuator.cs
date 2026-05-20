using System.Text;
using System.Text.Json;
using Dominatus.Core.Runtime;
using Microsoft.SemanticKernel;

namespace Dominatus.Actuators.SemanticKernel;

public sealed record AllowedSemanticKernelFunction(string PluginName, string FunctionName);

public sealed record SemanticKernelActuatorOptions
{
    public IReadOnlyList<AllowedSemanticKernelFunction> AllowedFunctions { get; init; } = [];
    public int MaxArgumentsBytes { get; init; } = 100_000;
    public int MaxResultBytes { get; init; } = 1_000_000;
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);
}

public sealed record SemanticKernelFunctionCommand(
    string PluginName,
    string FunctionName,
    string ArgumentsJson = "{}") : IActuationCommand;

public sealed record SemanticKernelFunctionResult(string PluginName, string FunctionName, string ResultText);

internal interface ISemanticKernelFunctionInvoker
{
    Task<string> InvokeAsync(string pluginName, string functionName, IReadOnlyDictionary<string, object?> arguments, CancellationToken cancellationToken);
}

internal sealed class KernelFunctionInvoker(Kernel kernel) : ISemanticKernelFunctionInvoker
{
    public async Task<string> InvokeAsync(string pluginName, string functionName, IReadOnlyDictionary<string, object?> arguments, CancellationToken cancellationToken)
    {
        var skArgs = new KernelArguments();
        foreach (var pair in arguments)
            skArgs[pair.Key] = pair.Value;

        var result = await kernel.InvokeAsync(pluginName, functionName, skArgs, cancellationToken).ConfigureAwait(false);
        return result.ToString() ?? string.Empty;
    }
}

internal sealed class SemanticKernelRequestResolver
{
    public SemanticKernelActuatorOptions Options { get; }
    private readonly HashSet<(string Plugin, string Function)> _allowed;
    private static readonly UTF8Encoding Utf8 = new(false);

    public SemanticKernelRequestResolver(SemanticKernelActuatorOptions options)
    {
        Options = options ?? throw new ArgumentNullException(nameof(options));
        if (options.AllowedFunctions is null || options.AllowedFunctions.Count == 0)
            throw new ArgumentException("At least one allowed function is required.", nameof(options));
        if (options.MaxArgumentsBytes <= 0)
            throw new ArgumentException("MaxArgumentsBytes must be > 0.", nameof(options));
        if (options.MaxResultBytes <= 0)
            throw new ArgumentException("MaxResultBytes must be > 0.", nameof(options));
        if (options.Timeout <= TimeSpan.Zero)
            throw new ArgumentException("Timeout must be > 0.", nameof(options));

        _allowed = new HashSet<(string, string)>();
        foreach (var fn in options.AllowedFunctions)
        {
            if (string.IsNullOrWhiteSpace(fn.PluginName)) throw new ArgumentException("Allowed function PluginName is required.", nameof(options));
            if (string.IsNullOrWhiteSpace(fn.FunctionName)) throw new ArgumentException("Allowed function FunctionName is required.", nameof(options));
            var key = (fn.PluginName.Trim().ToUpperInvariant(), fn.FunctionName.Trim().ToUpperInvariant());
            if (!_allowed.Add(key)) throw new ArgumentException("Duplicate allowed plugin/function pair.", nameof(options));
        }
    }

    public IReadOnlyDictionary<string, object?> Resolve(SemanticKernelFunctionCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.PluginName)) throw new InvalidOperationException("PluginName is required.");
        if (string.IsNullOrWhiteSpace(command.FunctionName)) throw new InvalidOperationException("FunctionName is required.");
        if (command.ArgumentsJson is null) throw new InvalidOperationException("ArgumentsJson is required.");
        if (Utf8.GetByteCount(command.ArgumentsJson) > Options.MaxArgumentsBytes)
            throw new InvalidOperationException($"ArgumentsJson exceeds MaxArgumentsBytes ({Options.MaxArgumentsBytes}).");

        var key = (command.PluginName.Trim().ToUpperInvariant(), command.FunctionName.Trim().ToUpperInvariant());
        if (!_allowed.Contains(key)) throw new InvalidOperationException("Semantic Kernel function is not allowlisted.");

        using var doc = JsonDocument.Parse(command.ArgumentsJson);
        if (doc.RootElement.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("ArgumentsJson must be a JSON object.");

        var map = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            map[prop.Name] = prop.Value.ValueKind switch
            {
                JsonValueKind.String => prop.Value.GetString(),
                JsonValueKind.Number when prop.Value.TryGetInt64(out var l) => l,
                JsonValueKind.Number => prop.Value.GetDouble(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Null => null,
                _ => throw new InvalidOperationException($"Argument '{prop.Name}' has unsupported nested JSON value type '{prop.Value.ValueKind}'.")
            };
        }

        return map;
    }
}

public sealed class SemanticKernelActuationHandler : IActuationHandler<SemanticKernelFunctionCommand>
{
    private readonly SemanticKernelRequestResolver _resolver;
    private readonly ISemanticKernelFunctionInvoker _invoker;
    private static readonly UTF8Encoding Utf8 = new(false);

    public SemanticKernelActuationHandler(Kernel kernel, SemanticKernelActuatorOptions options)
        : this(new KernelFunctionInvoker(kernel ?? throw new ArgumentNullException(nameof(kernel))), options)
    {
    }

    internal SemanticKernelActuationHandler(ISemanticKernelFunctionInvoker invoker, SemanticKernelActuatorOptions options)
    {
        _invoker = invoker ?? throw new ArgumentNullException(nameof(invoker));
        _resolver = new SemanticKernelRequestResolver(options);
    }

    public ActuatorHost.HandlerResult Handle(ActuatorHost host, AiCtx ctx, ActuationId id, SemanticKernelFunctionCommand cmd)
    {
        try
        {
            var args = _resolver.Resolve(cmd);
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ctx.Cancel);
            cts.CancelAfter(_resolver.Options.Timeout);

            var text = _invoker.InvokeAsync(cmd.PluginName, cmd.FunctionName, args, cts.Token).GetAwaiter().GetResult() ?? string.Empty;
            if (Utf8.GetByteCount(text) > _resolver.Options.MaxResultBytes)
                return Fail($"Semantic Kernel function result exceeds MaxResultBytes ({_resolver.Options.MaxResultBytes}).");

            return ActuatorHost.HandlerResult.CompletedWithPayload(new SemanticKernelFunctionResult(cmd.PluginName, cmd.FunctionName, text));
        }
        catch (Exception ex) when (ex is InvalidOperationException or JsonException or OperationCanceledException)
        {
            return Fail(Sanitize(ex));
        }
    }

    private static ActuatorHost.HandlerResult Fail(string message) => new(true, true, false, message);

    private static string Sanitize(Exception ex)
    {
        if (ex is OperationCanceledException)
            return "Semantic Kernel function invocation timed out or was canceled.";
        if (ex.Message.Contains("key", StringComparison.OrdinalIgnoreCase) || ex.Message.Contains("secret", StringComparison.OrdinalIgnoreCase))
            return "Semantic Kernel function invocation failed.";
        return ex.Message;
    }
}

public static class SemanticKernelActuatorRegistration
{
    public static ActuatorHost RegisterSemanticKernelActuators(this ActuatorHost host, Kernel kernel, SemanticKernelActuatorOptions options)
    {
        if (host is null) throw new ArgumentNullException(nameof(host));
        var handler = new SemanticKernelActuationHandler(kernel, options);
        host.Register<SemanticKernelFunctionCommand>(handler);
        return host;
    }
}
