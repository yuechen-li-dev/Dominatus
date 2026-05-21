using Dominatus.Llm.Context;
using System.Text;
using System.Text.Json;

namespace Dominatus.Llm.ContextDogfood;

public sealed record ContextDogfoodResult(
    string OutputDirectory,
    string JsonPath,
    string BinaryContextPath,
    IReadOnlyDictionary<string, string> PacketPaths,
    IReadOnlyDictionary<string, string> PacketManifestPaths,
    IReadOnlyDictionary<string, LlmContextPacket> Packets,
    LlmContextContainerManifest Manifest);

public static class ContextDogfoodDemo
{
    public static ContextDogfoodResult Run(string? outputDirectory = null, TextWriter? output = null)
    {
        var writer = output ?? TextWriter.Null;
        var now = new DateTimeOffset(2026, 5, 20, 12, 0, 0, TimeSpan.Zero);
        var outDir = outputDirectory ?? Path.Combine("artifacts", "llm-context-dogfood");
        var packetsDir = Path.Combine(outDir, "packets");
        Directory.CreateDirectory(packetsDir);

        var store = CreateStore(now);

        var jsonPath = Path.Combine(outDir, "PROJECT.dominatus.context.json");
        LlmContextStoreJson.Save(jsonPath, store);

        var binaryPath = Path.Combine(outDir, "PROJECT.dominatus.context");
        LlmContextContainer.Save(binaryPath, store);
        var manifest = LlmContextContainer.ReadManifest(binaryPath);

        var packetMap = new Dictionary<string, LlmContextPacket>(StringComparer.Ordinal);
        var packetPathMap = new Dictionary<string, string>(StringComparer.Ordinal);
        var packetManifestPathMap = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var loadoutId in new[] { "codex-author", "chatgpt-reviewer", "claude-auditor", "release-prep" })
        {
            var packet = store.BuildPacket(loadoutId, now);
            var packetPath = Path.Combine(packetsDir, $"{loadoutId}.md");
            File.WriteAllText(packetPath, packet.Text);
            var manifestJsonPath = Path.Combine(packetsDir, $"{loadoutId}.manifest.json");
            File.WriteAllText(manifestJsonPath, LlmContextPacketManifestJson.Serialize(packet.ToManifest()));
            packetMap[loadoutId] = packet;
            packetPathMap[loadoutId] = packetPath;
            packetManifestPathMap[loadoutId] = manifestJsonPath;
        }

        var manifestPath = Path.Combine(outDir, "manifest.json");
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));

        var reviewPromptPath = Path.Combine(packetsDir, "LLM_REVIEW_PROMPT.md");
        File.WriteAllText(reviewPromptPath, BuildReviewPrompt());

        writer.WriteLine("=== Dominatus LLM Context Dogfood ===");
        writer.WriteLine($"Wrote JSON store: {jsonPath}");
        writer.WriteLine($"Wrote binary context: {binaryPath}");
        foreach (var pair in packetPathMap)
        {
            writer.WriteLine($"Wrote packet: {pair.Key} {pair.Value}");
        }
        writer.WriteLine($"Wrote manifest: {manifestPath}");
        foreach (var pair in packetManifestPathMap)
        {
            writer.WriteLine($"Wrote packet manifest: {pair.Key} {pair.Value}");
        }
        writer.WriteLine($"Wrote review prompt: {reviewPromptPath}");
        writer.WriteLine();
        writer.WriteLine("LLM review prompt:");
        writer.WriteLine("Open packets/chatgpt-reviewer.md and answer:");
        writer.WriteLine("1. Is the context enough to understand the current Dominatus state?");
        writer.WriteLine("2. What chunks are missing?");
        writer.WriteLine("3. What should become persistent SOUL/PROJECT context?");

        return new ContextDogfoodResult(outDir, jsonPath, binaryPath, packetPathMap, packetManifestPathMap, packetMap, manifest);
    }

    private static LlmContextStore CreateStore(DateTimeOffset now)
    {
        var store = new LlmContextStore("PROJECT.dominatus", "Dominatus Project Context", now);
        void Add(string id, string kind, int priority, string title, string content, params string[] tags)
            => store.Upsert(new LlmContextChunk { Id = id, Kind = kind, Priority = priority, Title = title, Content = content, Tags = tags, CreatedUtc = now, UpdatedUtc = now, Version = 1 });

        Add("dominatus.doctrine.orchestration", "doctrine", 100, "Orchestration ownership", "Dominatus owns orchestration; external ecosystems provide capabilities.", "architecture");
        Add("dominatus.doctrine.llm-role", "doctrine", 95, "LLM role boundaries", "LLMs are author/reviewer/source/sink or high-judgment components, not default live orchestrators.", "llm");
        Add("dominatus.doctrine.context", "doctrine", 95, "Context persistence", "LLM context should be generated from explicit persisted chunks, not raw chat transcript.", "llm", "memory");
        Add("dominatus.warning.event-cursor", "warning", 90, "Event cursor caution", "Persistent event protocols should use sequence/correlation IDs; Ai.Event<T> defaults future-only.", "events");
        Add("dominatus.warning.semantic-kernel", "warning", 85, "Semantic Kernel scope", "Semantic Kernel is a capability/plugin surface; do not use SK planners/agents/orchestration in Dominatus samples.", "sk");
        Add("dominatus.state.release-0.2", "project-state", 80, "Release baseline", "Core/OptFlow/Standard/HomeAssistant/Server released/prepped at 0.2.0; LLM/Stride preview.", "release");
        Add("dominatus.state.semantic-kernel-sample", "sample-state", 80, "SK sample state", "SemanticKernelOrchestration sample maps ledger loop to WorldBb/mailbox/Ai.Decide/SK actuators.", "sample");
        Add("dominatus.state.llm-context", "project-state", 85, "Llm.Context status", "Llm.Context M0/M1/M2 implemented JSON store, loadouts, binary .context container.", "llm");
        Add("dominatus.state.testing", "project-state", 72, "Test matrix", "Context package tests run on net8.0 and net10.0 and are expected to stay provider-free.", "tests");
        Add("dominatus.constraint.no-live-providers", "constraint", 86, "No live providers", "Dogfood and tests must not call live providers, require API keys, or rely on network runtime behavior.", "constraints");
        Add("dominatus.decision.context-store-not-transcript", "decision", 90, "Store over transcript", "Treat transcript as artifact, not database. Use explicit chunks and generated packets.", "decisions");
        Add("dominatus.decision.refusal", "decision", 88, "Refusal outcome", "Llm.Decide and MagiDecide support mandatory refusal outcome with rationale; proposed alternatives are non-executable.", "decisions");
        Add("dominatus.audit.packet-observability", "audit", 78, "Packet observability", "Each role loadout should emit inspectable markdown artifacts and explicit included/omitted chunks.", "audit");
        Add("dominatus.open-loop.context-optflow-integration", "open-loop", 70, "OptFlow integration", "Future work: integrate generated context packets into Llm.Decide/MagiDecide context builders.", "future");
        Add("dominatus.open-loop.context-update-approval", "open-loop", 65, "Update approval", "Future work: context update commands/approval workflow so LLMs can propose memory updates without unilateral durable writes.", "future");
        Add("dominatus.release-state.preview-channel", "release-state", 76, "Preview release channel", "LLM context package remains preview and should ship with dogfood docs before provider integration.", "release");

        store.UpsertLoadout(new LlmContextLoadout { Id = "codex-author", Title = "Codex Author", Description = "Implementation author context", IncludeKinds = ["project-state", "sample-state", "constraint", "warning", "open-loop", "decision"], MaxChars = 6000 });
        store.UpsertLoadout(new LlmContextLoadout { Id = "chatgpt-reviewer", Title = "ChatGPT Reviewer", Description = "Review and design context", IncludeKinds = ["doctrine", "decision", "warning", "project-state", "open-loop"], MaxChars = 8000 });
        store.UpsertLoadout(new LlmContextLoadout { Id = "claude-auditor", Title = "Claude Auditor", Description = "Audit and edge-case context", IncludeKinds = ["warning", "decision", "audit", "project-state", "open-loop"], MaxChars = 7000 });
        store.UpsertLoadout(new LlmContextLoadout { Id = "release-prep", Title = "Release Prep", Description = "Release preflight context", IncludeKinds = ["release-state", "project-state", "warning", "open-loop"], MaxChars = 5000 });

        return store;
    }

    private static string BuildReviewPrompt()
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Dominatus LLM Context Dogfood Review Prompt");
        sb.AppendLine();
        sb.AppendLine("Review each generated packet markdown plus matching .manifest.json and answer:");
        sb.AppendLine("1. Which loadout would you want for implementation work?");
        sb.AppendLine("2. Which loadout would you want for review/audit work?");
        sb.AppendLine("3. Which packet felt too broad or too narrow?");
        sb.AppendLine("4. Which chunks should be split, merged, renamed, or reprioritized?");
        sb.AppendLine("5. Does this feel better than a pasted conversation summary?");
        sb.AppendLine("6. What additional metadata would help?");
        sb.AppendLine("7. What should become SOUL.context vs PROJECT.context vs SESSION.context?");
        sb.AppendLine("8. Which omitted chunks would you have wanted?");
        sb.AppendLine("9. Were any included chunks low-value?");
        sb.AppendLine("10. Was the loadout budget too tight?");
        sb.AppendLine("11. Were omissions due to filters or budget?");
        sb.AppendLine("12. Should any chunks be split, merged, or reprioritized?");
        return sb.ToString();
    }
}
