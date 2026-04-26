using System.Globalization;
using Dominatus.Core.Blackboard;
using Dominatus.Core.Runtime;
using Dominatus.Server.Dtos;

namespace Dominatus.Server.Internal;

public static class DominatusServerDtoMapper
{
    public static DominatusWorldDto ToWorldDto(AiWorld world)
        => new(world.Clock.Time, world.Agents.Count);

    public static DominatusAgentDto ToAgentDto(AiAgent agent, AgentSnapshot? snapshot)
    {
        var activePath = agent.Brain.GetActivePath().Select(state => state.ToString()).ToArray();
        var resolved = snapshot ?? new AgentSnapshot(agent.Id, Team: 0, Position: default, IsAlive: true);

        return new DominatusAgentDto(
            agent.Id.ToString(),
            resolved.Team,
            resolved.Position.X,
            resolved.Position.Y,
            resolved.Position.Z,
            resolved.IsAlive,
            activePath);
    }

    public static DominatusBlackboardDto ToBlackboardDto(Blackboard blackboard)
    {
        var entries = blackboard
            .EnumerateSnapshotEntries()
            .Select(static entry =>
            {
                var (type, value) = FormatBlackboardValue(entry.Value);
                return new DominatusBlackboardEntryDto(entry.Key, type, value, entry.ExpiresAt);
            })
            .OrderBy(static entry => entry.Key, StringComparer.Ordinal)
            .ToArray();

        return new DominatusBlackboardDto(entries);
    }

    public static DominatusAgentPathDto ToAgentPathDto(AiAgent agent)
        => new(agent.Id.ToString(), agent.Brain.GetActivePath().Select(state => state.ToString()).ToArray());

    public static DominatusAgentSnapshotDto ToAgentSnapshotDto(AgentSnapshot snapshot)
        => new(
            snapshot.Id.ToString(),
            snapshot.Team,
            snapshot.Position.X,
            snapshot.Position.Y,
            snapshot.Position.Z,
            snapshot.IsAlive);

    private static (string Type, string? Value) FormatBlackboardValue(object? value)
    {
        return value switch
        {
            null => ("null", null),
            bool b => ("bool", b ? "true" : "false"),
            int i => ("int", i.ToString(CultureInfo.InvariantCulture)),
            long l => ("long", l.ToString(CultureInfo.InvariantCulture)),
            float f => ("float", f.ToString(CultureInfo.InvariantCulture)),
            double d => ("double", d.ToString(CultureInfo.InvariantCulture)),
            string s => ("string", s),
            Guid g => ("guid", g.ToString("D", CultureInfo.InvariantCulture)),
            _ => ("unknown", value.ToString() ?? $"[unsupported:{value.GetType().FullName}]")
        };
    }
}
