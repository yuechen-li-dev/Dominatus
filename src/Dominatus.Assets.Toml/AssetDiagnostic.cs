namespace Dominatus.Assets.Toml;

public enum AssetDiagnosticSeverity
{
    Info,
    Warning,
    Error
}

public sealed record AssetDiagnostic
{
    public required AssetDiagnosticSeverity Severity { get; init; }

    public required string Code { get; init; }

    public required string Message { get; init; }

    public string? SourcePath { get; init; }

    public int? Line { get; init; }

    public int? Column { get; init; }
}
