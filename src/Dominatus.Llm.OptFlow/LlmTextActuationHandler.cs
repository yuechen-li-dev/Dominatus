using Dominatus.Core.Runtime;

namespace Dominatus.Llm.OptFlow;

public sealed class LlmTextActuationHandler : AsyncActuationHandler<LlmTextRequest>
{
    private readonly ILlmClient _client;
    private readonly ILlmCassette _cassette;
    private readonly LlmCassetteMode _mode;
    private readonly object _cassetteGate = new();

    public LlmTextActuationHandler(
        ILlmClient client,
        ILlmCassette cassette,
        LlmCassetteMode mode)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _cassette = cassette ?? throw new ArgumentNullException(nameof(cassette));
        _mode = mode;
    }

    public override async ValueTask<ActuatorHost.HandlerResult> HandleAsync(AsyncActuationContext context, LlmTextRequest cmd, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(cmd);

        string requestHash = LlmRequestHasher.ComputeHash(cmd);

        try
        {
            var result = await ResolveAsync(cmd, requestHash, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            return ActuatorHost.HandlerResult.CompletedWithPayload(result.Text);
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

    private async ValueTask<LlmTextResult> ResolveAsync(LlmTextRequest request, string requestHash, CancellationToken cancellationToken)
    {
        if (_mode is LlmCassetteMode.Replay or LlmCassetteMode.Strict)
        {
            if (!TryGet(requestHash, out var replayResult))
            {
                throw new InvalidOperationException("Cassette entry not found.");
            }

            return replayResult;
        }

        if (_mode is LlmCassetteMode.Record && TryGet(requestHash, out var recordedResult))
        {
            return recordedResult;
        }

        if (_mode is LlmCassetteMode.Live or LlmCassetteMode.Record)
        {
            var providerResult = await _client
                .GenerateTextAsync(request, requestHash, cancellationToken)
                .ConfigureAwait(false);

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
                lock (_cassetteGate) _cassette.Put(requestHash, request, providerResult);
            }

            return providerResult;
        }

        throw new InvalidOperationException($"Unsupported cassette mode '{_mode}'.");
    }

    private bool TryGet(string hash, out LlmTextResult result)
    {
        lock (_cassetteGate) return _cassette.TryGet(hash, out result);
    }

    private static string BuildFailureMessage(LlmCassetteMode mode, string stableId, string requestHash, string reason)
        => $"LlmText actuation failed (Mode={mode}, StableId={stableId}, RequestHash={requestHash}). {reason}";
}
