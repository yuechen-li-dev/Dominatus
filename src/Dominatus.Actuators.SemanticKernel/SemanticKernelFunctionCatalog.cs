using Microsoft.SemanticKernel;

namespace Dominatus.Actuators.SemanticKernel;

public sealed record SemanticKernelFunctionParameterMetadata(
    string Name,
    string? Description = null,
    string? Type = null,
    bool IsRequired = false);

public sealed record SemanticKernelFunctionMetadata(
    string PluginName,
    string FunctionName,
    bool IsAllowed,
    bool ExistsInKernel,
    string? Description = null,
    IReadOnlyList<SemanticKernelFunctionParameterMetadata>? Parameters = null);

public sealed class SemanticKernelFunctionCatalog
{
    private readonly SemanticKernelActuatorOptions _options;
    private readonly ISemanticKernelFunctionMetadataReader _metadataReader;

    public SemanticKernelFunctionCatalog(Kernel kernel, SemanticKernelActuatorOptions options)
        : this(new KernelSemanticKernelFunctionMetadataReader(kernel ?? throw new ArgumentNullException(nameof(kernel))), options)
    {
    }

    internal SemanticKernelFunctionCatalog(ISemanticKernelFunctionMetadataReader metadataReader, SemanticKernelActuatorOptions options)
    {
        _metadataReader = metadataReader ?? throw new ArgumentNullException(nameof(metadataReader));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public IReadOnlyList<SemanticKernelFunctionMetadata> GetAllowedFunctions()
    {
        var configured = _options.AllowedFunctions ?? throw new ArgumentException("AllowedFunctions is required.", nameof(_options));

        return configured
            .OrderBy(f => f.PluginName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(f => f.FunctionName, StringComparer.OrdinalIgnoreCase)
            .Select(f =>
            {
                var exists = _metadataReader.TryGetMetadata(f.PluginName, f.FunctionName, out var metadata);
                return new SemanticKernelFunctionMetadata(
                    f.PluginName,
                    f.FunctionName,
                    IsAllowed: true,
                    ExistsInKernel: exists,
                    Description: metadata?.Description,
                    Parameters: metadata?.Parameters);
            })
            .ToArray();
    }
}

internal sealed record SemanticKernelResolvedFunctionMetadata(
    string? Description,
    IReadOnlyList<SemanticKernelFunctionParameterMetadata> Parameters);

internal interface ISemanticKernelFunctionMetadataReader
{
    bool TryGetMetadata(string pluginName, string functionName, out SemanticKernelResolvedFunctionMetadata? metadata);
}

internal sealed class KernelSemanticKernelFunctionMetadataReader(Kernel kernel) : ISemanticKernelFunctionMetadataReader
{
    public bool TryGetMetadata(string pluginName, string functionName, out SemanticKernelResolvedFunctionMetadata? metadata)
    {
        metadata = null;
        if (!kernel.Plugins.TryGetFunction(pluginName, functionName, out var function))
            return false;

        var parameters = function.Metadata.Parameters
            .Select(p => new SemanticKernelFunctionParameterMetadata(
                p.Name,
                p.Description,
                p.ParameterType?.Name,
                p.IsRequired))
            .ToArray();

        metadata = new SemanticKernelResolvedFunctionMetadata(function.Metadata.Description, parameters);
        return true;
    }
}
