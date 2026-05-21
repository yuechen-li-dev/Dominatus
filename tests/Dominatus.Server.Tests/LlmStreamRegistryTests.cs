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
}
