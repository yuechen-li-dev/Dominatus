using System.Text.Json.Serialization;

namespace Dominatus.Core.Persistence;

[JsonSerializable(typeof(DominatusCheckpoint))]
[JsonSerializable(typeof(AgentCheckpoint))]
[JsonSerializable(typeof(ReplayLog))]
[JsonSerializable(typeof(ReplayEvent))]
[JsonSerializable(typeof(ReplayEvent.Advance))]
[JsonSerializable(typeof(ReplayEvent.Choice))]
[JsonSerializable(typeof(ReplayEvent.Text))]
[JsonSerializable(typeof(ReplayEvent.External))]
[JsonSerializable(typeof(ReplayEvent.RngSeed))]
[JsonSerializable(typeof(EventCursorSnapshot))]
[JsonSerializable(typeof(PendingActuation))]
[JsonSerializable(typeof(DominatusSaveMetaJson))]
internal partial class DominatusJsonContext : JsonSerializerContext;
