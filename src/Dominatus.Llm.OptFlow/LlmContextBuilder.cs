using System.Text;
using System.Text.Json;

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
