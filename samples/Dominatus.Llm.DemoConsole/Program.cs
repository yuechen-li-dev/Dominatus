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
    var cassetteMode = ToCassetteMode(options.Mode);
    var factoryResult = LlmProviderClientFactory.Create(new LlmProviderClientFactoryOptions(
        Client: options.Client,
        CassetteMode: cassetteMode,
        ModelOverride: options.Model));

    var sampling = new LlmSamplingOptions(
        Provider: factoryResult.Provider,
        Model: factoryResult.Model,
        Temperature: 0.0,
        MaxOutputTokens: 64,
        TopP: 1.0);

    var narrationRequest = NarrationScenario.BuildRequest(sampling);
    var narrationRequestHash = LlmRequestHasher.ComputeHash(narrationRequest);
    var oracleLineRequest = OracleLineScenario.BuildRequest(sampling);
    var oracleLineRequestHash = LlmRequestHasher.ComputeHash(oracleLineRequest);
    var oracleReplyRequest = OracleReplyScenario.BuildRequest(sampling);
    var oracleReplyRequestHash = LlmRequestHasher.ComputeHash(oracleReplyRequest);
    var cassette = CreateCassette(
        options,
        narrationRequestHash,
        narrationRequest,
        oracleLineRequestHash,
        oracleLineRequest,
        oracleReplyRequestHash,
        oracleReplyRequest);

    var countingClient = new CountingLlmClient(factoryResult.Client);
    var (world, ctx) = CreateWorldAndCtx(countingClient, cassetteMode, cassette);

    var narrationStep = NarrationScenario.BuildStep(sampling);
    var oracleLineStep = OracleLineScenario.BuildStep(sampling);
    var oracleReplyStep = OracleReplyScenario.BuildStep(sampling);

    try
    {
        ExecuteStep(narrationStep, ctx);
        ExecuteStep(oracleLineStep, ctx);
        ExecuteStep(oracleReplyStep, ctx);

        Console.WriteLine($"Client: {options.Client.ToString().ToLowerInvariant()}");
        Console.WriteLine($"Mode: {options.Mode.ToString().ToLowerInvariant()}");
        Console.WriteLine($"Model: {factoryResult.Model}");
        Console.WriteLine($"CassettePath: {options.CassettePath ?? "<in-memory>"}");
        Console.WriteLine($"StableId: {OracleReplyScenario.StableId}");
        Console.WriteLine($"RequestHash: {oracleReplyRequestHash}");
        Console.WriteLine($"ProviderCalled: {(countingClient.CallCount > 0).ToString().ToLowerInvariant()}");
        Console.WriteLine($"ApiKeyPresent: {factoryResult.ApiKeyPresent.ToString().ToLowerInvariant()}");
        Console.WriteLine($"Narration.Text: {ctx.Bb.GetOrDefault(NarrationScenario.NarrationKey, string.Empty)}");
        Console.WriteLine($"Oracle.Line: {ctx.Bb.GetOrDefault(OracleLineScenario.OracleLineKey, string.Empty)}");
        Console.WriteLine($"Oracle.Reply: {ctx.Bb.GetOrDefault(OracleReplyScenario.OracleReplyKey, string.Empty)}");
    }
    catch (InvalidOperationException ex) when (options.Mode is DemoMode.StrictMiss)
    {
        Console.WriteLine("Expected strict miss failure:");
        Console.WriteLine("  Mode: Strict");
        Console.WriteLine($"  CassettePath: {options.CassettePath ?? "<in-memory>"}");
        Console.WriteLine($"  NarrationStableId: {NarrationScenario.StableId}");
        Console.WriteLine($"  NarrationRequestHash: {narrationRequestHash}");
        Console.WriteLine($"  OracleLineStableId: {OracleLineScenario.StableId}");
        Console.WriteLine($"  OracleLineRequestHash: {oracleLineRequestHash}");
        Console.WriteLine($"  OracleReplyStableId: {OracleReplyScenario.StableId}");
        Console.WriteLine($"  OracleReplyRequestHash: {oracleReplyRequestHash}");
        Console.WriteLine($"  ProviderCalled: {(countingClient.CallCount > 0).ToString().ToLowerInvariant()}");
        Console.WriteLine($"  ApiKeyPresent: {factoryResult.ApiKeyPresent.ToString().ToLowerInvariant()}");
        Console.WriteLine($"  Reason: {ex.Message}");
    }

    if (options.Mode is DemoMode.Record && cassette is JsonLlmCassette jsonCassette)
    {
        jsonCassette.Save();
    }

    GC.KeepAlive(world);
}

static LlmCassetteMode ToCassetteMode(DemoMode mode)
    => mode is DemoMode.Live
        ? LlmCassetteMode.Live
        : mode is DemoMode.Record
            ? LlmCassetteMode.Record
            : mode is DemoMode.Replay
                ? LlmCassetteMode.Replay
                : LlmCassetteMode.Strict;

static ILlmCassette CreateCassette(
    DemoOptions options,
    string narrationRequestHash,
    LlmTextRequest narrationRequest,
    string oracleLineRequestHash,
    LlmTextRequest oracleLineRequest,
    string oracleReplyRequestHash,
    LlmTextRequest oracleReplyRequest)
{
    if (!string.IsNullOrWhiteSpace(options.CassettePath))
    {
        return JsonLlmCassette.LoadOrCreate(options.CassettePath);
    }

    var cassette = new InMemoryLlmCassette();

    if (options.Client is LlmProviderClientKind.Fake && options.Mode is DemoMode.Replay or DemoMode.Strict)
    {
        cassette.Put(narrationRequestHash, narrationRequest, new LlmTextResult(NarrationScenario.FakeResponse, narrationRequestHash));
        cassette.Put(oracleLineRequestHash, oracleLineRequest, new LlmTextResult(OracleLineScenario.FakeResponse, oracleLineRequestHash));
        cassette.Put(oracleReplyRequestHash, oracleReplyRequest, new LlmTextResult(OracleReplyScenario.FakeResponse, oracleReplyRequestHash));
    }

    return cassette;
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
    Console.WriteLine("Usage: dotnet run --project samples/Dominatus.Llm.DemoConsole -- --mode <live|record|replay|strict|strict-miss> [--client <fake|openai|anthropic|gemini>] [--model <name>] [--cassette <path>]");
}

enum DemoMode
{
    Live,
    Record,
    Replay,
    Strict,
    StrictMiss,
}

sealed record DemoOptions(DemoMode Mode, LlmProviderClientKind Client, string? Model, string? CassettePath);

static class DemoOptionsParser
{
    public static DemoOptions? Parse(string[] args)
    {
        string? modeValue = null;
        string? cassettePath = null;
        string? clientValue = null;
        string? model = null;

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
                continue;
            }

            if (string.Equals(arg, "--client", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                clientValue = args[++i];
                continue;
            }

            if (string.Equals(arg, "--model", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                model = args[++i];
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

        var client = (clientValue ?? "fake").ToLowerInvariant() switch
        {
            "fake" => LlmProviderClientKind.Fake,
            "openai" => LlmProviderClientKind.OpenAi,
            "anthropic" => LlmProviderClientKind.Anthropic,
            "gemini" => LlmProviderClientKind.Gemini,
            _ => (LlmProviderClientKind?)null,
        };

        if (mode is null || client is null)
        {
            return null;
        }

        return new DemoOptions(mode.Value, client.Value, model, cassettePath);
    }
}

static class OracleLineScenario
{
    public static readonly BbKey<string> OracleLineKey = new("oracle.line");

    public const string StableId = "demo.oracle.greeting.v1";
    public const string Speaker = "Oracle";
    public const string Intent = "greet the player at the shrine";
    public const string Persona = "Ancient oracle. Warm, cryptic, concise.";
    public const string FakeResponse = "Mira, the moonlit shrine remembers your footsteps before you make them.";

    public static AiStep BuildStep(LlmSamplingOptions sampling)
        => Llm.Line(
            stableId: StableId,
            speaker: Speaker,
            intent: Intent,
            persona: Persona,
            context: ctx => ctx
                .Add("playerName", "Mira")
                .Add("location", "moonlit shrine")
                .Add("oracleMood", "pleased but ominous"),
            storeAs: OracleLineKey,
            sampling: sampling);

    public static LlmTextRequest BuildRequest(LlmSamplingOptions sampling)
    {
        var context = new LlmContextBuilder()
            .Add(Llm.LineSpeakerContextKey, Speaker)
            .Add("playerName", "Mira")
            .Add("location", "moonlit shrine")
            .Add("oracleMood", "pleased but ominous")
            .BuildCanonicalJson();

        return new LlmTextRequest(
            StableId: StableId,
            Intent: Intent,
            Persona: Persona,
            CanonicalContextJson: context,
            Sampling: sampling,
            PromptTemplateVersion: LlmTextRequest.DefaultPromptTemplateVersion,
            OutputContractVersion: LlmTextRequest.DefaultOutputContractVersion);
    }
}

static class OracleReplyScenario
{
    public static readonly BbKey<string> OracleReplyKey = new("oracle.reply");

    public const string StableId = "demo.oracle.reply.v1";
    public const string Speaker = "Oracle";
    public const string Intent = "answer the player's question about the moonlit shrine";
    public const string Persona = "Ancient oracle. Warm, cryptic, concise. Knows omens, avoids direct prophecy.";
    public const string Input = "Why did the shrine remember me?";
    public const string FakeResponse = "Because some thresholds remember the souls brave enough to cross them.";

    public static AiStep BuildStep(LlmSamplingOptions sampling)
        => Llm.Reply(
            stableId: StableId,
            speaker: Speaker,
            intent: Intent,
            persona: Persona,
            input: Input,
            context: ctx => ctx
                .Add("playerName", "Mira")
                .Add("location", "moonlit shrine")
                .Add("oracleMood", "pleased but ominous"),
            storeAs: OracleReplyKey,
            sampling: sampling);

    public static LlmTextRequest BuildRequest(LlmSamplingOptions sampling)
    {
        var context = new LlmContextBuilder()
            .Add(Llm.ReplySpeakerContextKey, Speaker)
            .Add(Llm.ReplyInputContextKey, Input)
            .Add("playerName", "Mira")
            .Add("location", "moonlit shrine")
            .Add("oracleMood", "pleased but ominous")
            .BuildCanonicalJson();

        return new LlmTextRequest(
            StableId: StableId,
            Intent: Intent,
            Persona: Persona,
            CanonicalContextJson: context,
            Sampling: sampling,
            PromptTemplateVersion: LlmTextRequest.DefaultPromptTemplateVersion,
            OutputContractVersion: LlmTextRequest.DefaultOutputContractVersion);
    }
}

static class NarrationScenario
{
    public static readonly BbKey<string> NarrationKey = new("narration.text");

    public const string StableId = "demo.shrine.arrival.narration.v1";
    public const string Intent = "describe the player arriving at the moonlit shrine";
    public const string Narrator = "Narrator";
    public const string Style = "Ominous, concise, sensory, second-person.";
    public const string FakeResponse = "The moonlight pools over the shrine stones as Mira approaches.";

    public static AiStep BuildStep(LlmSamplingOptions sampling)
        => Llm.Narrate(
            stableId: StableId,
            intent: Intent,
            narrator: Narrator,
            style: Style,
            context: ctx => ctx
                .Add("playerName", "Mira")
                .Add("location", "moonlit shrine")
                .Add("oracleMood", "pleased but ominous"),
            storeAs: NarrationKey,
            sampling: sampling);

    public static LlmTextRequest BuildRequest(LlmSamplingOptions sampling)
    {
        var context = new LlmContextBuilder()
            .Add(Llm.NarrateNarratorContextKey, Narrator)
            .Add(Llm.NarrateStyleContextKey, Style)
            .Add("playerName", "Mira")
            .Add("location", "moonlit shrine")
            .Add("oracleMood", "pleased but ominous")
            .BuildCanonicalJson();

        return new LlmTextRequest(
            StableId: StableId,
            Intent: Intent,
            Persona: $"Narrator: {Narrator}\nNarration style: {Style}",
            CanonicalContextJson: context,
            Sampling: sampling,
            PromptTemplateVersion: LlmTextRequest.DefaultPromptTemplateVersion,
            OutputContractVersion: LlmTextRequest.DefaultOutputContractVersion);
    }
}

sealed class CountingLlmClient : ILlmClient
{
    private readonly ILlmClient _inner;

    public int CallCount { get; private set; }

    public CountingLlmClient(ILlmClient inner)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    }

    public async Task<LlmTextResult> GenerateTextAsync(LlmTextRequest request, string requestHash, CancellationToken cancellationToken)
    {
        CallCount++;
        return await _inner.GenerateTextAsync(request, requestHash, cancellationToken).ConfigureAwait(false);
    }
}
