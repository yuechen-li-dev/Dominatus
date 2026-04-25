using Dominatus.Core.Runtime;

namespace Dominatus.Llm.OptFlow;

public sealed class LlmTextActuationHandler : IActuationHandler<LlmTextRequest>
{
    private readonly ILlmClient _client;
    private readonly ILlmCassette _cassette;
    private readonly LlmCassetteMode _mode;

    public LlmTextActuationHandler(
        ILlmClient client,
        ILlmCassette cassette,
        LlmCassetteMode mode)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _cassette = cassette ?? throw new ArgumentNullException(nameof(cassette));
        _mode = mode;
    }

    public ActuatorHost.HandlerResult Handle(ActuatorHost host, AiCtx ctx, ActuationId id, LlmTextRequest cmd)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(cmd);

        string requestHash = LlmRequestHasher.ComputeHash(cmd);

        try
        {
            var result = Resolve(cmd, requestHash, ctx.Cancel);

            return new ActuatorHost.HandlerResult(
                Accepted: true,
                Completed: true,
                Ok: true,
                Payload: result.Text,
                PayloadType: typeof(string));
        }
        catch (Exception ex)
        {
            var error = BuildFailureMessage(_mode, cmd.StableId, requestHash, ex.Message);
            return new ActuatorHost.HandlerResult(
                Accepted: true,
                Completed: true,
                Ok: false,
                Error: error);
        }
    }

    private LlmTextResult Resolve(LlmTextRequest request, string requestHash, CancellationToken cancellationToken)
    {
        if (_mode is LlmCassetteMode.Replay or LlmCassetteMode.Strict)
        {
            if (!_cassette.TryGet(requestHash, out var replayResult))
            {
                throw new InvalidOperationException("Cassette entry not found.");
            }

            return replayResult;
        }

        if (_mode is LlmCassetteMode.Record && _cassette.TryGet(requestHash, out var recordedResult))
        {
            return recordedResult;
        }

        if (_mode is LlmCassetteMode.Live or LlmCassetteMode.Record)
        {
            var providerResult = _client
                .GenerateTextAsync(request, requestHash, cancellationToken)
                .GetAwaiter()
                .GetResult();

            if (providerResult is null)
            {
                throw new InvalidOperationException("LLM provider returned null result.");
            }

            if (!string.Equals(providerResult.RequestHash, requestHash, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("LLM provider returned a mismatched request hash.");
            }

            if (_mode is LlmCassetteMode.Record)
            {
                _cassette.Put(requestHash, request, providerResult);
            }

            return providerResult;
        }

        throw new InvalidOperationException($"Unsupported cassette mode '{_mode}'.");
    }

    private static string BuildFailureMessage(LlmCassetteMode mode, string stableId, string requestHash, string reason)
        => $"LlmText actuation failed (Mode={mode}, StableId={stableId}, RequestHash={requestHash}). {reason}";
}
