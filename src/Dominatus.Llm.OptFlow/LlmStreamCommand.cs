using Dominatus.Core.Runtime;

namespace Dominatus.Llm.OptFlow;

public sealed record LlmStreamCommand : IActuationCommand
{
    public string StreamId { get; init; }
    public LlmTextRequest Request { get; init; }

    public LlmStreamCommand(string streamId, LlmTextRequest request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(streamId);
        ArgumentNullException.ThrowIfNull(request);

        StreamId = streamId;
        Request = request;
    }
}
