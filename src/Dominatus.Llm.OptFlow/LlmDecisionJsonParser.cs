using System.Text.Json;

namespace Dominatus.Llm.OptFlow;

public static class LlmDecisionJsonParser
{
    public static LlmDecisionResult ParseAndValidate(string providerText, LlmDecisionRequest request, string requestHash, string context)
    {
        var decisionJson = ExtractSingleJsonObject(providerText, context);
        JsonDocument doc;
        try { doc = JsonDocument.Parse(decisionJson); }
        catch (JsonException ex) { throw new InvalidOperationException($"Malformed decision JSON ({context}). {ex.Message}", ex); }
        using (doc)
        {
        var root = doc.RootElement;
        var outcomeText = root.TryGetProperty("outcome", out var o) && o.ValueKind == JsonValueKind.String ? o.GetString() : "chosen";
        var outcome = outcomeText?.ToLowerInvariant() switch
        {
            "chosen" => LlmDecisionOutcome.Chosen,
            "refused" => LlmDecisionOutcome.Refused,
            _ => throw new InvalidOperationException($"Decision JSON has unknown outcome '{outcomeText}' ({context}).")
        };

        var rationale = ReadRequiredString(root, "rationale", context);
        var scores = new List<LlmDecisionOptionScore>();
        foreach (var s in root.GetProperty("scores").EnumerateArray())
        {
            scores.Add(new LlmDecisionOptionScore(ReadRequiredString(s, "id", context), ReadRequiredDouble(s, "score", context), ReadRequiredInt(s, "rank", context), ReadRequiredString(s, "rationale", context)));
        }

        LlmDecisionRefusal? refusal = null;
        if (outcome == LlmDecisionOutcome.Refused)
        {
            if (!root.TryGetProperty("refusal", out var r) || r.ValueKind != JsonValueKind.Object)
                throw new InvalidOperationException($"Decision JSON refusal is required for refused outcome ({context}).");
            var reason = ReadRequiredString(r, "reason", context);
            string? alt = r.TryGetProperty("proposedAlternative", out var a) && a.ValueKind == JsonValueKind.String ? a.GetString() : null;
            refusal = new LlmDecisionRefusal(reason, alt);
        }

        var result = new LlmDecisionResult(requestHash, scores, rationale, outcome, refusal);
        LlmDecisionResultValidator.ValidateAgainstRequest(request, requestHash, result);
        return result;
        }
    }
    public static string ExtractSingleJsonObject(string text, string context){ArgumentException.ThrowIfNullOrWhiteSpace(text);var objects=FindTopLevelJsonObjects(text);if(objects.Count==0)throw new InvalidOperationException($"No decision JSON object found in provider text ({context}).");if(objects.Count>1)throw new InvalidOperationException($"Multiple decision JSON objects found in provider text ({context}).");return objects[0];}
    private static List<string> FindTopLevelJsonObjects(string text){var result=new List<string>();var inString=false;var escaping=false;var depth=0;var start=-1;for(var i=0;i<text.Length;i++){var ch=text[i];if(inString){if(escaping){escaping=false;continue;}if(ch=='\\'){escaping=true;continue;}if(ch=='"'){inString=false;}continue;}if(ch=='"'){inString=true;continue;}if(ch=='{'){if(depth==0){start=i;}depth++;continue;}if(ch!='}')continue;if(depth==0)continue;depth--;if(depth!=0||start<0)continue;result.Add(text.Substring(start,i-start+1));start=-1;}return result;}
    private static string ReadRequiredString(JsonElement root,string propertyName,string context){if(!root.TryGetProperty(propertyName,out var value)||value.ValueKind!=JsonValueKind.String)throw new InvalidOperationException($"Decision JSON missing required '{propertyName}' string ({context}).");return value.GetString()??string.Empty;}
    private static int ReadRequiredInt(JsonElement root,string propertyName,string context){if(!root.TryGetProperty(propertyName,out var value)||value.ValueKind!=JsonValueKind.Number||!value.TryGetInt32(out var parsed))throw new InvalidOperationException($"Decision JSON missing required '{propertyName}' integer ({context}).");return parsed;}
    private static double ReadRequiredDouble(JsonElement root,string propertyName,string context){if(!root.TryGetProperty(propertyName,out var value)||value.ValueKind!=JsonValueKind.Number||!value.TryGetDouble(out var parsed))throw new InvalidOperationException($"Decision JSON missing required '{propertyName}' number ({context}).");return parsed;}
}
