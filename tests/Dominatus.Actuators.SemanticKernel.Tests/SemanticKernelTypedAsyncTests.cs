using System.ComponentModel;
using System.Text.Json;
using Microsoft.SemanticKernel;

namespace Dominatus.Actuators.SemanticKernel.Tests;

public sealed class SemanticKernelTypedAsyncTests
{
    public sealed record Filter(double Minimum, string[] Tags);
    public sealed class Catalog
    {
        [KernelFunction("find"), Description("Find matching parts with source evidence.")]
        public async Task<object> Find(Filter filter, CancellationToken cancellationToken)
        { await Task.Delay(1, cancellationToken); return new { filter.Minimum, filter.Tags, citation = "catalog:row:2" }; }
    }
    [Fact]
    public async Task Native_SK_binds_nested_DTOs_exports_native_schema_and_returns_structured_JSON()
    {
        var kernel = new Kernel(); kernel.Plugins.AddFromObject(new Catalog(), "parts");
        var options = new SemanticKernelActuatorOptions { AllowedFunctions = [new("parts", "find")] };
        var tools = SemanticKernelToolCatalog.Create(kernel, options);
        Assert.True(tools["parts__find"].Schema.GetProperty("parameters").GetProperty("properties").GetProperty("filter").TryGetProperty("properties", out _));
        var handler = new SemanticKernelActuationHandler(kernel, options);
        var result = await handler.HandleAsync(default, new("parts", "find", """{"filter":{"Minimum":8.5,"Tags":["bearing","sealed"]}}"""), default);
        Assert.True(result.Ok, result.Error);
        using var json = JsonDocument.Parse(((SemanticKernelFunctionResult)result.Payload!).ResultText);
        Assert.Equal(8.5, json.RootElement.GetProperty("minimum").GetDouble());
        Assert.Equal(2, json.RootElement.GetProperty("tags").GetArrayLength());
        Assert.Equal("catalog:row:2", json.RootElement.GetProperty("citation").GetString());
    }
}
