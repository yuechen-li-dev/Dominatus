using System.Text.Json;

namespace Dominatus.Template.LlmPrReview;

public static class PrReviewResultParser
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static PrReviewResult Parse(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        var json = ExtractJsonObject(text);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        var verdictText = RequiredString(root, "verdict");
        var verdict = ParseVerdict(verdictText);
        var summary = OptionalString(root, "summary") ?? verdict.ToString();
        var issues = ParseIssues(root);
        var notes = ParseNotes(root);

        if (verdict == PrReviewVerdict.Fail && issues.Count == 0)
        {
            issues.Add(new PrReviewIssue(PrReviewIssueSeverity.Blocking, "unknown", summary, "The model returned FAIL without structured issue details."));
        }

        return new PrReviewResult(verdict, summary, issues, notes);
    }

    public static string ToJson(PrReviewResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return JsonSerializer.Serialize(new
        {
            verdict = FormatVerdict(result.Verdict),
            result.Summary,
            issues = result.Issues.Select(issue => new
            {
                severity = issue.Severity.ToString(),
                issue.File,
                issue.Summary,
                issue.Rationale
            }),
            nonBlockingNotes = result.NonBlockingNotes
        }, JsonOptions);
    }

    private static List<PrReviewIssue> ParseIssues(JsonElement root)
    {
        var issues = new List<PrReviewIssue>();
        if (!TryGetPropertyIgnoreCase(root, "issues", out var array) || array.ValueKind != JsonValueKind.Array)
        {
            return issues;
        }

        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var severity = ParseSeverity(OptionalString(item, "severity") ?? "Note");
            var file = OptionalString(item, "file") ?? "unknown";
            var summary = OptionalString(item, "summary") ?? "No summary provided.";
            var rationale = OptionalString(item, "rationale") ?? string.Empty;
            issues.Add(new PrReviewIssue(severity, file, summary, rationale));
        }

        return issues;
    }

    private static List<string> ParseNotes(JsonElement root)
    {
        var notes = new List<string>();
        if (!TryGetPropertyIgnoreCase(root, "nonBlockingNotes", out var array) || array.ValueKind != JsonValueKind.Array)
        {
            return notes;
        }

        notes.AddRange(array.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString())
            .Where(note => !string.IsNullOrWhiteSpace(note))!);
        return notes;
    }

    private static string ExtractJsonObject(string text)
    {
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end <= start)
        {
            throw new JsonException("LLM response did not contain a JSON object.");
        }

        return text[start..(end + 1)];
    }

    private static string RequiredString(JsonElement element, string name)
        => OptionalString(element, name) ?? throw new JsonException($"Missing required string property '{name}'.");

    private static string? OptionalString(JsonElement element, string name)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)
                && property.Value.ValueKind == JsonValueKind.String)
            {
                return property.Value.GetString();
            }
        }

        return null;
    }

    private static bool TryGetPropertyIgnoreCase(JsonElement element, string name, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static PrReviewVerdict ParseVerdict(string value)
    {
        var normalized = value.Replace("_", string.Empty, StringComparison.Ordinal).Replace("-", string.Empty, StringComparison.Ordinal);
        return Enum.TryParse<PrReviewVerdict>(normalized, ignoreCase: true, out var verdict)
            ? verdict
            : throw new JsonException($"Unknown PR review verdict '{value}'.");
    }

    private static string FormatVerdict(PrReviewVerdict verdict)
        => verdict == PrReviewVerdict.NeedsHuman ? "NEEDS_HUMAN" : verdict.ToString().ToUpperInvariant();

    private static PrReviewIssueSeverity ParseSeverity(string value)
        => Enum.TryParse<PrReviewIssueSeverity>(value, ignoreCase: true, out var severity) ? severity : PrReviewIssueSeverity.Note;
}
