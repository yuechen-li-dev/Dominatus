using System.Threading;
using Dominatus.Core;
using Dominatus.Core.Blackboard;
using Dominatus.Core.Hfsm;
using Dominatus.Core.Nodes;
using Dominatus.Core.Runtime;
using Dominatus.Llm.OptFlow;

Console.WriteLine("Dominatus.Llm Demo — Oracle Greeting");

var options = DemoOptionsParser.Parse(args);
if (options is null)
{
    PrintUsage();
    return;
}

try
{
    RunDemo(options);
}
catch (Exception ex)
{
    Console.WriteLine($"Unhandled demo failure: {ex.Message}");
    Environment.ExitCode = 1;
}

static void RunDemo(DemoOptions options)
{
    var request = OracleScenario.BuildRequest();
    var requestHash = LlmRequestHasher.ComputeHash(request);

    ILlmCassette cassette = CreateCassette(options, requestHash, request);

    var cassetteMode = options.Mode is DemoMode.Live
        ? LlmCassetteMode.Live
        : options.Mode is DemoMode.Record
            ? LlmCassetteMode.Record
            : options.Mode is DemoMode.Replay
                ? LlmCassetteMode.Replay
                : LlmCassetteMode.Strict;

    RunSingleMode(options, cassetteMode, cassette, requestHash);

    if (options.Mode is DemoMode.Record && cassette is JsonLlmCassette jsonCassette)
    {
        jsonCassette.Save();
    }
}

static ILlmCassette CreateCassette(DemoOptions options, string requestHash, LlmTextRequest request)
{
    if (!string.IsNullOrWhiteSpace(options.CassettePath))
    {
        return JsonLlmCassette.LoadOrCreate(options.CassettePath);
    }

    var cassette = new InMemoryLlmCassette();

    if (options.Mode is DemoMode.Replay or DemoMode.Strict)
    {
        cassette.Put(requestHash, request, new LlmTextResult(OracleScenario.FakeResponse, requestHash));
    }

    return cassette;
}

static void RunSingleMode(DemoOptions options, LlmCassetteMode cassetteMode, ILlmCassette cassette, string requestHash)
{
    var client = new FakeLlmClient(OracleScenario.FakeResponse);
    var (world, ctx) = CreateWorldAndCtx(client, cassetteMode, cassette);

    var step = OracleScenario.BuildStep();

    try
    {
        ExecuteStep(step, ctx);

        Console.WriteLine($"Mode: {options.Mode}");
        Console.WriteLine($"CassettePath: {options.CassettePath ?? "<in-memory>"}");
        Console.WriteLine($"StableId: {OracleScenario.StableId}");
        Console.WriteLine($"RequestHash: {requestHash}");
        Console.WriteLine($"ProviderCalled: {client.CallCount > 0}");
        Console.WriteLine($"Oracle.Line: {ctx.Bb.GetOrDefault(OracleScenario.OracleLineKey, string.Empty)}");
    }
    catch (InvalidOperationException ex) when (options.Mode is DemoMode.StrictMiss)
    {
        Console.WriteLine("Expected strict miss failure:");
        Console.WriteLine("  Mode: Strict");
        Console.WriteLine($"  CassettePath: {options.CassettePath ?? "<in-memory>"}");
        Console.WriteLine($"  StableId: {OracleScenario.StableId}");
        Console.WriteLine($"  RequestHash: {requestHash}");
        Console.WriteLine($"  Reason: {ex.Message}");
    }

    GC.KeepAlive(world);
}

static void ExecuteStep(AiStep step, AiCtx ctx)
{
    var wait = (IWaitEvent)step;
    var cursor = default(EventCursor);

    for (int i = 0; i < 4; i++)
    {
        if (wait.TryConsume(ctx, ref cursor))
        {
            return;
        }
    }

    throw new InvalidOperationException("LLM text demo step did not complete.");
}

static (AiWorld World, AiCtx Ctx) CreateWorldAndCtx(ILlmClient client, LlmCassetteMode mode, ILlmCassette cassette)
{
    var host = new ActuatorHost();
    host.Register(new LlmTextActuationHandler(client, cassette, mode));

    var world = new AiWorld(host);

    var graph = new HfsmGraph { Root = "Root" };
    graph.Add(new HfsmStateDef { Id = "Root", Node = RootNode });

    var agent = new AiAgent(new HfsmInstance(graph, new HfsmOptions()));
    world.Add(agent);

    var ctx = new AiCtx(world, agent, agent.Events, CancellationToken.None, world.View, world.Mail, world.Actuator);
    return (world, ctx);
}

static IEnumerator<AiStep> RootNode(AiCtx _)
{
    yield break;
}

static void PrintUsage()
{
    Console.WriteLine("Usage: dotnet run --project samples/Dominatus.Llm.DemoConsole -- --mode <live|record|replay|strict|strict-miss> [--cassette <path>]");
}

enum DemoMode
{
    Live,
    Record,
    Replay,
    Strict,
    StrictMiss,
}

sealed record DemoOptions(DemoMode Mode, string? CassettePath);

static class DemoOptionsParser
{
    public static DemoOptions? Parse(string[] args)
    {
        string? modeValue = null;
        string? cassettePath = null;

        for (int i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (string.Equals(arg, "--mode", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                modeValue = args[++i];
                continue;
            }

            if (string.Equals(arg, "--cassette", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                cassettePath = args[++i];
            }
        }

        if (modeValue is null)
        {
            return null;
        }

        var mode = modeValue.ToLowerInvariant() switch
        {
            "live" => DemoMode.Live,
            "record" => DemoMode.Record,
            "replay" => DemoMode.Replay,
            "strict" => DemoMode.Strict,
            "strict-miss" => DemoMode.StrictMiss,
            _ => (DemoMode?)null,
        };

        return mode is null ? null : new DemoOptions(mode.Value, cassettePath);
    }
}

static class OracleScenario
{
    public static readonly BbKey<string> OracleLineKey = new("oracle.line");

    public const string StableId = "demo.oracle.greeting.v1";
    public const string Intent = "greet the player at the shrine";
    public const string Persona = "Ancient oracle. Warm, cryptic, concise.";
    public const string FakeResponse = "Mira, the moonlit shrine remembers your footsteps before you make them.";

    public static AiStep BuildStep()
        => Llm.Text(
            stableId: StableId,
            intent: Intent,
            persona: Persona,
            context: ctx => ctx
                .Add("playerName", "Mira")
                .Add("location", "moonlit shrine")
                .Add("oracleMood", "pleased but ominous"),
            storeAs: OracleLineKey);

    public static LlmTextRequest BuildRequest()
    {
        var context = new LlmContextBuilder()
            .Add("playerName", "Mira")
            .Add("location", "moonlit shrine")
            .Add("oracleMood", "pleased but ominous")
            .BuildCanonicalJson();

        return new LlmTextRequest(
            StableId: StableId,
            Intent: Intent,
            Persona: Persona,
            CanonicalContextJson: context,
            Sampling: Llm.DefaultSampling,
            PromptTemplateVersion: LlmTextRequest.DefaultPromptTemplateVersion,
            OutputContractVersion: LlmTextRequest.DefaultOutputContractVersion);
    }
}
