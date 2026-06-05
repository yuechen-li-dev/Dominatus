using Ariadne.OptFlow.Commands;
using Dominatus.Assets.Toml;

namespace Dominatus.Assets.Toml.AriadneDialogue;

public sealed record DialogueAddress(AssetId AssetId, string NodeId)
{
    public override string ToString() => $"{AssetId}:{NodeId}";

    public static DialogueAddress Parse(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var separator = value.IndexOf(':', StringComparison.Ordinal);
        if (separator <= 0 || separator == value.Length - 1)
        {
            throw new ArgumentException("Dialogue addresses must use 'asset_id:node_id'.", nameof(value));
        }

        return new DialogueAddress(new AssetId(value[..separator]), value[(separator + 1)..]);
    }
}

public sealed record DialogueRuntimeLine(string Speaker, LocalizationKey? Key, string Text, string? FallbackText);

public sealed record DialogueRuntimeChoice
{
    public required string Id { get; init; }

    public required DialogueRuntimeLine Line { get; init; }

    public required DialogueAddress Target { get; init; }

    public string? Condition { get; init; }

    public required IReadOnlyList<DialogueEffectAsset> Effects { get; init; }

    public DiagChoice ToAriadneChoice() => new(Id, Line.Text);
}

public sealed record DialogueRuntimeNode
{
    public required DialogueAddress Address { get; init; }

    public required DialogueRuntimeLine Line { get; init; }

    public string? Condition { get; init; }

    public required IReadOnlyList<DialogueEffectAsset> Effects { get; init; }

    public required IReadOnlyList<DialogueRuntimeChoice> Choices { get; init; }
}

public sealed record DialogueRuntimeGraph
{
    public required IReadOnlyDictionary<DialogueAddress, DialogueRuntimeNode> Nodes { get; init; }

    public required IReadOnlyDictionary<AssetId, DialogueAddress> Starts { get; init; }

    public bool TryGetNode(DialogueAddress address, out DialogueRuntimeNode node) => Nodes.TryGetValue(address, out node!);
}

public sealed class DialogueRuntimeContext
{
    public Dictionary<string, object?> State { get; } = new(StringComparer.Ordinal);
}

public sealed class DialogueConditionRegistry
{
    private readonly Dictionary<string, Func<DialogueRuntimeContext, bool>> _handlers = new(StringComparer.Ordinal);

    public void Register(string id, Func<DialogueRuntimeContext, bool> handler)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(handler);
        _handlers[id] = handler;
    }

    public bool Contains(string id) => _handlers.ContainsKey(id);

    public bool Evaluate(string id, DialogueRuntimeContext context)
    {
        if (!_handlers.TryGetValue(id, out var handler))
        {
            throw new InvalidOperationException($"Dialogue condition '{id}' is not registered.");
        }

        return handler(context);
    }
}

public sealed class DialogueEffectRegistry
{
    private readonly Dictionary<string, Action<DialogueRuntimeContext, string?>> _handlers = new(StringComparer.Ordinal);

    public void Register(string id, Action<DialogueRuntimeContext, string?> handler)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(handler);
        _handlers[id] = handler;
    }

    public bool Contains(string id) => _handlers.ContainsKey(id);

    public void Run(string id, string? value, DialogueRuntimeContext context)
    {
        if (!_handlers.TryGetValue(id, out var handler))
        {
            throw new InvalidOperationException($"Dialogue effect '{id}' is not registered.");
        }

        handler(context, value);
    }
}

public sealed record DialogueRunResult
{
    public required IReadOnlyList<string> Lines { get; init; }

    public required IReadOnlyList<string> ChoicesPresented { get; init; }

    public required IReadOnlyList<string> ChoicesTaken { get; init; }

    public required IReadOnlyList<string> EffectsRun { get; init; }

    public required DialogueAddress FinalAddress { get; init; }

    public required IReadOnlyList<AssetDiagnostic> Diagnostics { get; init; }

    public bool Success => !Diagnostics.Any(d => d.Severity == AssetDiagnosticSeverity.Error);
}

public static class DialogueRuntimeBridge
{
    public static DialogueRuntimeGraph BuildGraph(AssetPack<DialogueAsset> pack, ILocalizationTable localizationTable, IList<AssetDiagnostic>? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(pack);
        ArgumentNullException.ThrowIfNull(localizationTable);

        var nodes = new Dictionary<DialogueAddress, DialogueRuntimeNode>();
        var starts = new Dictionary<AssetId, DialogueAddress>();

        foreach (var entry in pack.Assets.Values.OrderBy(entry => entry.Id.ToString(), StringComparer.Ordinal))
        {
            var asset = entry.Asset;
            var assetId = entry.Id;
            starts[assetId] = new DialogueAddress(assetId, asset.Start);

            foreach (var (nodeId, node) in asset.Nodes)
            {
                var address = new DialogueAddress(assetId, nodeId);
                var choices = new List<DialogueRuntimeChoice>();
                for (var choiceIndex = 0; choiceIndex < node.Choices.Count; choiceIndex++)
                {
                    var choice = node.Choices[choiceIndex];
                    choices.Add(new DialogueRuntimeChoice
                    {
                        Id = choice.Id,
                        Line = ResolveLine(node.Speaker, choice.Line, choice.Text, localizationTable, entry, $"nodes.{nodeId}.choices[{choiceIndex}].line", diagnostics),
                        Target = ResolveTarget(pack, entry, nodeId, choice, choiceIndex, diagnostics),
                        Condition = choice.Condition,
                        Effects = choice.Effects
                    });
                }

                nodes[address] = new DialogueRuntimeNode
                {
                    Address = address,
                    Line = ResolveLine(node.Speaker, node.Line, node.Text, localizationTable, entry, $"nodes.{nodeId}.line", diagnostics),
                    Condition = node.Condition,
                    Effects = node.Effects,
                    Choices = choices
                };
            }
        }

        return new DialogueRuntimeGraph { Nodes = nodes, Starts = starts };
    }

    public static IReadOnlyList<AssetDiagnostic> ValidateRegistrySymbols(
        AssetPack<DialogueAsset> pack,
        DialogueConditionRegistry conditions,
        DialogueEffectRegistry effects)
    {
        ArgumentNullException.ThrowIfNull(pack);
        ArgumentNullException.ThrowIfNull(conditions);
        ArgumentNullException.ThrowIfNull(effects);

        var diagnostics = new List<AssetDiagnostic>();
        foreach (var entry in pack.Assets.Values)
        {
            foreach (var (nodeId, node) in entry.Asset.Nodes)
            {
                if (!string.IsNullOrWhiteSpace(node.Condition) && !conditions.Contains(node.Condition))
                {
                    diagnostics.Add(UnknownCondition(node.Condition, entry, $"nodes.{nodeId}.condition"));
                }

                for (var effectIndex = 0; effectIndex < node.Effects.Count; effectIndex++)
                {
                    var effect = node.Effects[effectIndex];
                    if (!effects.Contains(effect.Id))
                    {
                        diagnostics.Add(UnknownEffect(effect.Id, entry, $"nodes.{nodeId}.effects[{effectIndex}].id"));
                    }
                }

                for (var choiceIndex = 0; choiceIndex < node.Choices.Count; choiceIndex++)
                {
                    var choice = node.Choices[choiceIndex];
                    if (!string.IsNullOrWhiteSpace(choice.Condition) && !conditions.Contains(choice.Condition))
                    {
                        diagnostics.Add(UnknownCondition(choice.Condition, entry, $"nodes.{nodeId}.choices[{choiceIndex}].condition"));
                    }

                    for (var effectIndex = 0; effectIndex < choice.Effects.Count; effectIndex++)
                    {
                        var effect = choice.Effects[effectIndex];
                        if (!effects.Contains(effect.Id))
                        {
                            diagnostics.Add(UnknownEffect(effect.Id, entry, $"nodes.{nodeId}.choices[{choiceIndex}].effects[{effectIndex}].id"));
                        }
                    }
                }
            }
        }

        return diagnostics;
    }

    private static DialogueRuntimeLine ResolveLine(
        string speaker,
        string? line,
        string? fallbackText,
        ILocalizationTable table,
        AssetPackEntry<DialogueAsset> entry,
        string keyPath,
        IList<AssetDiagnostic>? diagnostics)
    {
        if (!string.IsNullOrWhiteSpace(line))
        {
            var key = new LocalizationKey(line);
            if (table.TryGet(key, out var localized))
            {
                return new DialogueRuntimeLine(speaker, key, localized, fallbackText);
            }

            if (!string.IsNullOrWhiteSpace(fallbackText))
            {
                diagnostics?.Add(AssetValidation.Warning(
                    "dialogue.localization_fallback",
                    $"Localization key '{key}' is missing; using authored fallback text.",
                    entry.SourcePath,
                    keyPath: keyPath,
                    span: Span(entry, keyPath)));
                return new DialogueRuntimeLine(speaker, key, fallbackText, fallbackText);
            }

            diagnostics?.Add(AssetValidation.Error(
                "dialogue.localization_missing_text",
                $"Localization key '{key}' is missing and no fallback text is available.",
                entry.SourcePath,
                keyPath: keyPath,
                span: Span(entry, keyPath)));
            return new DialogueRuntimeLine(speaker, key, $"<missing {key}>", null);
        }

        return new DialogueRuntimeLine(speaker, null, fallbackText ?? "<missing text>", fallbackText);
    }

    private static DialogueAddress ResolveTarget(
        AssetPack<DialogueAsset> pack,
        AssetPackEntry<DialogueAsset> entry,
        string nodeId,
        DialogueChoiceAsset choice,
        int choiceIndex,
        IList<AssetDiagnostic>? diagnostics)
    {
        var targetAsset = string.IsNullOrWhiteSpace(choice.NextAsset) ? entry.Id : new AssetId(choice.NextAsset!);
        var targetNode = string.IsNullOrWhiteSpace(choice.NextAsset) ? choice.Next : choice.NextNode;
        if (string.IsNullOrWhiteSpace(targetNode))
        {
            diagnostics?.Add(AssetValidation.Error(
                "dialogue.runtime_missing_choice_target",
                $"Choice '{choice.Id}' on node '{nodeId}' has no runtime target.",
                entry.SourcePath,
                keyPath: $"nodes.{nodeId}.choices[{choiceIndex}]"));
            return new DialogueAddress(targetAsset, "<missing>");
        }

        var address = new DialogueAddress(targetAsset, targetNode!);
        if (!pack.TryGetEntry(targetAsset, out var targetEntry) || !targetEntry.Asset.Nodes.ContainsKey(targetNode!))
        {
            diagnostics?.Add(AssetValidation.Error(
                "dialogue.runtime_missing_choice_target",
                $"Choice '{choice.Id}' on node '{nodeId}' points to missing runtime target '{address}'.",
                entry.SourcePath,
                keyPath: $"nodes.{nodeId}.choices[{choiceIndex}]"));
        }

        return address;
    }

    private static AssetDiagnostic UnknownCondition(string condition, AssetPackEntry<DialogueAsset> entry, string keyPath) =>
        AssetValidation.Error(
            "dialogue.unknown_condition",
            $"Dialogue condition '{condition}' is not registered.",
            entry.SourcePath,
            keyPath: keyPath,
            span: Span(entry, keyPath));

    private static AssetDiagnostic UnknownEffect(string effect, AssetPackEntry<DialogueAsset> entry, string keyPath) =>
        AssetValidation.Error(
            "dialogue.unknown_effect",
            $"Dialogue effect '{effect}' is not registered.",
            entry.SourcePath,
            keyPath: keyPath,
            span: Span(entry, keyPath));

    private static AssetSourceSpan? Span(AssetPackEntry<DialogueAsset> entry, string keyPath) =>
        entry.SourceMap is not null && entry.SourceMap.TryGetSpan(keyPath, out var span) ? span : null;
}

public sealed class DialogueTraversal
{
    private readonly DialogueRuntimeGraph _graph;
    private readonly DialogueConditionRegistry _conditions;
    private readonly DialogueEffectRegistry _effects;
    private readonly DialogueRuntimeContext _context;
    private readonly List<AssetDiagnostic> _diagnostics;
    private readonly List<string> _lines = [];
    private readonly List<string> _choicesPresented = [];
    private readonly List<string> _choicesTaken = [];
    private readonly List<string> _effectsRun = [];

    public DialogueTraversal(
        DialogueRuntimeGraph graph,
        DialogueConditionRegistry conditions,
        DialogueEffectRegistry effects,
        DialogueRuntimeContext? context = null,
        IEnumerable<AssetDiagnostic>? diagnostics = null)
    {
        _graph = graph ?? throw new ArgumentNullException(nameof(graph));
        _conditions = conditions ?? throw new ArgumentNullException(nameof(conditions));
        _effects = effects ?? throw new ArgumentNullException(nameof(effects));
        _context = context ?? new DialogueRuntimeContext();
        _diagnostics = diagnostics?.ToList() ?? [];
    }

    public DialogueRunResult RunScripted(DialogueAddress start, IReadOnlyList<string> scriptedChoices, int maxSteps = 64)
    {
        ArgumentNullException.ThrowIfNull(scriptedChoices);
        var current = start;
        var scriptIndex = 0;

        for (var step = 0; step < maxSteps; step++)
        {
            if (!_graph.TryGetNode(current, out var node))
            {
                AddError("dialogue.traversal_missing_node", $"Runtime node '{current}' does not exist.");
                break;
            }

            if (!ConditionAllows(node.Condition))
            {
                break;
            }

            _lines.Add($"{node.Line.Speaker}: {node.Line.Text}");
            RunEffects(node.Effects);

            var availableChoices = node.Choices.Where(choice => ConditionAllows(choice.Condition)).ToList();
            foreach (var choice in availableChoices)
            {
                _choicesPresented.Add($"{choice.Id}: {choice.Line.Text}");
            }

            if (availableChoices.Count == 0)
            {
                break;
            }

            var requested = scriptIndex < scriptedChoices.Count ? scriptedChoices[scriptIndex++] : availableChoices[0].Id;
            var selected = availableChoices.FirstOrDefault(choice => string.Equals(choice.Id, requested, StringComparison.Ordinal));
            if (selected is null)
            {
                AddError("dialogue.scripted_choice_unavailable", $"Scripted choice '{requested}' is not available at '{current}'.");
                break;
            }

            _choicesTaken.Add(selected.Id);
            RunEffects(selected.Effects);
            current = selected.Target;
        }

        return new DialogueRunResult
        {
            Lines = _lines,
            ChoicesPresented = _choicesPresented,
            ChoicesTaken = _choicesTaken,
            EffectsRun = _effectsRun,
            FinalAddress = current,
            Diagnostics = _diagnostics
        };
    }

    private bool ConditionAllows(string? condition)
    {
        if (string.IsNullOrWhiteSpace(condition))
        {
            return true;
        }

        if (!_conditions.Contains(condition))
        {
            AddError("dialogue.unknown_condition", $"Dialogue condition '{condition}' is not registered.");
            return false;
        }

        return _conditions.Evaluate(condition, _context);
    }

    private void RunEffects(IEnumerable<DialogueEffectAsset> effects)
    {
        foreach (var effect in effects)
        {
            if (!_effects.Contains(effect.Id))
            {
                AddError("dialogue.unknown_effect", $"Dialogue effect '{effect.Id}' is not registered.");
                continue;
            }

            _effects.Run(effect.Id, effect.Value, _context);
            _effectsRun.Add($"{effect.Id}{(effect.Value is null ? string.Empty : $" {effect.Value}")}");
        }
    }

    private void AddError(string code, string message) =>
        _diagnostics.Add(AssetValidation.Error(code, message));
}

public static class AriadneDialogueSampleRunner
{
    public static DialogueRunResult RunScripted(
        AssetPack<DialogueAsset> pack,
        ILocalizationTable localizationTable,
        DialogueAddress? start = null,
        IReadOnlyList<string>? scriptedChoices = null)
    {
        var diagnostics = new List<AssetDiagnostic>();
        var conditions = CreateDefaultConditions();
        var effects = CreateDefaultEffects();
        diagnostics.AddRange(DialogueRuntimeBridge.ValidateRegistrySymbols(pack, conditions, effects));
        var graph = DialogueRuntimeBridge.BuildGraph(pack, localizationTable, diagnostics);
        var startAddress = start ?? new DialogueAddress(new AssetId("dialogue.blacksmith_intro"), "greeting");
        var traversal = new DialogueTraversal(graph, conditions, effects, diagnostics: diagnostics);
        return traversal.RunScripted(startAddress, scriptedChoices ?? ["ask_work", "accept"]);
    }

    public static DialogueConditionRegistry CreateDefaultConditions()
    {
        var conditions = new DialogueConditionRegistry();
        conditions.Register("can_accept_bandit_quest", _ => true);
        conditions.Register("can_trade_with_blacksmith", _ => true);
        return conditions;
    }

    public static DialogueEffectRegistry CreateDefaultEffects()
    {
        var effects = new DialogueEffectRegistry();
        effects.Register("offer_quest", (_, _) => { });
        effects.Register("open_shop", (_, _) => { });
        return effects;
    }
}
