using Xunit;
namespace Dominatus.Actuators.Payments.Stripe.Tests;

public sealed class StripeLiveTestEnvironmentTests
{
    [Fact]
    public void DisabledEnvironment_SkipsLiveTests()
    {
        var env = StripeLiveTestEnvironment.FromSnapshot(new(false, null, null, false));

        Assert.False(env.IsEnabled);
        Assert.Contains(StripeLiveTestEnvironment.LiveTestsVariable, env.SkipReason);
    }

    [Fact]
    public void EnabledEnvironment_RejectsNonTestModeKey()
    {
        var env = StripeLiveTestEnvironment.FromSnapshot(new(true, "sk_live_redacted", null, false));

        Assert.False(env.IsEnabled);
        Assert.DoesNotContain("sk_live_redacted", env.SkipReason);
        Assert.Contains("sk_test_", env.SkipReason);
    }

    [Fact]
    public void EnabledEnvironment_AcceptsTestModeKey()
    {
        var env = StripeLiveTestEnvironment.FromSnapshot(new(true, "sk_test_redacted", null, false));

        Assert.True(env.IsEnabled);
        Assert.Equal("sk_test_redacted", env.TestApiKey);
        Assert.False(env.PlatformFeeLiveTestsEnabled);
        Assert.Null(env.SkipReason);
    }

    [Fact]
    public void PlatformFeeLiveTests_RequireConnectedAccountAndExplicitFlag()
    {
        Assert.False(StripeLiveTestEnvironment.FromSnapshot(new(true, "sk_test_redacted", "acct_123", false)).PlatformFeeLiveTestsEnabled);
        Assert.False(StripeLiveTestEnvironment.FromSnapshot(new(true, "sk_test_redacted", null, true)).PlatformFeeLiveTestsEnabled);
        Assert.True(StripeLiveTestEnvironment.FromSnapshot(new(true, "sk_test_redacted", "acct_123", true)).PlatformFeeLiveTestsEnabled);
    }
}
