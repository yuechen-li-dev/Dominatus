namespace Dominatus.Assets.Toml;

public static class AssetValidation
{
    public static AssetDiagnostic Error(string code, string message, string? sourcePath = null, int? line = null, int? column = null) =>
        new()
        {
            Severity = AssetDiagnosticSeverity.Error,
            Code = code,
            Message = message,
            SourcePath = sourcePath,
            Line = line,
            Column = column
        };

    public static AssetDiagnostic Warning(string code, string message, string? sourcePath = null, int? line = null, int? column = null) =>
        new()
        {
            Severity = AssetDiagnosticSeverity.Warning,
            Code = code,
            Message = message,
            SourcePath = sourcePath,
            Line = line,
            Column = column
        };

    public static AssetDiagnostic Info(string code, string message, string? sourcePath = null, int? line = null, int? column = null) =>
        new()
        {
            Severity = AssetDiagnosticSeverity.Info,
            Code = code,
            Message = message,
            SourcePath = sourcePath,
            Line = line,
            Column = column
        };

    public static AssetDiagnostic Required(string fieldName, string? sourcePath = null) =>
        Error("ASSET_REQUIRED", $"Required field '{fieldName}' is missing or empty.", sourcePath);
}
