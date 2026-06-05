namespace Dominatus.Assets.Toml;

public enum AssetDiagnosticSeverity
{
    Info,
    Warning,
    Error
}

public sealed record AssetSourceSpan
{
    public required string SourcePath { get; init; }

    public int? StartLine { get; init; }

    public int? StartColumn { get; init; }

    public int? EndLine { get; init; }

    public int? EndColumn { get; init; }
}

public sealed record AssetDiagnostic
{
    public required AssetDiagnosticSeverity Severity { get; init; }

    public required string Code { get; init; }

    public required string Message { get; init; }

    public string? SourcePath { get; init; }

    public int? Line { get; init; }

    public int? Column { get; init; }

    public AssetSourceSpan? Span { get; init; }

    public string? KeyPath { get; init; }

    public AssetDiagnostic WithSpan(AssetSourceSpan span)
    {
        ArgumentNullException.ThrowIfNull(span);

        return this with
        {
            SourcePath = span.SourcePath,
            Line = span.StartLine,
            Column = span.StartColumn,
            Span = span
        };
    }

    public AssetDiagnostic WithKeyPath(string keyPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyPath);

        return this with { KeyPath = keyPath };
    }
}
