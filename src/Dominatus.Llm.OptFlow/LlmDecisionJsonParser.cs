using System.Text.Json;

namespace Dominatus.Llm.OptFlow;

public static class LlmDecisionJsonParser
{
    public static LlmDecisionResult ParseAndValidate(
        string providerText,
        LlmDecisionRequest request,
        string requestHash,
        string context)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerText);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestHash);

        var decisionJson = ExtractSingleJsonObject(providerText, context);

        JsonDocument decisionDocument;
        try
        {
            decisionDocument = JsonDocument.Parse(decisionJson);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Malformed decision JSON ({context}). {ex.Message}", ex);
        }

        using (decisionDocument)
        {
            var root = decisionDocument.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidOperationException($"Decision JSON root must be an object ({context}).");
            }

            if (!root.TryGetProperty("scores", out var scoresElement) || scoresElement.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidOperationException($"Decision JSON missing required scores array ({context}).");
            }

            if (!root.TryGetProperty("rationale", out var rationaleElement) || rationaleElement.ValueKind != JsonValueKind.String)
            {
                throw new InvalidOperationException($"Decision JSON missing required rationale string ({context}).");
            }

            var overallRationale = rationaleElement.GetString() ?? string.Empty;
            var scores = new List<LlmDecisionOptionScore>();

            foreach (var scoreElement in scoresElement.EnumerateArray())
            {
                if (scoreElement.ValueKind != JsonValueKind.Object)
                {
                    throw new InvalidOperationException($"Decision score entry must be an object ({context}).");
                }

                var optionId = ReadRequiredString(scoreElement, "id", context);
                var score = ReadRequiredDouble(scoreElement, "score", context);
                var rank = ReadRequiredInt(scoreElement, "rank", context);
                var rationale = ReadRequiredString(scoreElement, "rationale", context);

                scores.Add(new LlmDecisionOptionScore(optionId, score, rank, rationale));
            }

            var result = new LlmDecisionResult(requestHash, scores, overallRationale);
            LlmDecisionResultValidator.ValidateAgainstRequest(request, requestHash, result);
            return result;
        }
    }

    public static string ExtractSingleJsonObject(string text, string context)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        var objects = FindTopLevelJsonObjects(text);
        if (objects.Count == 0)
        {
            throw new InvalidOperationException($"No decision JSON object found in provider text ({context}).");
        }

        if (objects.Count > 1)
        {
            throw new InvalidOperationException($"Multiple decision JSON objects found in provider text ({context}).");
        }

        return objects[0];
    }

    private static List<string> FindTopLevelJsonObjects(string text)
    {
        var result = new List<string>();
        var inString = false;
        var escaping = false;
        var depth = 0;
        var start = -1;

        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];

            if (inString)
            {
                if (escaping)
                {
                    escaping = false;
                    continue;
                }

                if (ch == '\\')
                {
                    escaping = true;
                    continue;
                }

                if (ch == '"')
                {
                    inString = false;
                }

                continue;
            }

            if (ch == '"')
            {
                inString = true;
                continue;
            }

            if (ch == '{')
            {
                if (depth == 0)
                {
                    start = i;
                }

                depth++;
                continue;
            }

            if (ch != '}')
            {
                continue;
            }

            if (depth == 0)
            {
                continue;
            }

            depth--;
            if (depth != 0 || start < 0)
            {
                continue;
            }

            result.Add(text.Substring(start, i - start + 1));
            start = -1;
        }

        return result;
    }

    private static string ReadRequiredString(JsonElement root, string propertyName, string context)
    {
        if (!root.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.String)
        {
            throw new InvalidOperationException($"Decision JSON missing required '{propertyName}' string ({context}).");
        }

        return value.GetString() ?? string.Empty;
    }

    private static int ReadRequiredInt(JsonElement root, string propertyName, string context)
    {
        if (!root.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var parsed))
        {
            throw new InvalidOperationException($"Decision JSON missing required '{propertyName}' integer ({context}).");
        }

        return parsed;
    }

    private static double ReadRequiredDouble(JsonElement root, string propertyName, string context)
    {
        if (!root.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out var parsed))
        {
            throw new InvalidOperationException($"Decision JSON missing required '{propertyName}' number ({context}).");
        }

        return parsed;
    }
}
