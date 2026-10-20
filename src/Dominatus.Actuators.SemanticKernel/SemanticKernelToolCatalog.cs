using System.Text.Json;
using Microsoft.SemanticKernel;

namespace Dominatus.Actuators.SemanticKernel;

/// <summary>Project registered native/OpenAPI/MCP metadata into conversation function schemas.
/// The supplied function grant is authoritative; discovery never grants invocation.</summary>
public static class SemanticKernelToolCatalog
{
    public static IReadOnlyDictionary<string, (AllowedSemanticKernelFunction Function, JsonElement Schema)> Create(
        Kernel kernel, SemanticKernelActuatorOptions options)
    {
        _ = new SemanticKernelRequestResolver(options);
        var result = new Dictionary<string, (AllowedSemanticKernelFunction, JsonElement)>(StringComparer.Ordinal);
        foreach (var allowed in options.AllowedFunctions)
        {
            var function = kernel.Plugins.GetFunction(allowed.PluginName, allowed.FunctionName);
            var name = allowed.PluginName + "__" + allowed.FunctionName;
            var schema = JsonSerializer.SerializeToElement(new { type = "function", name, description = function.Description,
                parameters = new { type = "object", properties = function.Metadata.Parameters.ToDictionary(p => p.Name,
                    p => p.Schema is not null ? JsonSerializer.Deserialize<JsonElement>(p.Schema.ToString()) : JsonSerializer.SerializeToElement(new { type = "string", description = p.Description })),
                    required = function.Metadata.Parameters.Where(p => p.IsRequired).Select(p => p.Name).ToArray(), additionalProperties = false } });
            result.Add(name, (allowed, schema));
        }
        return result;
    }
}
