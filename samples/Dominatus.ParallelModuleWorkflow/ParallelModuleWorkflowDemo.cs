using System.Collections.Concurrent;
using System.Threading;
using Dominatus.Core;
using Dominatus.Core.Blackboard;
using Dominatus.Core.Hfsm;
using Dominatus.Core.Nodes;
using Dominatus.Core.Runtime;
using Dominatus.Llm.OptFlow;
using LlmApi = Dominatus.Llm.OptFlow.Llm;

namespace Dominatus.ParallelModuleWorkflow;

public enum ModuleName
{
    Auth,
    Api,
    Database,
    Frontend
}

public sealed record ModuleResult
{
    public required ModuleName Module { get; init; }
    public required string StableId { get; init; }
    public required string Output { get; init; }
    public required DateTimeOffset StartedUtc { get; init; }
    public required DateTimeOffset CompletedUtc { get; init; }
}

public sealed record ParallelModuleWorkflowResult
{
    public required string Contract { get; init; }
    public required IReadOnlyList<ModuleResult> ModuleResults { get; init; }
    public required string FinalReport { get; init; }
    public required IReadOnlyList<string> Events { get; init; }
    public required int MaxObservedConcurrency { get; init; }
    public required bool UsedLlmCall { get; init; }
}

public static class ParallelModuleWorkflowDemo
{
    public const string AuthStableId = "parallel.auth.design-contract";
    public const string ApiStableId = "parallel.api.implement";
    public const string DatabaseStableId = "parallel.database.implement";
    public const string FrontendStableId = "parallel.frontend.implement";

    private static readonly IReadOnlyList<ModuleName> DeterministicMergeOrder =
    [
        ModuleName.Auth,
        ModuleName.Api,
        ModuleName.Database,
        ModuleName.Frontend
    ];

    public static async Task<ParallelModuleWorkflowResult> RunAsync(
        TextWriter? output = null,
        CancellationToken cancellationToken = default)
    {
        output ??= TextWriter.Null;

        var fakeLlm = new DeterministicParallelWorkflowLlmClient();
        var auth = await RunModuleAsync(ModuleName.Auth, AuthStableId, contract: null, fakeLlm, cancellationToken).ConfigureAwait(false);
        string contract = auth.Output;

        ModuleResult[] parallelResults = await Task.WhenAll(
            RunModuleAsync(ModuleName.Api, ApiStableId, contract, fakeLlm, cancellationToken),
            RunModuleAsync(ModuleName.Database, DatabaseStableId, contract, fakeLlm, cancellationToken),
            RunModuleAsync(ModuleName.Frontend, FrontendStableId, contract, fakeLlm, cancellationToken)).ConfigureAwait(false);

        var byModule = parallelResults
            .Append(auth)
            .ToDictionary(result => result.Module, result => result);

        var merged = DeterministicMergeOrder
            .Select(module => byModule[module])
            .ToArray();

        string finalReport = BuildFinalReport(contract, merged, fakeLlm.MaxObservedConcurrency);
        var events = BuildDeterministicEvents().ToArray();
        var result = new ParallelModuleWorkflowResult
        {
            Contract = contract,
            ModuleResults = merged,
            FinalReport = finalReport,
            Events = events,
            MaxObservedConcurrency = fakeLlm.MaxObservedConcurrency,
            UsedLlmCall = fakeLlm.CallCount == 4 && StableIdsInOrder(merged).SequenceEqual(
            [
                AuthStableId,
                ApiStableId,
                DatabaseStableId,
                FrontendStableId
            ], StringComparer.Ordinal)
        };

        await output.WriteLineAsync("Dominatus parallel module workflow demo").ConfigureAwait(false);
        await output.WriteLineAsync(finalReport).ConfigureAwait(false);
        return result;
    }

    private static async Task<ModuleResult> RunModuleAsync(
        ModuleName module,
        string stableId,
        string? contract,
        DeterministicParallelWorkflowLlmClient fakeLlm,
        CancellationToken cancellationToken)
    {
        return await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var started = DateTimeOffset.UtcNow;
            string moduleOutput = RunIsolatedDominatusLlmWorkflow(module, stableId, contract, fakeLlm, cancellationToken);
            var completed = DateTimeOffset.UtcNow;

            return new ModuleResult
            {
                Module = module,
                StableId = stableId,
                Output = moduleOutput,
                StartedUtc = started,
                CompletedUtc = completed
            };
        }, cancellationToken).ConfigureAwait(false);
    }

    private static string RunIsolatedDominatusLlmWorkflow(
        ModuleName module,
        string stableId,
        string? contract,
        DeterministicParallelWorkflowLlmClient fakeLlm,
        CancellationToken cancellationToken)
    {
        var textKey = new BbKey<string>($"ParallelModuleWorkflow.{module}.Text");
        var resultJsonKey = new BbKey<string>($"ParallelModuleWorkflow.{module}.ResultJson");

        var host = new ActuatorHost();
        host.Register(new LlmTextActuationHandler(fakeLlm, new InMemoryLlmCassette(), LlmCassetteMode.Live));

        var world = new AiWorld(host);
        var graph = new HfsmGraph { Root = "Root" };
        graph.Add(new HfsmStateDef { Id = "Root", Node = EmptyNode });
        var agent = new AiAgent(new HfsmInstance(graph, new HfsmOptions()));
        world.Add(agent);
        var ctx = new AiCtx(world, agent, agent.Events, cancellationToken, world.View, world.Mail, world.Actuator, new LiveWorldBb(world.Bb));

        var step = LlmApi.Call(
            stableId,
            intent: module == ModuleName.Auth ? "Design the shared auth contract." : "Implement the module against the shared auth contract.",
            persona: $"Deterministic {module} module worker",
            context: builder =>
            {
                builder.Add("module", module.ToString());
                builder.Add("workflow", "parallel-module-workflow-m0");
                if (contract is not null)
                {
                    builder.Add("authContract", contract);
                }
            },
            storeTextAs: textKey,
            storeResultJsonAs: resultJsonKey);

        var wait = (IWaitEvent)step;
        var cursor = default(EventCursor);
        if (!wait.TryConsume(ctx, ref cursor))
        {
            wait.TryConsume(ctx, ref cursor);
        }

        return ctx.Bb.GetOrDefault(textKey, string.Empty);
    }

    private static IEnumerator<AiStep> EmptyNode(AiCtx _)
    {
        yield break;
    }

    private static string BuildFinalReport(string contract, IReadOnlyList<ModuleResult> moduleResults, int maxObservedConcurrency)
    {
        var lines = new List<string>
        {
            "Final parallel module workflow report",
            $"Contract: {contract}",
            "Dependency evidence: Auth contract completed before Api, Database, and Frontend workers were launched.",
            $"Parallel evidence: Api, Database, and Frontend reached the fake LLM barrier together; max observed concurrency={maxObservedConcurrency}.",
            "Merged module statuses:"
        };

        lines.AddRange(moduleResults.Select(result => $"- {result.Module}: {result.Output}"));
        return string.Join(Environment.NewLine, lines);
    }

    private static IEnumerable<string> BuildDeterministicEvents()
    {
        yield return "Auth contract ready";
        yield return "Starting parallel module workers";
        yield return "Api started";
        yield return "Database started";
        yield return "Frontend started";
        yield return "Api completed";
        yield return "Database completed";
        yield return "Frontend completed";
        yield return "Parallel module workers completed";
        yield return "Final report ready";
    }

    private static IEnumerable<string> StableIdsInOrder(IEnumerable<ModuleResult> moduleResults)
        => moduleResults.Select(result => result.StableId);

    private sealed class DeterministicParallelWorkflowLlmClient : ILlmClient
    {
        private static readonly IReadOnlyDictionary<string, string> Outputs = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [AuthStableId] = "AuthContract v1: endpoints=/login,/refresh; token=jwt; userId=string",
            [ApiStableId] = "API module implemented against AuthContract v1.",
            [DatabaseStableId] = "Database schema supports AuthContract v1 users and refresh tokens.",
            [FrontendStableId] = "Frontend login flow uses AuthContract v1 endpoints."
        };

        private readonly ConcurrentDictionary<string, byte> _startedParallelModules = new(StringComparer.Ordinal);
        private readonly TaskCompletionSource _allParallelModulesStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _inFlight;
        private int _maxObservedConcurrency;
        private int _callCount;

        public int MaxObservedConcurrency => Volatile.Read(ref _maxObservedConcurrency);
        public int CallCount => Volatile.Read(ref _callCount);

        public async Task<LlmTextResult> GenerateTextAsync(
            LlmTextRequest request,
            string requestHash,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _callCount);
            int inFlight = Interlocked.Increment(ref _inFlight);
            UpdateMaxObservedConcurrency(inFlight);

            try
            {
                if (IsParallelModule(request.StableId))
                {
                    _startedParallelModules.TryAdd(request.StableId, 0);
                    if (_startedParallelModules.Count == 3)
                    {
                        _allParallelModulesStarted.TrySetResult();
                    }

                    await _allParallelModulesStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
                }

                if (!Outputs.TryGetValue(request.StableId, out string? text))
                {
                    throw new InvalidOperationException($"No fake LLM output registered for stable id '{request.StableId}'.");
                }

                return new LlmTextResult(text, requestHash, Provider: "fake", Model: "parallel-module-workflow-scripted-v1", FinishReason: "stop");
            }
            finally
            {
                Interlocked.Decrement(ref _inFlight);
            }
        }

        private static bool IsParallelModule(string stableId)
            => string.Equals(stableId, ApiStableId, StringComparison.Ordinal)
            || string.Equals(stableId, DatabaseStableId, StringComparison.Ordinal)
            || string.Equals(stableId, FrontendStableId, StringComparison.Ordinal);

        private void UpdateMaxObservedConcurrency(int observed)
        {
            while (true)
            {
                int current = Volatile.Read(ref _maxObservedConcurrency);
                if (observed <= current)
                {
                    return;
                }

                if (Interlocked.CompareExchange(ref _maxObservedConcurrency, observed, current) == current)
                {
                    return;
                }
            }
        }
    }
}
