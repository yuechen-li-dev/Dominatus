using Dominatus.Core.Runtime;

namespace Dominatus.Llm.OptFlow;

public sealed class LlmStreamActuationHandler : AsyncActuationHandler<LlmStreamCommand>
{
    private readonly ILlmStreamingClient _client;
    private readonly LlmStreamRecorder _recorder;

    public LlmStreamActuationHandler(ILlmStreamingClient client, LlmStreamRecorder recorder)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _recorder = recorder ?? throw new ArgumentNullException(nameof(recorder));
    }

    public override async ValueTask<ActuatorHost.HandlerResult> HandleAsync(AsyncActuationContext context, LlmStreamCommand cmd, CancellationToken cancellationToken)
    {
        var snapshot = await _recorder.RunAsync(
            cmd.StreamId,
            cmd.Request,
            _client,
            chunk => context.Report(new LlmStreamChunkAvailable(chunk.StreamId, chunk.Index, chunk.Text, chunk.IsFinal, chunk.FinishReason)),
            cancellationToken).ConfigureAwait(false);

        return ActuatorHost.HandlerResult.CompletedWithPayload(snapshot, ok: snapshot.Status == LlmStreamStatus.Completed, error: snapshot.Error);
    }
}
