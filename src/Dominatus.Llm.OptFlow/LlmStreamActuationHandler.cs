using Dominatus.Core.Runtime;

namespace Dominatus.Llm.OptFlow;

public sealed class LlmStreamActuationHandler : IActuationHandler<LlmStreamCommand>
{
    private readonly ILlmStreamingClient _client;
    private readonly LlmStreamRecorder _recorder;

    public LlmStreamActuationHandler(ILlmStreamingClient client, LlmStreamRecorder recorder)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _recorder = recorder ?? throw new ArgumentNullException(nameof(recorder));
    }

    public ActuatorHost.HandlerResult Handle(ActuatorHost host, AiCtx ctx, ActuationId id, LlmStreamCommand cmd)
    {
        var snapshot = _recorder.RunAsync(
            cmd.StreamId,
            cmd.Request,
            _client,
            chunk => ctx.Agent.Events.Publish(new LlmStreamChunkAvailable(chunk.StreamId, chunk.Index, chunk.Text, chunk.IsFinal, chunk.FinishReason)),
            ctx.Cancel).GetAwaiter().GetResult();

        return ActuatorHost.HandlerResult.CompletedWithPayload(snapshot);
    }
}
