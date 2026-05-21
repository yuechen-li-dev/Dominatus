using Dominatus.Llm.OptFlow;

namespace Dominatus.Server.Tests;

public class LlmStreamRegistryTests
{
    [Fact]
    public void StreamRegistry_RecordChunk_StoresOrderedChunks()
    {
        var registry = new DominatusLlmStreamRegistry();
        registry.RecordChunk(new LlmStreamChunkAvailable("s1", 1, "lo", false, null));
        registry.RecordChunk(new LlmStreamChunkAvailable("s1", 0, "Hel", false, null));

        var chunks = registry.GetChunks("s1");

        Assert.Collection(chunks,
            c => Assert.Equal(0, c.Index),
            c => Assert.Equal(1, c.Index));
    }

    [Fact]
    public void StreamRegistry_RecordChunk_DuplicateSameChunkIsIdempotent()
    {
        var registry = new DominatusLlmStreamRegistry();
        var chunk = new LlmStreamChunkAvailable("s1", 0, "Hel", false, null);

        registry.RecordChunk(chunk);
        registry.RecordChunk(chunk);

        Assert.Single(registry.GetChunks("s1"));
    }

    [Fact]
    public void StreamRegistry_RecordChunk_DuplicateDifferentChunkFails()
    {
        var registry = new DominatusLlmStreamRegistry();
        registry.RecordChunk(new LlmStreamChunkAvailable("s1", 0, "Hel", false, null));

        Assert.Throws<InvalidOperationException>(() =>
            registry.RecordChunk(new LlmStreamChunkAvailable("s1", 0, "Hi", false, null)));
    }

    [Fact]
    public void StreamRegistry_RecordSnapshot_StoresFinalStatusAndText()
    {
        var registry = new DominatusLlmStreamRegistry();
        registry.RecordSnapshot(new LlmStreamSnapshot("s1", "h", LlmStreamStatus.Completed, 2, "Hello", "stop"));

        var stream = registry.GetStream("s1");

        Assert.NotNull(stream);
        Assert.Equal("Completed", stream.Status);
        Assert.Equal("Hello", stream.TextSoFar);
        Assert.Equal(2, stream.NextChunkIndex);
    }

    [Fact]
    public void StreamRegistry_GetChunksAfter_ReturnsOnlyMissingChunks()
    {
        var registry = new DominatusLlmStreamRegistry();
        registry.RecordChunk(new LlmStreamChunkAvailable("s1", 0, "Hel", false, null));
        registry.RecordChunk(new LlmStreamChunkAvailable("s1", 1, "lo", true, "stop"));

        var chunks = registry.GetChunks("s1", after: 0);

        Assert.Single(chunks);
        Assert.Equal(1, chunks[0].Index);
    }

    [Fact]
    public void StreamRegistry_UnknownStream_ReturnsNullOrEmpty()
    {
        var registry = new DominatusLlmStreamRegistry();

        Assert.Null(registry.GetStream("missing"));
        Assert.Empty(registry.GetChunks("missing"));
    }

    [Fact]
    public async Task StreamRegistry_WatchChunks_YieldsExistingChunksAfterIndex()
    {
        var registry = new DominatusLlmStreamRegistry();
        registry.RecordChunk(new LlmStreamChunkAvailable("s1", 0, "Hel", false, null));
        registry.RecordChunk(new LlmStreamChunkAvailable("s1", 1, "lo", true, "stop"));

        var chunks = await CollectAsync(registry.WatchChunksAsync("s1", after: 0));

        Assert.Single(chunks);
        Assert.Equal(1, chunks[0].Index);
    }

    [Fact]
    public async Task StreamRegistry_WatchChunks_YieldsNewChunkRecordedAfterSubscribe()
    {
        var registry = new DominatusLlmStreamRegistry();
        registry.RecordChunk(new LlmStreamChunkAvailable("s1", 0, "Hel", false, null));

        var watchTask = WaitFirstAsync(registry.WatchChunksAsync("s1", after: 0));
        await Task.Delay(20);
        registry.RecordChunk(new LlmStreamChunkAvailable("s1", 1, "lo", false, null));

        var chunk = await watchTask;
        Assert.Equal(1, chunk.Index);
    }

    [Fact]
    public async Task StreamRegistry_WatchChunks_CompletesWhenFinalChunkRecorded()
    {
        var registry = new DominatusLlmStreamRegistry();
        registry.RecordChunk(new LlmStreamChunkAvailable("s1", 0, "Hel", false, null));

        var watchTask = CollectAsync(registry.WatchChunksAsync("s1", after: -1));
        await Task.Delay(20);
        registry.RecordChunk(new LlmStreamChunkAvailable("s1", 1, "lo", true, "stop"));

        var chunks = await watchTask;
        Assert.Equal(2, chunks.Count);
        Assert.True(chunks[^1].IsFinal);
    }

    [Fact]
    public async Task StreamRegistry_WatchChunks_CompletesWhenTerminalSnapshotRecorded()
    {
        var registry = new DominatusLlmStreamRegistry();
        registry.RecordChunk(new LlmStreamChunkAvailable("s1", 0, "Hel", false, null));

        var watchTask = CollectAsync(registry.WatchChunksAsync("s1", after: -1));
        await Task.Delay(20);
        registry.RecordSnapshot(new LlmStreamSnapshot("s1", "h", LlmStreamStatus.Completed, 1, "Hel", "stop"));

        var chunks = await watchTask;
        Assert.Single(chunks);
    }

    [Fact]
    public async Task StreamRegistry_WatchChunks_UnknownStreamThrows()
    {
        var registry = new DominatusLlmStreamRegistry();
        await Assert.ThrowsAsync<KeyNotFoundException>(async () =>
        {
            await foreach (var _ in registry.WatchChunksAsync("missing"))
            {
            }
        });
    }

    [Fact]
    public async Task StreamRegistry_WatchChunks_CancellationStopsWatcher()
    {
        var registry = new DominatusLlmStreamRegistry();
        registry.RecordChunk(new LlmStreamChunkAvailable("s1", 0, "Hel", false, null));
        using var cts = new CancellationTokenSource();

        var enumerator = registry.WatchChunksAsync("s1", after: 0, cts.Token).GetAsyncEnumerator(cts.Token);
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await enumerator.MoveNextAsync());
    }

    private static async Task<List<Dominatus.Server.Dtos.LlmStreamChunkDto>> CollectAsync(IAsyncEnumerable<Dominatus.Server.Dtos.LlmStreamChunkDto> source)
    {
        var result = new List<Dominatus.Server.Dtos.LlmStreamChunkDto>();
        await foreach (var item in source)
            result.Add(item);
        return result;
    }

    private static async Task<Dominatus.Server.Dtos.LlmStreamChunkDto> WaitFirstAsync(IAsyncEnumerable<Dominatus.Server.Dtos.LlmStreamChunkDto> source)
    {
        await foreach (var item in source)
            return item;

        throw new InvalidOperationException("Sequence was empty.");
    }
}
