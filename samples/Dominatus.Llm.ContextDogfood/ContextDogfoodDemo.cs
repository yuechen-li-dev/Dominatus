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
    LlmContextContainerManifest Manifest,
    RustPrimerDogfoodArtifacts RustPrimer);

public sealed record RustPrimerDogfoodArtifacts(
    string OutputDirectory,
    string JsonPath,
    string BinaryContextPath,
    string ManifestPath,
    IReadOnlyDictionary<string, string> PacketPaths,
    IReadOnlyDictionary<string, string> PacketManifestPaths,
    string ReviewPromptPath,
    LlmContextContainerManifest Manifest,
    LlmContextStore Store);

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
        foreach (var loadoutId in new[] { "codex-author", "chatgpt-reviewer", "claude-auditor", "release-prep", "pressure-test" })
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

        var rustPrimer = GenerateRustPrimerArtifacts(outDir, now);
        return new ContextDogfoodResult(outDir, jsonPath, binaryPath, packetPathMap, packetManifestPathMap, packetMap, manifest, rustPrimer);
    }

    private static LlmContextStore CreateStore(DateTimeOffset now)
    {
        var store = new LlmContextStore("PROJECT.dominatus", "Dominatus Project Context", now);
        void Add(string id, string kind, int priority, string title, string content, params string[] tags)
            => store.Upsert(new LlmContextChunk { Id = id, Kind = kind, Priority = priority, Title = title, Content = content, Tags = tags, CreatedUtc = now, UpdatedUtc = now, Version = 1 });

        Add("dominatus.doctrine.orchestration", "doctrine", 100, "Orchestration ownership", "Dominatus owns orchestration; external ecosystems provide capabilities while keeping orchestration logic and refusal policy in Dominatus.", "architecture", "doctrine");
        Add("dominatus.doctrine.llm-role", "doctrine", 95, "LLM role boundaries", "LLMs are author/reviewer/source/sink or high-judgment components, not default live orchestrators and should stay behind explicit policies.", "llm", "doctrine");
        Add("dominatus.doctrine.context", "doctrine", 95, "Context persistence", "LLM context should be generated from explicit persisted chunks, not raw chat transcript, so packet manifests remain inspectable and reproducible.", "llm", "memory", "doctrine");
        Add("dominatus.warning.event-cursor", "warning", 90, "Event cursor caution", "Persistent event protocols should use sequence/correlation IDs; Ai.Event<T> defaults future-only.", "events");
        Add("dominatus.warning.semantic-kernel", "warning", 85, "Semantic Kernel scope", "Semantic Kernel is a capability/plugin surface; do not use SK planners/agents/orchestration in Dominatus samples.", "sk");
        Add("dominatus.state.release-0.2", "project-state", 80, "Release baseline", "Published/prepped 0.2.0 baseline: Core/OptFlow/Standard/HomeAssistant/Server. Next package wave (Llm.Context hardening and follow-on LLM work) still needs fresh release-prep gating before publish.", "release", "milestone", "release-gate");
        Add("dominatus.state.semantic-kernel-sample", "sample-state", 80, "SK sample state", "SemanticKernelOrchestration sample maps ledger loop to WorldBb/mailbox/Ai.Decide/SK actuators; Core fixed the event-cursor issue with future-only default waits, while sample sequence/correlation discipline remains the protocol model.", "sample", "implementation");
        Add("dominatus.state.llm-context", "project-state", 85, "Llm.Context status", "Implemented milestones: M0 store model, M1 loadouts, M2 container, M3 dogfood, M4.1 packet+manifest diagnostics, and M8b context-packet integration call surface. Current next action from review is M4.3 hardening then release prep. Known rough edges were release-prep gate coverage, budget-pressure evidence, and manifest enum readability.", "llm", "milestone", "implementation");
        Add("dominatus.state.testing", "project-state", 72, "Test matrix", "Context package tests run on net8.0 and net10.0 and are expected to stay provider-free.", "tests");
        Add("dominatus.constraint.no-live-providers", "constraint", 86, "No live providers", "Dogfood and tests must not call live providers, require API keys, or rely on network runtime behavior.", "constraints", "release-gate", "risk");
        Add("dominatus.decision.context-store-not-transcript", "decision", 90, "Store over transcript", "Treat transcript as artifact, not database. Use explicit chunks and generated packets.", "decisions");
        Add("dominatus.decision.refusal", "decision", 88, "Refusal outcome", "Llm.Decide and MagiDecide support mandatory refusal outcome with rationale; proposed alternatives are non-executable.", "decisions", "release-gate", "risk");
        Add("dominatus.audit.packet-observability", "audit", 78, "Packet observability", "Each role loadout should emit inspectable markdown artifacts and explicit included/omitted chunks.", "audit", "implementation");
        Add("dominatus.open-loop.context-optflow-integration", "open-loop", 70, "OptFlow integration", "Future work: integrate generated context packets into Llm.Decide/MagiDecide context builders.", "future");
        Add("dominatus.open-loop.context-update-approval", "open-loop", 65, "Update approval", "Future work: context update commands/approval workflow so LLMs can propose memory updates without unilateral durable writes.", "future");
        Add("dominatus.release-state.preview-channel", "release-state", 76, "Preview release channel", "LLM context package remains preview and should ship with dogfood docs before provider integration.", "release", "release-gate");

        store.UpsertLoadout(new LlmContextLoadout { Id = "codex-author", Title = "Codex Author", Description = "Implementation author context", IncludeKinds = ["project-state", "sample-state", "constraint", "warning", "open-loop", "decision"], RequiredChunkIds = ["dominatus.doctrine.orchestration", "dominatus.doctrine.context"], MaxChars = 6000 });
        store.UpsertLoadout(new LlmContextLoadout { Id = "chatgpt-reviewer", Title = "ChatGPT Reviewer", Description = "Review and design context", IncludeKinds = ["doctrine", "decision", "warning", "project-state", "open-loop"], MaxChars = 8000 });
        store.UpsertLoadout(new LlmContextLoadout { Id = "claude-auditor", Title = "Claude Auditor", Description = "Audit and edge-case context", IncludeKinds = ["warning", "decision", "audit", "project-state", "open-loop"], RequiredChunkIds = ["dominatus.doctrine.llm-role", "dominatus.doctrine.orchestration"], MaxChars = 7000 });
        store.UpsertLoadout(new LlmContextLoadout { Id = "release-prep", Title = "Release Prep", Description = "Release preflight context", IncludeKinds = ["release-state", "project-state", "warning", "open-loop"], RequiredChunkIds = ["dominatus.constraint.no-live-providers", "dominatus.decision.refusal"], MaxChars = 5000 });
        store.UpsertLoadout(new LlmContextLoadout { Id = "pressure-test", Title = "Pressure Test", Description = "Intentionally tight loadout used to verify budget omission diagnostics.", IncludeKinds = ["doctrine", "warning", "decision", "project-state", "open-loop", "constraint", "audit", "release-state", "sample-state"], RequiredChunkIds = ["dominatus.doctrine.orchestration"], MaxChars = 1500 });

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
        sb.AppendLine("13. Does the packet provenance clearly explain which loadout/query produced this packet?");
        sb.AppendLine("14. Did pressure-test show useful budget omission behavior?");
        sb.AppendLine("15. Were enum names readable without looking up numeric values?");
        sb.AppendLine("16. Are release-prep gate chunks now present?");
        return sb.ToString();
    }

    private static RustPrimerDogfoodArtifacts GenerateRustPrimerArtifacts(string outputRoot, DateTimeOffset now)
    {
        var outDir = Path.Combine(outputRoot, "primers", "rust");
        var packetsDir = Path.Combine(outDir, "packets");
        Directory.CreateDirectory(packetsDir);
        var store = CreateRustPrimerStore(now);

        var jsonPath = Path.Combine(outDir, "RUST.primer.context.json");
        LlmContextStoreJson.Save(jsonPath, store);
        var binaryPath = Path.Combine(outDir, "RUST.primer.context");
        LlmContextContainer.Save(binaryPath, store);
        var manifest = LlmContextContainer.ReadManifest(binaryPath);
        var manifestPath = Path.Combine(outDir, "manifest.json");
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));

        var packets = new Dictionary<string, string>(StringComparer.Ordinal);
        var packetManifests = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var loadoutId in new[] { "rust-author", "rust-reviewer", "rust-auditor" })
        {
            var packet = store.BuildPacket(loadoutId, now);
            var packetPath = Path.Combine(packetsDir, $"{loadoutId}.md");
            File.WriteAllText(packetPath, packet.Text);
            var packetManifestPath = Path.Combine(packetsDir, $"{loadoutId}.manifest.json");
            File.WriteAllText(packetManifestPath, LlmContextPacketManifestJson.Serialize(packet.ToManifest()));
            packets[loadoutId] = packetPath;
            packetManifests[loadoutId] = packetManifestPath;
        }

        var reviewPromptPath = Path.Combine(packetsDir, "PRIMER_REVIEW_PROMPT.md");
        File.WriteAllText(reviewPromptPath, BuildPrimerReviewPrompt());
        return new RustPrimerDogfoodArtifacts(outDir, jsonPath, binaryPath, manifestPath, packets, packetManifests, reviewPromptPath, manifest, store);
    }

    private static LlmContextStore CreateRustPrimerStore(DateTimeOffset now)
    {
        var store = new LlmContextStore("RUST.primer", "Rust Primer Context", now);
        void Add(string id, string kind, int priority, string title, string content, params string[] tags)
            => store.Upsert(new LlmContextChunk { Id = id, Kind = kind, Priority = priority, Title = title, Content = content, Tags = tags, CreatedUtc = now, UpdatedUtc = now, Version = 1 });
        Add("rust.primer.intent", "primer-doctrine", 100, "Rust primer intent", "Define a narrow Rust subset for authors and reviewers. Reduce ownership pretzeling, avoid compiler-fighting designs, and keep code explicit and refactorable.", "rust", "primer");
        Add("rust.primer.goal", "primer-doctrine", 98, "Rust primer goal", "Use boring explicit Rust where ownership, mutation, lifetimes, and error flow remain local and obvious.", "rust", "primer");
        Add("rust.primer.core-principles", "primer-rule", 95, "Core principles", "Prefer dull solutions. Prefer owned data over delicate borrowed relationships and short borrows over long coupling. Prefer plain structs/enums before trait architecture theater. Prefer cheap clones over lifetime pretzeling when clarity improves. Do not fight the borrow checker with complexity.", "rust", "principles");
        Add("rust.primer.must-do", "primer-rule", 92, "Must do", "Target stable modern Rust, keep ownership explicit, keep borrow scopes short, use Result/Option idiomatically, keep mutation visible, and follow repository-local patterns.", "rust", "must-do");
        Add("rust.primer.restricted", "primer-restriction", 94, "Restricted features", "unsafe, Rc<RefCell<T>>, Arc<Mutex<T>>/Arc<RwLock<T>>, broad interior mutability, explicit lifetime-heavy public APIs, trait/macro-heavy abstraction, async spread, and self-referential patterns are restricted or banned.", "rust", "restriction");
        Add("rust.primer.pattern.ownership", "primer-pattern", 90, "Pattern: ownership", "Own data by default; avoid storing references in normal structs; use handles/IDs over reference graphs when relationships persist.");
        Add("rust.primer.pattern.borrowing", "primer-pattern", 89, "Pattern: borrowing", "Borrow briefly and locally; split steps to shorten lifetimes; end borrows before mutation; avoid chained expressions that widen scopes.");
        Add("rust.primer.pattern.cloning", "primer-pattern", 88, "Pattern: cloning", "Use cheap/local clones to simplify ownership and avoid lifetime pretzels; do not optimize away tiny clones at the cost of clarity.");
        Add("rust.primer.pattern.structs-enums-traits", "primer-pattern", 87, "Pattern: concrete first", "Start with structs/enums and inherent impls; use traits only with real interface boundaries.");
        Add("rust.primer.pattern.generics", "primer-pattern", 86, "Pattern: generics", "Keep generics boring, concrete-first, and bounds readable; avoid advanced trait machinery for ordinary logic.");
        Add("rust.primer.pattern.iteration", "primer-pattern", 85, "Pattern: iteration/control flow", "Prefer clear loops when they improve control-flow readability; avoid iterator/combinator soup.");
        Add("rust.primer.pattern.error-handling", "primer-pattern", 84, "Pattern: error handling", "Use Result and Option directly, use ? for readable fallible flow, and avoid theatrical local error hierarchies.");
        Add("rust.primer.pattern.mutation-state", "primer-pattern", 83, "Pattern: mutation/state", "Keep mutation local and explicit; avoid interior mutability as default; prefer returning owned results over alias-heavy shared mutation.");
        Add("rust.primer.pattern.async", "primer-pattern", 82, "Pattern: async", "Async is not default; keep boundaries narrow and avoid spreading async through modules without material benefit.");
        Add("rust.primer.pattern.unsafe", "primer-pattern", 81, "Pattern: unsafe", "Unsafe must be rare, isolated, and justified with documented invariants; never use unsafe as a shortcut around design issues.");
        Add("rust.primer.review-checklist", "primer-checklist", 80, "Review checklist", "Reject if code relies on ownership/lifetime pretzels, long-lived borrowed fields, unjustified unsafe/interior mutability/async spread, or trait-heavy abstraction without boundary need. Confirm owned data defaults, short borrows, visible mutation, idiomatic errors, and reviewer-local reasoning.");
        Add("rust.example.ownership.good-owned-struct", "primer-example", 70, "Good: owned struct", "why: owns data and avoids lifetime plumbing.\ncode:\nstruct UserRecord { user_id: String, display_name: String }");
        Add("rust.example.ownership.bad-borrowed-field-pretzel", "primer-example", 69, "Bad: borrowed field pretzel", "why: persistent borrows force lifetime coupling.\ncode:\nstruct UserRecord<'a> { user_id: &'a str, display_name: &'a str }");
        Add("rust.example.borrowing.good-short-borrow-scope", "primer-example", 68, "Good: short borrow scope", "why: borrow ends before mutation.\ncode:\nlet count = names.len();\nnames.push(format!(\"count={count}\"));");
        Add("rust.example.borrowing.bad-chained-borrow-coupling", "primer-example", 67, "Bad: chained borrow coupling", "why: expression chain widens borrow scope and coupling.\ncode:\nlet summary = names.first().map(|n| format!(\"{n}-{}\", names.len()));");
        Add("rust.example.cloning.good-small-clone-for-clarity", "primer-example", 66, "Good: small clone for clarity", "why: clone simplifies ownership and keeps flow local.\ncode:\nlet first = items.first()?.clone();\nSome((first.clone(), first));");
        Add("rust.example.cloning.bad-clone-avoidance-pretzel", "primer-example", 65, "Bad: clone avoidance pretzel", "why: avoiding cheap clone causes lifetime-heavy API.\ncode:\nfn rename_first_with_suffix<'a>(items: &'a mut [String], suffix: &'a str) -> &'a str { /* ... */ }");
        Add("rust.example.traits.good-concrete-first-design", "primer-example", 64, "Good: concrete first", "why: plain struct + inherent methods are clearer.\ncode:\nstruct Counter { value: usize }\nimpl Counter { fn increment(&mut self){ self.value += 1; } }");
        Add("rust.example.traits.bad-traitify-everything", "primer-example", 63, "Bad: traitify everything", "why: trait indirection added without boundary need.\ncode:\ntrait CounterBehavior { fn increment(&mut self); }");
        Add("rust.example.iteration.good-clear-loop", "primer-example", 62, "Good: clear loop", "why: loop makes control flow and mutation obvious.\ncode:\nfor value in values { if value % 2 == 0 { result.push(value.to_string()); } }");
        Add("rust.example.iteration.bad-iterator-soup", "primer-example", 61, "Bad: iterator soup", "why: dense combinators reduce debuggability.\ncode:\nvalues.iter().enumerate().filter_map(...).map(...).collect()");
        Add("rust.example.interior-mutability.bad-rc-refcell-emotional-support", "primer-example", 60, "Bad: Rc<RefCell> emotional support", "why: shared mutation introduced instead of simplifying ownership.\ncode:\nstruct Node { name: Rc<RefCell<String>> }");
        Add("rust.example.unsafe.bad-unsafe-for-no-reason", "primer-example", 59, "Bad: unsafe for no reason", "why: unsafe used without boundary need/invariant.\ncode:\nunsafe { *data.get_unchecked(0) }");

        store.UpsertLoadout(new LlmContextLoadout { Id = "rust-author", Title = "Rust Author", Description = "Implementation authoring primer packet", IncludeKinds = ["primer-doctrine", "primer-rule", "primer-pattern", "primer-example", "primer-checklist"], RequiredChunkIds = ["rust.primer.intent", "rust.primer.must-do", "rust.primer.pattern.ownership"], MaxChars = 9500 });
        store.UpsertLoadout(new LlmContextLoadout { Id = "rust-reviewer", Title = "Rust Reviewer", Description = "Subset-violation review packet", IncludeKinds = ["primer-restriction", "primer-checklist", "primer-rule", "primer-example", "primer-doctrine"], RequiredChunkIds = ["rust.primer.restricted", "rust.primer.review-checklist"], MaxChars = 8000 });
        store.UpsertLoadout(new LlmContextLoadout { Id = "rust-auditor", Title = "Rust Auditor", Description = "Footgun-focused audit packet", IncludeKinds = ["primer-restriction", "primer-pattern", "primer-checklist", "primer-example"], RequiredChunkIds = ["rust.primer.restricted", "rust.primer.pattern.unsafe", "rust.primer.pattern.async", "rust.primer.pattern.mutation-state"], MaxChars = 7200 });
        return store;
    }

    private static string BuildPrimerReviewPrompt()
        => """
           # PRIMER.context Rust Packet Review Prompt

           1. Would rust-author help you implement Rust without borrow-checker pretzeling?
           2. Would rust-reviewer help you catch subset violations?
           3. Would rust-auditor help catch unsafe/interior-mutability/async footguns?
           4. Which chunks are too broad or too terse?
           5. Which examples are most useful?
           6. Which examples should be added?
           7. Should primer examples be included inline or referenced on demand?
           8. What should be reusable PRIMER.context vs project-specific PROJECT.context?
           """;
}
