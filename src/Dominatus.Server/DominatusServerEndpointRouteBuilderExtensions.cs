using Dominatus.Core.Runtime;
using Dominatus.Server.Dtos;
using Dominatus.Server.Internal;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Dominatus.Server;

public static class DominatusServerEndpointRouteBuilderExtensions
{
    public static IEndpointRouteBuilder MapDominatusServer(this IEndpointRouteBuilder endpoints, string prefix = "/dominatus")
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var runtime = endpoints.ServiceProvider.GetService<DominatusServerRuntime>()
            ?? throw new InvalidOperationException("DominatusServerRuntime is not registered. Call AddDominatusServer(...) first.");

        var streamRegistry = endpoints.ServiceProvider.GetService<DominatusLlmStreamRegistry>()
            ?? throw new InvalidOperationException("DominatusLlmStreamRegistry is not registered. Call AddDominatusServer(...) first.");

        var group = endpoints.MapGroup(prefix);

        group.MapGet("/health", static () => Results.Ok(new DominatusHealthDto("ok")));
        group.MapGet("/world", () => Results.Ok(runtime.Read(DominatusServerDtoMapper.ToWorldDto)));
        group.MapGet("/world/blackboard", () => Results.Ok(runtime.Read(world => DominatusServerDtoMapper.ToBlackboardDto(world.Bb))));
        group.MapGet("/agents", () => Results.Ok(runtime.Read(GetAgents)));

        group.MapGet("/agents/{id}", (string id) => runtime.Read(world =>
        {
            if (!TryGetAgent(world, id, out var agent))
                return Results.NotFound();

            AgentSnapshot? snapshot = world.TryGetPublic(agent.Id, out var resolved) ? resolved : null;
            return Results.Ok(DominatusServerDtoMapper.ToAgentDto(agent, snapshot));
        }));

        group.MapGet("/agents/{id}/blackboard", (string id) => runtime.Read(world =>
        {
            if (!TryGetAgent(world, id, out var agent))
                return Results.NotFound();

            return Results.Ok(DominatusServerDtoMapper.ToBlackboardDto(agent.Bb));
        }));

        group.MapGet("/agents/{id}/path", (string id) => runtime.Read(world =>
        {
            if (!TryGetAgent(world, id, out var agent))
                return Results.NotFound();

            return Results.Ok(DominatusServerDtoMapper.ToAgentPathDto(agent));
        }));

        group.MapGet("/agents/{id}/snapshot", (string id) => runtime.Read(world =>
        {
            if (!TryGetAgent(world, id, out var agent))
                return Results.NotFound();

            return world.TryGetPublic(agent.Id, out var snapshot)
                ? Results.Ok(DominatusServerDtoMapper.ToAgentSnapshotDto(snapshot))
                : Results.NotFound();
        }));


        group.MapGet("/streams", () => Results.Ok(streamRegistry.ListStreams()));

        group.MapGet("/streams/{streamId}", (string streamId) =>
        {
            if (string.IsNullOrWhiteSpace(streamId))
                return Results.BadRequest("streamId must be non-empty.");

            var detail = streamRegistry.GetStream(streamId);
            return detail is null ? Results.NotFound() : Results.Ok(detail);
        });

        group.MapGet("/streams/{streamId}/chunks", (string streamId, int? after) =>
        {
            if (string.IsNullOrWhiteSpace(streamId))
                return Results.BadRequest("streamId must be non-empty.");

            var afterValue = after ?? -1;
            if (afterValue < -1)
                return Results.BadRequest("after must be greater than or equal to -1.");

            if (streamRegistry.GetStream(streamId) is null)
                return Results.NotFound();

            return Results.Ok(streamRegistry.GetChunks(streamId, afterValue));
        });

        group.MapGet("/snapshots", () => Results.Ok(runtime.Read(world =>
            world.Agents
                .Select(agent => world.TryGetPublic(agent.Id, out var snapshot)
                    ? DominatusServerDtoMapper.ToAgentSnapshotDto(snapshot)
                    : null)
                .Where(static snapshot => snapshot is not null)
                .Cast<DominatusAgentSnapshotDto>()
                .OrderBy(snapshot => snapshot.AgentId, StringComparer.Ordinal)
                .ToArray())));

        return endpoints;
    }

    private static IReadOnlyList<DominatusAgentDto> GetAgents(AiWorld world)
        => world.Agents
            .Select(agent =>
            {
                AgentSnapshot? snapshot = world.TryGetPublic(agent.Id, out var resolved) ? resolved : null;
                return DominatusServerDtoMapper.ToAgentDto(agent, snapshot);
            })
            .OrderBy(dto => dto.Id, StringComparer.Ordinal)
            .ToArray();

    private static bool TryGetAgent(AiWorld world, string id, out AiAgent agent)
    {
        foreach (var candidate in world.Agents)
        {
            if (string.Equals(candidate.Id.ToString(), id, StringComparison.Ordinal))
            {
                agent = candidate;
                return true;
            }
        }

        agent = null!;
        return false;
    }
}
