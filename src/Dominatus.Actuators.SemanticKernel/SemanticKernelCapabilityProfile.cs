namespace Dominatus.Actuators.SemanticKernel;

public enum SemanticKernelCapabilityRisk
{
    Read,
    Write,
    ExternalEffect,
    Destructive,
    Unknown
}

public sealed record SemanticKernelCapabilityProfileEntry(
    string PluginName,
    string FunctionName,
    SemanticKernelCapabilityRisk Risk,
    string? Description = null,
    bool RequiresHumanApproval = false)
{
    public string PluginName { get; } = string.IsNullOrWhiteSpace(PluginName)
        ? throw new ArgumentException("PluginName is required.", nameof(PluginName))
        : PluginName.Trim();

    public string FunctionName { get; } = string.IsNullOrWhiteSpace(FunctionName)
        ? throw new ArgumentException("FunctionName is required.", nameof(FunctionName))
        : FunctionName.Trim();
}

public sealed record SemanticKernelCapabilityProfile(
    string Id,
    string Title,
    IReadOnlyList<SemanticKernelCapabilityProfileEntry> Entries)
{
    public string Id { get; } = string.IsNullOrWhiteSpace(Id)
        ? throw new ArgumentException("Id is required.", nameof(Id))
        : Id.Trim();

    public string Title { get; } = string.IsNullOrWhiteSpace(Title)
        ? throw new ArgumentException("Title is required.", nameof(Title))
        : Title.Trim();

    public IReadOnlyList<SemanticKernelCapabilityProfileEntry> Entries { get; } = ValidateEntries(Entries);

    public IReadOnlyList<AllowedSemanticKernelFunction> ToAllowedFunctions(Func<SemanticKernelCapabilityProfileEntry, bool>? predicate = null)
    {
        IEnumerable<SemanticKernelCapabilityProfileEntry> source = Entries;
        if (predicate is not null)
            source = source.Where(predicate);

        return source
            .Select(e => new AllowedSemanticKernelFunction(e.PluginName, e.FunctionName))
            .ToArray();
    }

    private static IReadOnlyList<SemanticKernelCapabilityProfileEntry> ValidateEntries(IReadOnlyList<SemanticKernelCapabilityProfileEntry> entries)
    {
        if (entries is null)
            throw new ArgumentNullException(nameof(entries));
        if (entries.Count == 0)
            throw new ArgumentException("At least one capability profile entry is required.", nameof(entries));

        var dedupe = new HashSet<(string Plugin, string Function)>();
        foreach (var entry in entries)
        {
            if (entry is null)
                throw new ArgumentException("Capability profile entries cannot contain null values.", nameof(entries));

            var key = (entry.PluginName.Trim().ToUpperInvariant(), entry.FunctionName.Trim().ToUpperInvariant());
            if (!dedupe.Add(key))
                throw new ArgumentException("Duplicate plugin/function pair in capability profile entries.", nameof(entries));
        }

        return entries;
    }
}

public static class SemanticKernelCapabilityProfilePredicates
{
    public static bool IsReadOnly(SemanticKernelCapabilityProfileEntry entry)
        => entry.Risk == SemanticKernelCapabilityRisk.Read;

    public static bool RequiresApproval(SemanticKernelCapabilityProfileEntry entry)
        => entry.RequiresHumanApproval;

    public static bool IsWriteOrEffectful(SemanticKernelCapabilityProfileEntry entry)
        => entry.Risk is SemanticKernelCapabilityRisk.Write
            or SemanticKernelCapabilityRisk.ExternalEffect
            or SemanticKernelCapabilityRisk.Destructive;
}
