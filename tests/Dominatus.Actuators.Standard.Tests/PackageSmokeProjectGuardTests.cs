namespace Dominatus.Actuators.Standard.Tests;

public sealed class PackageSmokeProjectGuardTests
{
    [Fact]
    public void PackageSmokeProject_UsesPackageReferenceWithoutStandardProjectReference()
    {
        var projectPath = Path.Combine(ProjectRoot(), "tests", "Dominatus.Actuators.Standard.PackageSmoke", "Dominatus.Actuators.Standard.PackageSmoke.csproj");
        Assert.True(File.Exists(projectPath), $"Smoke project not found: {projectPath}");

        var text = File.ReadAllText(projectPath);

        Assert.Contains("<PackageReference Include=\"Dominatus.Actuators.Standard\"", text, StringComparison.Ordinal);
        Assert.DoesNotContain("<ProjectReference", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Dominatus.Actuators.Standard.csproj", text, StringComparison.OrdinalIgnoreCase);
    }

    private static string ProjectRoot()
        => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
}
