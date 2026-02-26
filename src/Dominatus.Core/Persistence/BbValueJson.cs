using System.Text.Json;

namespace Dominatus.Core.Persistence;

public sealed record BbValueJson(string t, JsonElement v);