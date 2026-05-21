using System.Text;
using System.Text.Json;
using Dominatus.Llm.Context;

namespace Dominatus.Llm.OptFlow;

public sealed class LlmContextBuilder
{
    private readonly Dictionary<string, ContextValue> _values = new(StringComparer.Ordinal);

    public static string EmptyCanonicalJson => "{}";

    public LlmContextBuilder Add(string key, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return AddCore(key, new ContextValue(ContextValueKind.String, value));
    }

    public LlmContextBuilder Add(string key, bool value) => AddCore(key, new ContextValue(ContextValueKind.Bool, value));

    public LlmContextBuilder Add(string key, int value) => AddCore(key, new ContextValue(ContextValueKind.Int, value));

    public LlmContextBuilder Add(string key, long value) => AddCore(key, new ContextValue(ContextValueKind.Long, value));

    public LlmContextBuilder Add(string key, double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Double context values must be finite.");
        }

        return AddCore(key, new ContextValue(ContextValueKind.Double, value));
    }

    public LlmContextBuilder Add(string key, Guid value) => AddCore(key, new ContextValue(ContextValueKind.Guid, value));

    public LlmContextBuilder AddPacket(
        LlmContextPacket packet,
        string? title = null,
        bool includeManifestSummary = true)
    {
        ArgumentNullException.ThrowIfNull(packet);

        var rendered = includeManifestSummary
            ? RenderPacketWithManifestSummary(packet, title)
            : packet.Text;

        return AddCore("__contextPacket", new ContextValue(ContextValueKind.String, rendered));
    }

    public string BuildCanonicalJson()
    {
        if (_values.Count == 0)
        {
            return EmptyCanonicalJson;
        }

        var stream = new MemoryStream();
        using var writer = new Utf8JsonWriter(stream);

        writer.WriteStartObject();
        foreach (var key in _values.Keys.OrderBy(static x => x, StringComparer.Ordinal))
        {
            writer.WritePropertyName(key);
            _values[key].Write(writer);
        }

        writer.WriteEndObject();
        writer.Flush();

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private LlmContextBuilder AddCore(string key, ContextValue value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        if (!_values.TryAdd(key, value))
        {
            throw new InvalidOperationException($"Duplicate context key '{key}' is not allowed.");
        }

        return this;
    }

    private enum ContextValueKind
    {
        String,
        Bool,
        Int,
        Long,
        Double,
        Guid,
    }

    private static string RenderPacketWithManifestSummary(LlmContextPacket packet, string? title)
    {
        var headerTitle = string.IsNullOrWhiteSpace(title)
            ? packet.Provenance.LoadoutId ?? packet.StoreId
            : title;

        var sb = new StringBuilder();
        sb.AppendLine($"# Context Packet: {headerTitle}");
        sb.AppendLine();
        sb.AppendLine($"Source: {packet.Provenance.SourceKind.ToString().ToLowerInvariant()}");
        if (!string.IsNullOrWhiteSpace(packet.Provenance.LoadoutId))
        {
            sb.AppendLine($"Loadout: {packet.Provenance.LoadoutId}");
        }

        sb.AppendLine($"Store: {packet.StoreId}");
        sb.AppendLine($"CharacterCount: {packet.CharacterCount}");
        sb.AppendLine($"MaxChars: {packet.MaxChars}");
        sb.AppendLine($"RemainingChars: {packet.RemainingChars}");
        sb.AppendLine($"BudgetConstrained: {packet.WasBudgetConstrained.ToString().ToLowerInvariant()}");
        sb.AppendLine($"IncludedChunks: {string.Join(", ", packet.IncludedChunkIds)}");
        sb.AppendLine($"OmittedChunks: {packet.OmittedChunkIds.Count}");
        sb.AppendLine();
        sb.Append(packet.Text);
        return sb.ToString();
    }

    private readonly record struct ContextValue(ContextValueKind Kind, object Value)
    {
        public void Write(Utf8JsonWriter writer)
        {
            switch (Kind)
            {
                case ContextValueKind.String:
                    writer.WriteStringValue((string)Value);
                    return;
                case ContextValueKind.Bool:
                    writer.WriteBooleanValue((bool)Value);
                    return;
                case ContextValueKind.Int:
                    writer.WriteNumberValue((int)Value);
                    return;
                case ContextValueKind.Long:
                    writer.WriteNumberValue((long)Value);
                    return;
                case ContextValueKind.Double:
                    writer.WriteNumberValue((double)Value);
                    return;
                case ContextValueKind.Guid:
                    writer.WriteStringValue((Guid)Value);
                    return;
                default:
                    throw new InvalidOperationException($"Unsupported context value type '{Kind}'.");
            }
        }
    }
}
