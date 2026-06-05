using System.Globalization;
using System.Text;

namespace Dominatus.Assets.Toml;

public static class AssetPackReloadReportFormatter
{
    public static string Format<TAsset>(AssetPackReloadResult<TAsset> result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var builder = new StringBuilder();
        builder.Append("Asset reload: ");
        builder.AppendLine(result.Success ? "OK" : result.UsedOldPack ? "FAILED — keeping previous pack" : "FAILED");
        builder.Append("Added: ").AppendLine(result.Added.Count.ToString(CultureInfo.InvariantCulture));
        builder.Append("Removed: ").AppendLine(result.Removed.Count.ToString(CultureInfo.InvariantCulture));
        builder.Append("Changed: ").AppendLine(result.Changed.Count.ToString(CultureInfo.InvariantCulture));
        builder.Append("Unchanged: ").AppendLine(result.Unchanged.Count.ToString(CultureInfo.InvariantCulture));

        if (!result.Success)
        {
            var errors = result.Diagnostics.Count(d => d.Severity == AssetDiagnosticSeverity.Error);
            builder.Append("Errors: ").AppendLine(errors.ToString(CultureInfo.InvariantCulture));
            builder.Append("Effective pack: ").AppendLine(result.UsedOldPack ? "old" : "new");
        }
        else
        {
            builder.Append("Effective pack: ").AppendLine(result.UsedOldPack ? "old" : "new");
        }

        AppendIds(builder, "Added", result.Added);
        AppendIds(builder, "Removed", result.Removed);
        AppendIds(builder, "Changed", result.Changed);
        AppendIds(builder, "Unchanged", result.Unchanged);

        if (result.Diagnostics.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("Diagnostics:");
            builder.Append(AssetDiagnosticFormatter.FormatMany(result.Diagnostics));
        }

        return builder.ToString().TrimEnd();
    }

    private static void AppendIds(StringBuilder builder, string title, IReadOnlyList<AssetId> ids)
    {
        if (ids.Count == 0)
        {
            return;
        }

        builder.AppendLine();
        builder.AppendLine($"{title}:");
        foreach (var id in ids)
        {
            builder.Append("* ").AppendLine(id.Value);
        }
    }
}
