namespace Dominatus.Actuators.Standard;

public sealed record HttpTextResult(
    int StatusCode,
    bool IsSuccessStatusCode,
    string Text,
    IReadOnlyDictionary<string, IReadOnlyList<string>> Headers);
