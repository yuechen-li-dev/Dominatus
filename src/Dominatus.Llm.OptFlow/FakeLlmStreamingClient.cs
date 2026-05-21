using System.Runtime.CompilerServices;

namespace Dominatus.Llm.OptFlow;

public sealed class FakeLlmStreamingClient : ILlmStreamingClient
{
    private readonly Dictionary<string, List<LlmStreamDelta>> _streams = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _throwAfterChunk = new(StringComparer.Ordinal);

    public int CallCount { get; private set; }

    public bool ObserveCancellation { get; set; }

    public void Configure(string requestHash, params LlmStreamDelta[] deltas)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestHash);
        ArgumentNullException.ThrowIfNull(deltas);
        _streams[requestHash] = [.. deltas];
    }

    public void ConfigureThrowAfterChunk(string requestHash, int chunkCount)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestHash);
        if (chunkCount < 0) throw new ArgumentOutOfRangeException(nameof(chunkCount));
        _throwAfterChunk[requestHash] = chunkCount;
    }

    public async IAsyncEnumerable<LlmStreamDelta> StreamAsync(LlmTextRequest request, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        CallCount++;

        string hash = LlmRequestHasher.ComputeHash(request);
        if (!_streams.TryGetValue(hash, out var deltas))
        {
            deltas = [];
        }

        for (int i = 0; i < deltas.Count; i++)
        {
            if (ObserveCancellation)
            {
                await Task.Delay(10, cancellationToken);
            }
            else
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            if (_throwAfterChunk.TryGetValue(hash, out var throwAfter) && i >= throwAfter)
            {
                throw new InvalidOperationException("stream failed");
            }

            yield return deltas[i];
        }
    }
}
