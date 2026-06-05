using System.Reflection;
using Tomlyn;
using Tomlyn.Syntax;

namespace Dominatus.Assets.Toml;

public static class TomlAssetLoader
{
    public static TomlAssetLoadResult<T> LoadString<T>(
        string toml,
        TomlAssetLoadOptions? options = null) where T : class =>
        LoadString<T>(toml, validator: null, options);

    public static TomlAssetLoadResult<T> LoadString<T>(
        string toml,
        IAssetValidator<T>? validator,
        TomlAssetLoadOptions? options = null) where T : class
    {
        ArgumentNullException.ThrowIfNull(toml);

        options ??= new TomlAssetLoadOptions();
        var diagnostics = new List<AssetDiagnostic>();
        var sourcePath = options.SourcePath;

        DocumentSyntax document;
        try
        {
            document = global::Tomlyn.Toml.Parse(toml, sourcePath ?? string.Empty);
        }
        catch (Exception ex)
        {
            diagnostics.Add(AssetValidation.Error("toml.parse", $"Unexpected TOML parse failure: {ex.Message}", sourcePath));
            return new TomlAssetLoadResult<T> { Value = default, Diagnostics = diagnostics };
        }

        var sourceMap = TomlAssetSourceMapBuilder.Build(document, sourcePath);
        diagnostics.AddRange(document.Diagnostics.Select(d => ConvertDiagnostic(d, sourcePath, "toml.parse", sourceMap)));
        if (document.HasErrors)
        {
            return new TomlAssetLoadResult<T> { Value = default, Diagnostics = diagnostics, SourceMap = sourceMap };
        }

        try
        {
            var bindResult = TryBindToModel<T>(document, options.ModelOptions);
            if (!bindResult.Success)
            {
                diagnostics.AddRange(bindResult.Diagnostics.Select(d => ConvertDiagnostic(d, sourcePath, "toml.bind", sourceMap)));
                return new TomlAssetLoadResult<T> { Value = default, Diagnostics = diagnostics, SourceMap = sourceMap };
            }

            var value = bindResult.Value;
            diagnostics.AddRange(bindResult.Diagnostics.Select(d => ConvertDiagnostic(d, sourcePath, "toml.bind", sourceMap)));

            if (value is null)
            {
                diagnostics.Add(AssetValidation.Error("toml.bind", $"TOML content did not bind to {typeof(T).Name}.", sourcePath));
                return new TomlAssetLoadResult<T> { Value = default, Diagnostics = diagnostics, SourceMap = sourceMap };
            }

            if (validator is not null)
            {
                diagnostics.AddRange(validator.Validate(value, new AssetValidationContext { SourcePath = sourcePath, SourceMap = sourceMap }));
            }

            if (options.RequireNoDiagnostics && diagnostics.Count > 0 && !diagnostics.Any(d => d.Severity == AssetDiagnosticSeverity.Error))
            {
                diagnostics.Add(AssetValidation.Error("asset.diagnostics_present", "TOML asset produced diagnostics and RequireNoDiagnostics is enabled.", sourcePath));
            }

            return new TomlAssetLoadResult<T> { Value = value, Diagnostics = diagnostics, SourceMap = sourceMap };
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or FormatException or NotSupportedException or TargetInvocationException)
        {
            var message = (ex as TargetInvocationException)?.InnerException?.Message ?? ex.Message;
            diagnostics.Add(AssetValidation.Error("toml.bind", $"TOML content could not bind to {typeof(T).Name}: {message}", sourcePath));
            return new TomlAssetLoadResult<T> { Value = default, Diagnostics = diagnostics };
        }
    }

    public static TomlAssetLoadResult<T> LoadFile<T>(
        string path,
        TomlAssetLoadOptions? options = null) where T : class =>
        LoadFile<T>(path, validator: null, options);

    public static TomlAssetLoadResult<T> LoadFile<T>(
        string path,
        IAssetValidator<T>? validator,
        TomlAssetLoadOptions? options = null) where T : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var sourcePath = options?.SourcePath ?? path;
        var effectiveOptions = (options ?? new TomlAssetLoadOptions()) with { SourcePath = sourcePath };
        var toml = File.ReadAllText(path);
        return LoadString(toml, validator, effectiveOptions);
    }

    private static TomlynBindResult<T> TryBindToModel<T>(DocumentSyntax document, TomlModelOptions? modelOptions) where T : class
    {
        var method = typeof(global::Tomlyn.Toml)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(method =>
                method.Name == nameof(global::Tomlyn.Toml.TryToModel) &&
                method.IsGenericMethodDefinition &&
                method.GetParameters() is
                [
                    { ParameterType: var first },
                    { IsOut: true },
                    { IsOut: true },
                    { ParameterType: var fourth }
                ] &&
                first == typeof(DocumentSyntax) &&
                fourth == typeof(TomlModelOptions));

        var genericMethod = method.MakeGenericMethod(typeof(T));
        object?[] parameters = [document, null, null, modelOptions];
        var success = (bool)genericMethod.Invoke(null, parameters)!;
        return new TomlynBindResult<T>((T?)parameters[1], (DiagnosticsBag)parameters[2]!, success);
    }

    private sealed record TomlynBindResult<T>(T? Value, DiagnosticsBag Diagnostics, bool Success);

    private static AssetDiagnostic ConvertDiagnostic(DiagnosticMessage diagnostic, string? sourcePath, string code, TomlAssetSourceMap? sourceMap)
    {
        var severity = diagnostic.Kind == DiagnosticMessageKind.Warning
            ? AssetDiagnosticSeverity.Warning
            : AssetDiagnosticSeverity.Error;

        return new AssetDiagnostic
        {
            Severity = severity,
            Code = code,
            Message = diagnostic.Message,
            SourcePath = string.IsNullOrWhiteSpace(diagnostic.Span.FileName) ? sourcePath : diagnostic.Span.FileName,
            Line = diagnostic.Span.Start.Line + 1,
            Column = diagnostic.Span.Start.Column + 1,
            Span = ToAssetSpan(diagnostic.Span, sourcePath),
            KeyPath = sourceMap?.FindNearestKeyPath(diagnostic.Span)
        };
    }

    private static AssetSourceSpan ToAssetSpan(SourceSpan span, string? fallbackSourcePath) =>
        new()
        {
            SourcePath = string.IsNullOrWhiteSpace(span.FileName) ? fallbackSourcePath ?? string.Empty : span.FileName,
            StartLine = span.Start.Line + 1,
            StartColumn = span.Start.Column + 1,
            EndLine = span.End.Line + 1,
            EndColumn = span.End.Column + 1
        };
}
