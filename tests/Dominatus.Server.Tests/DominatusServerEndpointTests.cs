using System.Net;
using System.Net.Http.Json;
using Dominatus.Core;
using Dominatus.Core.Blackboard;
using Dominatus.Core.Hfsm;
using Dominatus.Core.Nodes;
using Dominatus.Core.Nodes.Steps;
using Dominatus.Core.Runtime;
using Dominatus.Server;
using Dominatus.Server.Dtos;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;

namespace Dominatus.Server.Tests;

public class DominatusServerEndpointTests
{
    [Fact]
    public async Task GET_health_ReturnsOk()
    {
        await using var app = await CreateAppAsync();

        var response = await app.Client.GetAsync("/dominatus/health");
        var dto = await response.Content.ReadFromJsonAsync<DominatusHealthDto>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("ok", dto?.Status);
    }

    [Fact]
    public async Task GET_world_ReturnsTimeAndAgentCount()
    {
        await using var app = await CreateAppAsync();

        var dto = await app.Client.GetFromJsonAsync<DominatusWorldDto>("/dominatus/world");

        Assert.NotNull(dto);
        Assert.Equal(1, dto.AgentCount);
        Assert.True(dto.TimeSeconds > 0f);
    }

    [Fact]
    public async Task GET_worldBlackboard_ReturnsEntries()
    {
        await using var app = await CreateAppAsync();

        var dto = await app.Client.GetFromJsonAsync<DominatusBlackboardDto>("/dominatus/world/blackboard");

        Assert.NotNull(dto);
        Assert.Single(dto.Entries);
        Assert.Equal("weather", dto.Entries[0].Key);
    }

    [Fact]
    public async Task GET_agents_ReturnsAgentList()
    {
        await using var app = await CreateAppAsync();

        var dto = await app.Client.GetFromJsonAsync<List<DominatusAgentDto>>("/dominatus/agents");

        Assert.NotNull(dto);
        Assert.Single(dto);
        Assert.Equal("1", dto[0].Id);
    }

    [Fact]
    public async Task GET_agent_ReturnsAgent()
    {
        await using var app = await CreateAppAsync();

        var dto = await app.Client.GetFromJsonAsync<DominatusAgentDto>("/dominatus/agents/1");

        Assert.NotNull(dto);
        Assert.Equal("1", dto.Id);
        Assert.Equal(new[] { "Root", "Patrol" }, dto.ActivePath);
    }

    [Fact]
    public async Task GET_missingAgent_Returns404()
    {
        await using var app = await CreateAppAsync();

        var response = await app.Client.GetAsync("/dominatus/agents/999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GET_agentBlackboard_ReturnsEntries()
    {
        await using var app = await CreateAppAsync();

        var dto = await app.Client.GetFromJsonAsync<DominatusBlackboardDto>("/dominatus/agents/1/blackboard");

        Assert.NotNull(dto);
        Assert.Single(dto.Entries);
        Assert.Equal("mood", dto.Entries[0].Key);
    }

    [Fact]
    public async Task GET_agentPath_ReturnsActivePath()
    {
        await using var app = await CreateAppAsync();

        var dto = await app.Client.GetFromJsonAsync<DominatusAgentPathDto>("/dominatus/agents/1/path");

        Assert.NotNull(dto);
        Assert.Equal("1", dto.AgentId);
        Assert.Equal(new[] { "Root", "Patrol" }, dto.ActivePath);
    }

    [Fact]
    public async Task GET_agentSnapshot_ReturnsSnapshot()
    {
        await using var app = await CreateAppAsync();

        var dto = await app.Client.GetFromJsonAsync<DominatusAgentSnapshotDto>("/dominatus/agents/1/snapshot");

        Assert.NotNull(dto);
        Assert.Equal("1", dto.AgentId);
        Assert.Equal(7, dto.Team);
        Assert.False(dto.IsAlive);
    }

    [Fact]
    public async Task MapDominatusServer_UsesCustomPrefix()
    {
        await using var app = await CreateAppAsync("/inspect");

        var custom = await app.Client.GetAsync("/inspect/health");
        var defaultPrefix = await app.Client.GetAsync("/dominatus/health");

        Assert.Equal(HttpStatusCode.OK, custom.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, defaultPrefix.StatusCode);
    }

    private static async Task<TestApp> CreateAppAsync(string prefix = "/dominatus")
    {
        var world = new AiWorld();
        world.Bb.Set(new BbKey<string>("weather"), "clear");

        var agent = new AiAgent(CreateBrain());
        world.Add(agent);
        agent.Bb.SetUntil(new BbKey<string>("mood"), "calm", expiresAt: 5f);
        world.SetPublic(agent.Id, new AgentSnapshot(agent.Id, Team: 7, Position: new(10, 20, 30), IsAlive: false));
        world.Tick(0.1f);

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddDominatusServer(new DominatusServerRuntime(world));

        var app = builder.Build();
        app.MapDominatusServer(prefix);
        await app.StartAsync();

        return new TestApp(app, app.GetTestClient());
    }

    private sealed record TestApp(WebApplication App, HttpClient Client) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync() => await App.DisposeAsync();
    }

    private static HfsmInstance CreateBrain()
    {
        var graph = new HfsmGraph { Root = "Root" };
        graph.Add(new HfsmStateDef
        {
            Id = "Root",
            Node = static _ => RootNode()
        });
        graph.Add(new HfsmStateDef
        {
            Id = "Patrol",
            Node = static _ => PatrolNode()
        });

        return new HfsmInstance(graph);

        static IEnumerator<AiStep> RootNode()
        {
            yield return new Push("Patrol", "start");
            while (true)
                yield return new WaitSeconds(999f);
        }

        static IEnumerator<AiStep> PatrolNode()
        {
            while (true)
                yield return new WaitSeconds(999f);
        }
    }
}
