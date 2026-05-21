using System.Text;

namespace Dominatus.Llm.OptFlow;

public sealed class LlmStreamRecorder
{
    public async Task<LlmStreamSnapshot> RunAsync(
        string streamId,
        LlmTextRequest request,
        ILlmStreamingClient client,
        Action<LlmStreamChunk>? onChunk,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(streamId);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(client);

        string requestHash = LlmRequestHasher.ComputeHash(request);
        var text = new StringBuilder();
        int nextChunkIndex = 0;
        string? finishReason = null;

        try
        {
            await foreach (var delta in client.StreamAsync(request, cancellationToken))
            {
                var chunk = new LlmStreamChunk(streamId, nextChunkIndex, delta.Text, delta.IsFinal, delta.FinishReason);
                nextChunkIndex++;
                text.Append(delta.Text);
                finishReason = delta.FinishReason ?? finishReason;
                onChunk?.Invoke(chunk);
            }

            return new LlmStreamSnapshot(streamId, requestHash, LlmStreamStatus.Completed, nextChunkIndex, text.ToString(), finishReason, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new LlmStreamSnapshot(streamId, requestHash, LlmStreamStatus.Cancelled, nextChunkIndex, text.ToString(), finishReason, "stream cancelled");
        }
        catch (Exception ex)
        {
            return new LlmStreamSnapshot(streamId, requestHash, LlmStreamStatus.Failed, nextChunkIndex, text.ToString(), finishReason, Sanitize(ex.Message));
        }
    }

    private static string Sanitize(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "stream failed";
        return value.Replace('\n', ' ').Replace('\r', ' ').Trim();
    }
}
