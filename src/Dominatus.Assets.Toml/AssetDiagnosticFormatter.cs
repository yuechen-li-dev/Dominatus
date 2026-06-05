using System.Globalization;
using System.Text;

namespace Dominatus.Assets.Toml;

public static class AssetDiagnosticFormatter
{
    public static string Format(AssetDiagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);

        var builder = new StringBuilder();
        builder.Append(ToDisplaySeverity(diagnostic.Severity));
        builder.Append(' ');
        builder.Append(diagnostic.Code);
        builder.Append(": ");
        builder.Append(diagnostic.Message);

        var sourcePath = diagnostic.Span?.SourcePath ?? diagnostic.SourcePath;
        if (!string.IsNullOrWhiteSpace(sourcePath))
        {
            builder.AppendLine();
            builder.Append("at ");
            builder.Append(sourcePath);

            var line = diagnostic.Span?.StartLine ?? diagnostic.Line;
            var column = diagnostic.Span?.StartColumn ?? diagnostic.Column;
            if (line is not null)
            {
                builder.Append(':');
                builder.Append(line.Value.ToString(CultureInfo.InvariantCulture));
                if (column is not null)
                {
                    builder.Append(':');
                    builder.Append(column.Value.ToString(CultureInfo.InvariantCulture));
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(diagnostic.KeyPath))
        {
            builder.AppendLine();
            builder.Append("key: ");
            builder.Append(diagnostic.KeyPath);
        }

        return builder.ToString();
    }

    public static string FormatMany(IEnumerable<AssetDiagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);

        return string.Join(Environment.NewLine, diagnostics.Select(Format));
    }

    private static string ToDisplaySeverity(AssetDiagnosticSeverity severity) => severity switch
    {
        AssetDiagnosticSeverity.Info => "info",
        AssetDiagnosticSeverity.Warning => "warning",
        AssetDiagnosticSeverity.Error => "error",
        _ => severity.ToString().ToLowerInvariant()
    };
}
