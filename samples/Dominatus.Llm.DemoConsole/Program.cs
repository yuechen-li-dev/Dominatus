using System.Threading;
using Dominatus.Core;
using Dominatus.Core.Blackboard;
using Dominatus.Core.Hfsm;
using Dominatus.Core.Nodes;
using Dominatus.Core.Runtime;
using Dominatus.Llm.OptFlow;

Console.WriteLine("Dominatus.Llm Demo — Oracle Greeting");

var mode = DemoModeParser.Parse(args);
if (mode is null)
{
    PrintUsage();
    return;
}

try
{
    RunDemo(mode.Value);
}
catch (Exception ex)
{
    Console.WriteLine($"Unhandled demo failure: {ex.Message}");
    Environment.ExitCode = 1;
}

static void RunDemo(DemoMode mode)
{
    var cassette = new InMemoryLlmCassette();
    var request = OracleScenario.BuildRequest();
    var requestHash = LlmRequestHasher.ComputeHash(request);

    if (mode is DemoMode.Replay or DemoMode.Strict)
    {
        cassette.Put(requestHash, request, new LlmTextResult(OracleScenario.FakeResponse, requestHash));
    }

    var cassetteMode = mode is DemoMode.Live
        ? LlmCassetteMode.Live
        : mode is DemoMode.Record
            ? LlmCassetteMode.Record
            : mode is DemoMode.Replay
                ? LlmCassetteMode.Replay
                : LlmCassetteMode.Strict;

    RunSingleMode(mode, cassetteMode, cassette, requestHash);
}

static void RunSingleMode(DemoMode mode, LlmCassetteMode cassetteMode, InMemoryLlmCassette cassette, string requestHash)
{
    var client = new FakeLlmClient(OracleScenario.FakeResponse);
    var (world, ctx) = CreateWorldAndCtx(client, cassetteMode, cassette);

    var step = OracleScenario.BuildStep();

    try
    {
        ExecuteStep(step, ctx);

        Console.WriteLine($"Mode: {mode}");
        Console.WriteLine($"StableId: {OracleScenario.StableId}");
        Console.WriteLine($"RequestHash: {requestHash}");
        Console.WriteLine($"ProviderCalled: {client.CallCount > 0}");
        Console.WriteLine($"Oracle.Line: {ctx.Bb.GetOrDefault(OracleScenario.OracleLineKey, string.Empty)}");
    }
    catch (InvalidOperationException ex) when (mode is DemoMode.StrictMiss)
    {
        Console.WriteLine("Expected strict miss failure:");
        Console.WriteLine("  Mode: Strict");
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
    Console.WriteLine("Usage: dotnet run --project samples/Dominatus.Llm.DemoConsole -- --mode <live|record|replay|strict|strict-miss>");
}

enum DemoMode
{
    Live,
    Record,
    Replay,
    Strict,
    StrictMiss,
}

static class DemoModeParser
{
    public static DemoMode? Parse(string[] args)
    {
        if (args.Length < 2 || !string.Equals(args[0], "--mode", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return args[1].ToLowerInvariant() switch
        {
            "live" => DemoMode.Live,
            "record" => DemoMode.Record,
            "replay" => DemoMode.Replay,
            "strict" => DemoMode.Strict,
            "strict-miss" => DemoMode.StrictMiss,
            _ => null,
        };
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
