using Dominatus.Actuators.Standard;
using Dominatus.Core.Hfsm;
using Dominatus.Core.Nodes;
using Dominatus.Core.Nodes.Steps;
using Dominatus.Core.Runtime;
using System.Net;
using System.Net.Http;
using System.Text;

Console.WriteLine("Dominatus.Actuators.Standard package smoke");

var workspace = Path.Combine(Path.GetTempPath(), "dominatus-package-smoke", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(workspace);

try
{
    var fixedUtc = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);
    var fixedLocal = fixedUtc.ToOffset(TimeSpan.FromHours(-5));

    var host = new ActuatorHost();
    host.RegisterStandardFileActuators(new SandboxedFileActuatorOptions
    {
        Roots = [new SandboxedFileRoot("workspace", workspace)]
    });
    host.RegisterStandardTimeActuators(new SmokeClock(fixedUtc, fixedLocal));
    host.RegisterStandardHttpActuators(
        new HttpActuatorOptions
        {
            Endpoints = [new AllowedHttpEndpoint("smoke", new Uri("https://example.test/api/"))]
        },
        new FakeHttpMessageHandler());

    var ctx = NewCtx(host);

    var write = host.Dispatch(ctx, new WriteTextFileCommand("workspace", "notes/status.txt", "ready"));
    Require(write.Ok, "Expected file write command to succeed.");

    var read = host.Dispatch(ctx, new ReadTextFileCommand("workspace", "notes/status.txt"));
    Require(read.Ok, "Expected file read command to succeed.");
    Require(Equals(read.Payload, "ready"), "Expected file read payload to match written text.");
    Console.WriteLine("File write/read: OK");

    var exists = host.Dispatch(ctx, new FileExistsCommand("workspace", "notes/status.txt"));
    Require(exists.Ok, "Expected file exists command to succeed.");
    Require(exists.Payload is bool value && value, "Expected file exists payload to be true.");
    Console.WriteLine("File exists: OK");

    var now = host.Dispatch(ctx, new GetUtcNowCommand());
    Require(now.Ok, "Expected UTC time command to succeed.");
    Require(now.Payload is TimeResult tr && tr.Timestamp == fixedUtc, "Expected UTC time payload to match injected clock.");
    Console.WriteLine("Time: OK");

    var http = host.Dispatch(ctx, new HttpGetTextCommand("smoke", "status"));
    Require(http.Ok, "Expected HTTP command to succeed.");
    Require(http.Payload is HttpTextResult, "Expected HTTP payload type.");
    var httpResult = (HttpTextResult)http.Payload!;
    Require(httpResult.StatusCode == (int)HttpStatusCode.OK, "Expected HTTP status 200.");
    Require(httpResult.Text == "{\"ok\":true}", "Expected HTTP response text from fake transport.");
    Require(httpResult.Headers.TryGetValue("X-Smoke", out var smokeHeader) && smokeHeader.Count > 0 && smokeHeader[0] == "yes",
        "Expected X-Smoke header from fake transport.");
    Console.WriteLine("HTTP fake transport: OK");

    Console.WriteLine("Package smoke: OK");
    return 0;
}
finally
{
    if (Directory.Exists(workspace))
        Directory.Delete(workspace, recursive: true);
}

static void Require(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

static AiCtx NewCtx(ActuatorHost host)
{
    var world = new AiWorld(host);
    var graph = new HfsmGraph { Root = "Root" };
    graph.Add(new HfsmStateDef { Id = "Root", Node = static _ => Idle() });

    var agent = new AiAgent(new HfsmInstance(graph));
    world.Add(agent);
    return new AiCtx(world, agent, agent.Events, CancellationToken.None, world.View, world.Mail, world.Actuator);

    static IEnumerator<AiStep> Idle()
    {
        yield break;
    }
}

file sealed record SmokeClock(DateTimeOffset UtcNow, DateTimeOffset LocalNow) : IStandardSystemClock;

file sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.RequestUri is null)
            throw new InvalidOperationException("Request URI is required.");

        if (request.Method != HttpMethod.Get)
            throw new InvalidOperationException("Smoke transport expected GET request.");

        if (request.RequestUri.ToString() != "https://example.test/api/status")
            throw new InvalidOperationException($"Unexpected smoke request URI: {request.RequestUri}");

        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"ok\":true}", Encoding.UTF8, "application/json")
        };

        response.Headers.TryAddWithoutValidation("X-Smoke", "yes");
        return Task.FromResult(response);
    }
}
