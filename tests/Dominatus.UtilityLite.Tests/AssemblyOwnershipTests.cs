using System.Reflection;
using Dominatus.Core;
using Dominatus.Core.Decision;
using Dominatus.UtilityLite;

namespace Dominatus.UtilityLite.Tests;

public sealed class AssemblyOwnershipTests
{
    [Fact]
    public void UtilityVocabulary_IsOwnedByOptFlow_AndForwardedByCompatibilityAssembly()
    {
        Assert.Equal("Dominatus.OptFlow", typeof(Utility).Assembly.GetName().Name);
        Assert.Equal(typeof(Utility), Assembly.Load("Dominatus.UtilityLite").GetType("Dominatus.UtilityLite.Utility"));
        Assert.Equal(typeof(When), Assembly.Load("Dominatus.UtilityLite").GetType("Dominatus.UtilityLite.When"));
        Assert.DoesNotContain(Assembly.Load("Dominatus.UtilityLite").DefinedTypes,
            type => type.FullName is "Dominatus.UtilityLite.Utility" or "Dominatus.UtilityLite.When");
    }

    [Fact]
    public void UtilityHelpers_ReturnCoreDecisionTypes()
    {
        Assert.IsType<UtilityOption>(Utility.Option("Combat", Utility.Always, "Combat"));
        Assert.IsType<DecisionPolicy>(Utility.Policy());
        Assert.IsType<DecisionSlot>(Utility.Slot("Intent"));
        Assert.Equal(typeof(Consideration), When.Score((_, _) => 0.4f).GetType());
    }
}
