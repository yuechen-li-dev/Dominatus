namespace Dominatus.Actuators.Standard.Http;

public enum WebContentBlockKind { Text, Link, Image, Script, IFrame, Download, Unknown }

public sealed record WebContentBlock
{
    public required string Id { get; init; }
    public WebContentBlockKind Kind { get; init; } = WebContentBlockKind.Text;
    public string? Text { get; init; }
    public string? Url { get; init; }
    public string? Label { get; init; }
    public string? ClassOrId { get; init; }
    public string? SourceHint { get; init; }
}

public enum WebContentSafetyCategory { Safe, Advertisement, Sponsored, Tracker, PromptInjection, UnsafeDownload, Affiliate, Suspicious, Unknown }
public enum WebContentSafetySignalTarget { TextContains, LabelContains, ClassOrIdContains, UrlContains, SourceHintContains, KindIs, KindIsAndUrlMissing }

public sealed record WebContentSafetySignal
{
    public required string Id { get; init; }
    public WebContentSafetyCategory Category { get; init; }
    public WebContentSafetySignalTarget Target { get; init; }
    public required string Pattern { get; init; }
    public float Weight { get; init; }
    public string? Reason { get; init; }
}

public sealed record WebContentSafetySignalMatch(
    string Id,
    WebContentSafetyCategory Category,
    WebContentSafetySignalTarget Target,
    string Pattern,
    float Weight,
    string? Reason = null);

public enum WebContentBlockDecisionKind { Keep, Omit }

public sealed record WebContentBlockDecision
{
    public required string BlockId { get; init; }
    public WebContentBlockDecisionKind Decision { get; init; }
    public WebContentSafetyCategory Category { get; init; }
    public float RawScore { get; init; }
    public float Score { get; init; }
    public IReadOnlyList<WebContentSafetySignalMatch> Matches { get; init; } = [];
}

public sealed record WebContentSafetyReport
{
    public IReadOnlyList<WebContentBlockDecision> Decisions { get; init; } = [];
    public IReadOnlyList<WebContentBlock> KeptBlocks { get; init; } = [];
    public IReadOnlyList<WebContentBlock> OmittedBlocks { get; init; } = [];
    public bool HasOmissions { get; init; }
    public string OmissionSummary { get; init; } = "";
    public IReadOnlyList<WebContentOmittedBlockRecord> OmittedBlockRecords { get; init; } = [];
    public string SafeText { get; init; } = string.Empty;
}
public sealed record WebContentOmittedBlockRecord
{
    public required string BlockId { get; init; }
    public WebContentSafetyCategory Category { get; init; }
    public float RawScore { get; init; }
    public float Score { get; init; }
    public IReadOnlyList<string> TopSignalIds { get; init; } = [];
}

public sealed record WebContentSafetyOptions
{
    public IReadOnlyList<WebContentSafetySignal> Signals { get; init; } = WebContentSafetyDefaults.Signals;
    public IReadOnlyList<WebContentSafetyCategory> HardOmitCategories { get; init; } = WebContentSafetyDefaults.HardOmitCategories;
    public float OmitThreshold { get; init; } = 0.70f;
    public string BlockSeparator { get; init; } = "\n\n";
    public bool AnnotateOmissions { get; init; } = true;
    public int MaxOmissionSignalIds { get; init; } = 3;
}

public static class WebContentSafety
{
    public static WebContentSafetyReport Evaluate(IReadOnlyList<WebContentBlock> blocks, WebContentSafetyOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(blocks);
        var validated = WebContentSafetyValidation.Validate(options ?? new WebContentSafetyOptions(), blocks);

        var decisions = new List<WebContentBlockDecision>(blocks.Count);
        var kept = new List<WebContentBlock>();
        var omitted = new List<WebContentBlock>();

        foreach (var block in blocks)
        {
            var matches = new List<WebContentSafetySignalMatch>();
            var rawScore = 0f;
            foreach (var signal in validated.Signals)
            {
                if (!IsMatch(block, signal)) continue;
                rawScore += signal.Weight;
                matches.Add(new(signal.Id, signal.Category, signal.Target, signal.Pattern, signal.Weight, signal.Reason));
            }

            var score = Math.Clamp(rawScore, 0f, 1f);
            var hardMatch = matches.FirstOrDefault(m => validated.HardOmitCategories.Contains(m.Category));
            WebContentBlockDecisionKind decision;
            WebContentSafetyCategory category;
            if (hardMatch is not null)
            {
                decision = WebContentBlockDecisionKind.Omit;
                category = hardMatch.Category;
            }
            else if (score >= validated.OmitThreshold)
            {
                decision = WebContentBlockDecisionKind.Omit;
                category = DominantCategory(matches);
            }
            else
            {
                decision = WebContentBlockDecisionKind.Keep;
                category = WebContentSafetyCategory.Safe;
            }

            var blockDecision = new WebContentBlockDecision
            {
                BlockId = block.Id,
                Decision = decision,
                Category = category,
                RawScore = rawScore,
                Score = score,
                Matches = matches
            };
            decisions.Add(blockDecision);
            if (decision == WebContentBlockDecisionKind.Keep) kept.Add(block); else omitted.Add(block);
        }

        var omittedRecords = decisions.Where(static d => d.Decision == WebContentBlockDecisionKind.Omit).Select(d => new WebContentOmittedBlockRecord
        {
            BlockId = d.BlockId,
            Category = d.Category,
            RawScore = d.RawScore,
            Score = d.Score,
            TopSignalIds = d.Matches.OrderByDescending(static m => m.Weight).ThenBy(static m => m.Id, StringComparer.Ordinal).Take(validated.MaxOmissionSignalIds).Select(static m => m.Id).ToArray()
        }).ToArray();
        var safeText = RenderSafeText(blocks, decisions, validated);
        return new WebContentSafetyReport
        {
            Decisions = decisions,
            KeptBlocks = kept,
            OmittedBlocks = omitted,
            HasOmissions = omitted.Count > 0,
            OmissionSummary = BuildOmissionSummary(omittedRecords),
            OmittedBlockRecords = omittedRecords,
            SafeText = safeText
        };
    }

    private static bool IsMatch(WebContentBlock block, WebContentSafetySignal signal) => signal.Target switch
    {
        WebContentSafetySignalTarget.TextContains => Contains(block.Text, signal.Pattern),
        WebContentSafetySignalTarget.LabelContains => Contains(block.Label, signal.Pattern),
        WebContentSafetySignalTarget.ClassOrIdContains => Contains(block.ClassOrId, signal.Pattern),
        WebContentSafetySignalTarget.UrlContains => Contains(block.Url, signal.Pattern),
        WebContentSafetySignalTarget.SourceHintContains => Contains(block.SourceHint, signal.Pattern),
        WebContentSafetySignalTarget.KindIs => string.Equals(block.Kind.ToString(), signal.Pattern, StringComparison.OrdinalIgnoreCase),
        WebContentSafetySignalTarget.KindIsAndUrlMissing => string.Equals(block.Kind.ToString(), signal.Pattern, StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(block.Url),
        _ => false
    };

    private static bool Contains(string? value, string pattern) => !string.IsNullOrWhiteSpace(value) && value.Contains(pattern, StringComparison.OrdinalIgnoreCase);

    private static WebContentSafetyCategory DominantCategory(IReadOnlyList<WebContentSafetySignalMatch> matches)
        => matches.Count == 0 ? WebContentSafetyCategory.Unknown : matches.OrderByDescending(static m => m.Weight).First().Category;

    private static string RenderSafeText(IReadOnlyList<WebContentBlock> blocks, IReadOnlyList<WebContentBlockDecision> decisions, WebContentSafetyOptions options)
    {
        var decisionMap = decisions.ToDictionary(static d => d.BlockId, StringComparer.Ordinal);
        var parts = new List<string>(blocks.Count);
        foreach (var block in blocks)
        {
            var decision = decisionMap[block.Id];
            if (decision.Decision == WebContentBlockDecisionKind.Keep)
            {
                var rendered = RenderBlock(block);
                if (!string.IsNullOrWhiteSpace(rendered)) parts.Add(rendered);
                continue;
            }

            if (options.AnnotateOmissions)
            {
                var topSignals = decision.Matches.OrderByDescending(static m => m.Weight).ThenBy(static m => m.Id, StringComparer.Ordinal).Take(options.MaxOmissionSignalIds).Select(static m => m.Id);
                parts.Add($"[CONTENT OMITTED: {decision.Category}; block={block.Id}; signals={string.Join(",", topSignals)}]");
            }
        }

        return string.Join(options.BlockSeparator, parts.Where(static s => !string.IsNullOrWhiteSpace(s)));
    }

    private static string? RenderBlock(WebContentBlock block)
    {
        if (block.Kind == WebContentBlockKind.Link && !string.IsNullOrWhiteSpace(block.Url))
            return $"[{(string.IsNullOrWhiteSpace(block.Label) ? "link" : block.Label)}]({block.Url})";
        if (block.Kind == WebContentBlockKind.Download && !string.IsNullOrWhiteSpace(block.Url))
            return string.IsNullOrWhiteSpace(block.Label) ? $"[download]({block.Url})" : $"[download: {block.Label}]({block.Url})";
        if (block.Kind == WebContentBlockKind.Image)
        {
            if (!string.IsNullOrWhiteSpace(block.Label)) return $"[image: {block.Label}]";
            if (!string.IsNullOrWhiteSpace(block.Url)) return $"[image]({block.Url})";
        }
        if (!string.IsNullOrWhiteSpace(block.Text)) return block.Text;
        if (!string.IsNullOrWhiteSpace(block.Label)) return block.Label;
        if (!string.IsNullOrWhiteSpace(block.Url))
            return $"{block.Kind}: {block.Url}";
        return null;
    }

    private static string BuildOmissionSummary(IReadOnlyList<WebContentOmittedBlockRecord> omitted)
    {
        if (omitted.Count == 0) return "";
        var byCategory = omitted.GroupBy(static o => o.Category).OrderBy(static g => g.Key.ToString(), StringComparer.Ordinal).Select(static g => $"{g.Count()} {g.Key}");
        return $"[{omitted.Count} blocks omitted: {string.Join(", ", byCategory)}]";
    }
}

public static class WebContentSafetyDefaults
{
    public static IReadOnlyList<WebContentSafetyCategory> HardOmitCategories { get; } = [WebContentSafetyCategory.PromptInjection, WebContentSafetyCategory.UnsafeDownload];
    public static IReadOnlyList<WebContentSafetySignal> Signals { get; } =
    [
        new() { Id = "text.advertisement", Category = WebContentSafetyCategory.Advertisement, Target = WebContentSafetySignalTarget.TextContains, Pattern = "advertisement", Weight = 0.60f },
        new() { Id = "text.sponsored", Category = WebContentSafetyCategory.Sponsored, Target = WebContentSafetySignalTarget.TextContains, Pattern = "sponsored", Weight = 0.65f },
        new() { Id = "text.promoted", Category = WebContentSafetyCategory.Sponsored, Target = WebContentSafetySignalTarget.TextContains, Pattern = "promoted", Weight = 0.55f },
        new() { Id = "label.sponsored", Category = WebContentSafetyCategory.Sponsored, Target = WebContentSafetySignalTarget.LabelContains, Pattern = "sponsored", Weight = 0.70f },
        new() { Id = "label.advertisement", Category = WebContentSafetyCategory.Advertisement, Target = WebContentSafetySignalTarget.LabelContains, Pattern = "advertisement", Weight = 0.70f },
        new() { Id = "class.ad", Category = WebContentSafetyCategory.Advertisement, Target = WebContentSafetySignalTarget.ClassOrIdContains, Pattern = "ad-", Weight = 0.45f },
        new() { Id = "class.ads_hyphen_suffix", Category = WebContentSafetyCategory.Advertisement, Target = WebContentSafetySignalTarget.ClassOrIdContains, Pattern = "-ads", Weight = 0.45f },
        new() { Id = "class.ads_hyphen_prefix", Category = WebContentSafetyCategory.Advertisement, Target = WebContentSafetySignalTarget.ClassOrIdContains, Pattern = "ads-", Weight = 0.45f },
        new() { Id = "class.ads_underscore_suffix", Category = WebContentSafetyCategory.Advertisement, Target = WebContentSafetySignalTarget.ClassOrIdContains, Pattern = "_ads", Weight = 0.45f },
        new() { Id = "class.ads_underscore_prefix", Category = WebContentSafetyCategory.Advertisement, Target = WebContentSafetySignalTarget.ClassOrIdContains, Pattern = "ads_", Weight = 0.45f },
        new() { Id = "class.sponsor", Category = WebContentSafetyCategory.Sponsored, Target = WebContentSafetySignalTarget.ClassOrIdContains, Pattern = "sponsor", Weight = 0.60f },
        new() { Id = "text.ignore_previous", Category = WebContentSafetyCategory.PromptInjection, Target = WebContentSafetySignalTarget.TextContains, Pattern = "ignore previous instructions", Weight = 0.95f },
        new() { Id = "text.system_prompt", Category = WebContentSafetyCategory.Suspicious, Target = WebContentSafetySignalTarget.TextContains, Pattern = "system prompt", Weight = 0.15f },
        new() { Id = "text.follow_these_instructions", Category = WebContentSafetyCategory.Suspicious, Target = WebContentSafetySignalTarget.TextContains, Pattern = "follow these instructions", Weight = 0.15f },
        new() { Id = "pi.disregard_previous", Category = WebContentSafetyCategory.PromptInjection, Target = WebContentSafetySignalTarget.TextContains, Pattern = "disregard your previous", Weight = 0.95f },
        new() { Id = "pi.ignore_all_previous", Category = WebContentSafetyCategory.PromptInjection, Target = WebContentSafetySignalTarget.TextContains, Pattern = "ignore all previous", Weight = 0.95f },
        new() { Id = "pi.forget_instructions", Category = WebContentSafetyCategory.PromptInjection, Target = WebContentSafetySignalTarget.TextContains, Pattern = "forget your instructions", Weight = 0.95f },
        new() { Id = "pi.reveal_instructions", Category = WebContentSafetyCategory.PromptInjection, Target = WebContentSafetySignalTarget.TextContains, Pattern = "reveal your instructions", Weight = 0.95f },
        new() { Id = "pi.show_prompt", Category = WebContentSafetyCategory.PromptInjection, Target = WebContentSafetySignalTarget.TextContains, Pattern = "show me your prompt", Weight = 0.95f },
        new() { Id = "pi.script_tag", Category = WebContentSafetyCategory.PromptInjection, Target = WebContentSafetySignalTarget.TextContains, Pattern = "<script", Weight = 0.85f },
        new() { Id = "text.do_not_tell_user", Category = WebContentSafetyCategory.PromptInjection, Target = WebContentSafetySignalTarget.TextContains, Pattern = "do not tell the user", Weight = 0.80f },
        new() { Id = "url.javascript_scheme", Category = WebContentSafetyCategory.PromptInjection, Target = WebContentSafetySignalTarget.UrlContains, Pattern = "javascript:", Weight = 0.99f },
        new() { Id = "url.data_scheme", Category = WebContentSafetyCategory.PromptInjection, Target = WebContentSafetySignalTarget.UrlContains, Pattern = "data:", Weight = 0.85f },
        new() { Id = "url.bitly", Category = WebContentSafetyCategory.Suspicious, Target = WebContentSafetySignalTarget.UrlContains, Pattern = "bit.ly/", Weight = 0.65f },
        new() { Id = "cta.claim_prize", Category = WebContentSafetyCategory.Suspicious, Target = WebContentSafetySignalTarget.TextContains, Pattern = "claim your prize", Weight = 0.90f },
        new() { Id = "text.update_required", Category = WebContentSafetyCategory.UnsafeDownload, Target = WebContentSafetySignalTarget.TextContains, Pattern = "update required", Weight = 0.75f },
        new() { Id = "text.paid_partnership", Category = WebContentSafetyCategory.Advertisement, Target = WebContentSafetySignalTarget.TextContains, Pattern = "paid partnership", Weight = 0.85f },
        new() { Id = "text.advertorial", Category = WebContentSafetyCategory.Advertisement, Target = WebContentSafetySignalTarget.TextContains, Pattern = "advertorial", Weight = 0.90f },
        new() { Id = "class.dfp", Category = WebContentSafetyCategory.Advertisement, Target = WebContentSafetySignalTarget.ClassOrIdContains, Pattern = "dfp", Weight = 0.90f },
        new() { Id = "url.affid", Category = WebContentSafetyCategory.Affiliate, Target = WebContentSafetySignalTarget.UrlContains, Pattern = "affid=", Weight = 0.60f },
        new() { Id = "text.download_now", Category = WebContentSafetyCategory.UnsafeDownload, Target = WebContentSafetySignalTarget.TextContains, Pattern = "download now", Weight = 0.45f },
        new() { Id = "text.install_now", Category = WebContentSafetyCategory.UnsafeDownload, Target = WebContentSafetySignalTarget.TextContains, Pattern = "install now", Weight = 0.45f },
        new() { Id = "url.download", Category = WebContentSafetyCategory.UnsafeDownload, Target = WebContentSafetySignalTarget.UrlContains, Pattern = "/download", Weight = 0.35f },
        new() { Id = "url.exe", Category = WebContentSafetyCategory.UnsafeDownload, Target = WebContentSafetySignalTarget.UrlContains, Pattern = ".exe", Weight = 0.80f },
        new() { Id = "url.gclid", Category = WebContentSafetyCategory.Tracker, Target = WebContentSafetySignalTarget.UrlContains, Pattern = "gclid=", Weight = 0.55f },
        new() { Id = "url.fbclid", Category = WebContentSafetyCategory.Tracker, Target = WebContentSafetySignalTarget.UrlContains, Pattern = "fbclid=", Weight = 0.55f },
        new() { Id = "url.utm", Category = WebContentSafetyCategory.Tracker, Target = WebContentSafetySignalTarget.UrlContains, Pattern = "utm_", Weight = 0.25f },
        new() { Id = "url.affiliate", Category = WebContentSafetyCategory.Affiliate, Target = WebContentSafetySignalTarget.UrlContains, Pattern = "affiliate", Weight = 0.45f },
        new() { Id = "url.aff", Category = WebContentSafetyCategory.Affiliate, Target = WebContentSafetySignalTarget.UrlContains, Pattern = "aff=", Weight = 0.55f },
        new() { Id = "url.click", Category = WebContentSafetyCategory.Tracker, Target = WebContentSafetySignalTarget.UrlContains, Pattern = "/click", Weight = 0.35f },
        new() { Id = "url.redirect", Category = WebContentSafetyCategory.Tracker, Target = WebContentSafetySignalTarget.UrlContains, Pattern = "/redirect", Weight = 0.35f },
        new() { Id = "kind.iframe", Category = WebContentSafetyCategory.Suspicious, Target = WebContentSafetySignalTarget.KindIs, Pattern = "IFrame", Weight = 0.40f },
        new() { Id = "kind.script", Category = WebContentSafetyCategory.Suspicious, Target = WebContentSafetySignalTarget.KindIs, Pattern = "Script", Weight = 0.50f },
        new() { Id = "kind.download", Category = WebContentSafetyCategory.UnsafeDownload, Target = WebContentSafetySignalTarget.KindIs, Pattern = "Download", Weight = 0.60f },
        new() { Id = "structural.link_missing_url", Category = WebContentSafetyCategory.Suspicious, Target = WebContentSafetySignalTarget.KindIsAndUrlMissing, Pattern = "Link", Weight = 0.50f }
    ];
}

internal static class WebContentSafetyValidation
{
    public static WebContentSafetyOptions Validate(WebContentSafetyOptions options, IReadOnlyList<WebContentBlock> blocks)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.OmitThreshold is < 0f or > 1f) throw new ArgumentException("OmitThreshold must be between 0 and 1.", nameof(options));
        if (options.MaxOmissionSignalIds < 0) throw new ArgumentException("MaxOmissionSignalIds must be >= 0.", nameof(options));
        if (options.BlockSeparator is null) throw new ArgumentException("BlockSeparator cannot be null.", nameof(options));
        var seenSignals = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var signal in options.Signals)
        {
            if (signal is null) throw new ArgumentException("Signal cannot be null.", nameof(options.Signals));
            if (string.IsNullOrWhiteSpace(signal.Id)) throw new ArgumentException("Signal Id is required.", nameof(options.Signals));
            if (signal.Category == WebContentSafetyCategory.Safe) throw new ArgumentException("Signal category cannot be Safe.", nameof(options.Signals));
            if (string.IsNullOrWhiteSpace(signal.Pattern)) throw new ArgumentException("Signal Pattern is required.", nameof(options.Signals));
            if (signal.Weight <= 0f) throw new ArgumentException("Signal Weight must be > 0.", nameof(options.Signals));
            if (!seenSignals.Add(signal.Id.Trim())) throw new ArgumentException($"Duplicate signal Id '{signal.Id}'.", nameof(options.Signals));
        }

        foreach (var block in blocks)
        {
            if (block is null) throw new ArgumentException("Block cannot be null.", nameof(blocks));
            if (string.IsNullOrWhiteSpace(block.Id)) throw new ArgumentException("Block Id is required.", nameof(blocks));
            var hasUseful = !string.IsNullOrWhiteSpace(block.Text)
                || !string.IsNullOrWhiteSpace(block.Url)
                || !string.IsNullOrWhiteSpace(block.Label)
                || !string.IsNullOrWhiteSpace(block.ClassOrId)
                || !string.IsNullOrWhiteSpace(block.SourceHint)
                || block.Kind is WebContentBlockKind.Script or WebContentBlockKind.IFrame or WebContentBlockKind.Download;
            if (!hasUseful) throw new ArgumentException($"Block '{block.Id}' does not contain useful content fields.", nameof(blocks));
        }

        return options;
    }
}
