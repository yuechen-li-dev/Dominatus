namespace Dominatus.Assets.Toml;

public static class AssetValidation
{
    public static AssetDiagnostic Error(string code, string message, string? sourcePath = null, int? line = null, int? column = null, string? keyPath = null, AssetSourceSpan? span = null) =>
        Create(AssetDiagnosticSeverity.Error, code, message, sourcePath, line, column, keyPath, span);

    public static AssetDiagnostic Warning(string code, string message, string? sourcePath = null, int? line = null, int? column = null, string? keyPath = null, AssetSourceSpan? span = null) =>
        Create(AssetDiagnosticSeverity.Warning, code, message, sourcePath, line, column, keyPath, span);

    public static AssetDiagnostic Info(string code, string message, string? sourcePath = null, int? line = null, int? column = null, string? keyPath = null, AssetSourceSpan? span = null) =>
        Create(AssetDiagnosticSeverity.Info, code, message, sourcePath, line, column, keyPath, span);

    public static AssetDiagnostic Required(string fieldName, string? sourcePath = null) =>
        Required(fieldName, sourcePath, keyPath: fieldName);

    public static AssetDiagnostic Required(string fieldName, string? sourcePath, string? keyPath) =>
        Error("asset.required_field", $"Required field '{fieldName}' is missing or empty.", sourcePath, keyPath: keyPath);

    private static AssetDiagnostic Create(AssetDiagnosticSeverity severity, string code, string message, string? sourcePath, int? line, int? column, string? keyPath, AssetSourceSpan? span)
    {
        if (span is not null)
        {
            sourcePath = span.SourcePath;
            line = span.StartLine;
            column = span.StartColumn;
        }

        return new AssetDiagnostic
        {
            Severity = severity,
            Code = code,
            Message = message,
            SourcePath = sourcePath,
            Line = line,
            Column = column,
            Span = span,
            KeyPath = keyPath
        };
    }
}
