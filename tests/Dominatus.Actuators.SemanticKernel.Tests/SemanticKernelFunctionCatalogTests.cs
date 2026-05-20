namespace Dominatus.Actuators.SemanticKernel.Tests;

public sealed class SemanticKernelFunctionCatalogTests
{
    [Fact]
    public void SemanticKernelCatalog_ReturnsAllowedFunctions()
    {
        var catalog = CreateCatalog(
            [new("Tools", "Echo"), new("Math", "Add")],
            new Dictionary<(string Plugin, string Function), SemanticKernelResolvedFunctionMetadata?>
            {
                [("Tools", "Echo")] = new("Echoes text", []),
                [("Math", "Add")] = new("Adds values", [])
            });

        var result = catalog.GetAllowedFunctions();

        Assert.Equal(2, result.Count);
        Assert.All(result, m => Assert.True(m.IsAllowed));
    }

    [Fact]
    public void SemanticKernelCatalog_ReturnsDeterministicOrder()
    {
        var catalog = CreateCatalog([new("z", "b"), new("A", "c"), new("a", "b")], new Dictionary<(string Plugin, string Function), SemanticKernelResolvedFunctionMetadata?>());
        var result = catalog.GetAllowedFunctions();
        Assert.Collection(result,
            x => Assert.Equal(("a", "b"), (x.PluginName, x.FunctionName)),
            x => Assert.Equal(("A", "c"), (x.PluginName, x.FunctionName)),
            x => Assert.Equal(("z", "b"), (x.PluginName, x.FunctionName)));
    }

    [Fact]
    public void SemanticKernelCatalog_MarksMissingAllowedFunctionAsMissing()
    {
        var catalog = CreateCatalog([new("Tools", "Missing")], new Dictionary<(string Plugin, string Function), SemanticKernelResolvedFunctionMetadata?>());
        var result = Assert.Single(catalog.GetAllowedFunctions());
        Assert.False(result.ExistsInKernel);
        Assert.Null(result.Description);
        Assert.Null(result.Parameters);
    }

    [Fact]
    public void SemanticKernelCatalog_IncludesDescriptionWhenAvailable()
    {
        var catalog = CreateCatalog([new("Tools", "Echo")], new Dictionary<(string Plugin, string Function), SemanticKernelResolvedFunctionMetadata?>
        {
            [("Tools", "Echo")] = new("Useful description", [])
        });
        var result = Assert.Single(catalog.GetAllowedFunctions());
        Assert.Equal("Useful description", result.Description);
    }

    [Fact]
    public void SemanticKernelCatalog_IncludesParametersWhenAvailable()
    {
        var catalog = CreateCatalog([new("Tools", "Echo")], new Dictionary<(string Plugin, string Function), SemanticKernelResolvedFunctionMetadata?>
        {
            [("Tools", "Echo")] = new("desc", [new("name", "person", "string", true)])
        });
        var result = Assert.Single(catalog.GetAllowedFunctions());
        var parameter = Assert.Single(result.Parameters!);
        Assert.Equal("name", parameter.Name);
        Assert.True(parameter.IsRequired);
    }

    [Fact]
    public void SemanticKernelCatalog_DoesNotAutoAllowUnlistedKernelFunctions()
    {
        var catalog = CreateCatalog([new("Tools", "Echo")], new Dictionary<(string Plugin, string Function), SemanticKernelResolvedFunctionMetadata?>
        {
            [("Tools", "Echo")] = new("desc", []),
            [("Tools", "Hidden")]= new("desc", [])
        });

        var result = catalog.GetAllowedFunctions();

        Assert.Single(result);
        Assert.DoesNotContain(result, x => x.FunctionName.Equals("Hidden", StringComparison.Ordinal));
    }

    [Fact]
    public void SemanticKernelCatalog_RejectsNullKernelOrOptions()
    {
        Assert.Throws<ArgumentNullException>(() => new SemanticKernelFunctionCatalog((Microsoft.SemanticKernel.Kernel)null!, new()));
        Assert.Throws<ArgumentNullException>(() => new SemanticKernelFunctionCatalog(new FakeMetadataReader(new Dictionary<(string Plugin, string Function), SemanticKernelResolvedFunctionMetadata?>()), null!));
    }

    private static SemanticKernelFunctionCatalog CreateCatalog(
        IReadOnlyList<AllowedSemanticKernelFunction> allowed,
        IReadOnlyDictionary<(string Plugin, string Function), SemanticKernelResolvedFunctionMetadata?> available)
    {
        var options = new SemanticKernelActuatorOptions { AllowedFunctions = allowed };
        return new SemanticKernelFunctionCatalog(new FakeMetadataReader(available), options);
    }

    private sealed class FakeMetadataReader(IReadOnlyDictionary<(string Plugin, string Function), SemanticKernelResolvedFunctionMetadata?> byFunction)
        : ISemanticKernelFunctionMetadataReader
    {
        public bool TryGetMetadata(string pluginName, string functionName, out SemanticKernelResolvedFunctionMetadata? metadata)
        {
            if (byFunction.TryGetValue((pluginName, functionName), out var match))
            {
                metadata = match;
                return true;
            }

            metadata = null;
            return false;
        }
    }
}
