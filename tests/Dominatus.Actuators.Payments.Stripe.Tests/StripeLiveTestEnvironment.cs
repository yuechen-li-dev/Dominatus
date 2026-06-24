using Xunit;
namespace Dominatus.Actuators.Payments.Stripe.Tests;

internal sealed record StripeLiveTestEnvironmentSnapshot(
    bool LiveTestsRequested,
    string? TestApiKey,
    string? ConnectedAccountId,
    bool PlatformFeeLiveTestsRequested);

internal sealed record StripeLiveTestEnvironment(
    bool IsEnabled,
    string? TestApiKey,
    string? ConnectedAccountId,
    bool PlatformFeeLiveTestsEnabled,
    string? SkipReason)
{
    public const string LiveTestsVariable = "DOMINATUS_STRIPE_LIVE_TESTS";
    public const string TestApiKeyVariable = "DOMINATUS_STRIPE_TEST_API_KEY";
    public const string ConnectedAccountIdVariable = "DOMINATUS_STRIPE_CONNECTED_ACCOUNT_ID";
    public const string PlatformFeeLiveTestsVariable = "DOMINATUS_STRIPE_LIVE_PLATFORM_FEE_TESTS";

    public static StripeLiveTestEnvironment Current => FromEnvironment(Environment.GetEnvironmentVariable);

    public static StripeLiveTestEnvironment FromEnvironment(Func<string, string?> read) => FromSnapshot(new StripeLiveTestEnvironmentSnapshot(
        IsOne(read(LiveTestsVariable)),
        Clean(read(TestApiKeyVariable)),
        Clean(read(ConnectedAccountIdVariable)),
        IsOne(read(PlatformFeeLiveTestsVariable))));

    internal static StripeLiveTestEnvironment FromSnapshot(StripeLiveTestEnvironmentSnapshot snapshot)
    {
        if (!snapshot.LiveTestsRequested)
            return Disabled("Stripe live smoke tests are skipped because DOMINATUS_STRIPE_LIVE_TESTS is not 1.", snapshot);

        if (string.IsNullOrWhiteSpace(snapshot.TestApiKey))
            return Disabled("Stripe live smoke tests are skipped because DOMINATUS_STRIPE_TEST_API_KEY is not set.", snapshot);

        if (!snapshot.TestApiKey.StartsWith("sk_test_", StringComparison.Ordinal))
            return Disabled("Stripe live smoke tests require a Stripe test-mode secret key beginning with sk_test_.", snapshot);

        var platformFeeEnabled = snapshot.PlatformFeeLiveTestsRequested && !string.IsNullOrWhiteSpace(snapshot.ConnectedAccountId);
        return new StripeLiveTestEnvironment(true, snapshot.TestApiKey, snapshot.ConnectedAccountId, platformFeeEnabled, null);
    }

    public string RequireTestApiKeyOrSkip()
    {
        if (!IsEnabled || string.IsNullOrWhiteSpace(TestApiKey))
            Assert.Skip(SkipReason ?? "Stripe live smoke tests are disabled.");

        return TestApiKey;
    }

    public string RequireConnectedAccountOrSkip()
    {
        RequireTestApiKeyOrSkip();

        if (!PlatformFeeLiveTestsEnabled || string.IsNullOrWhiteSpace(ConnectedAccountId))
            Assert.Skip("Stripe Connect platform-fee smoke test is skipped because DOMINATUS_STRIPE_CONNECTED_ACCOUNT_ID is not set or DOMINATUS_STRIPE_LIVE_PLATFORM_FEE_TESTS is not 1.");

        return ConnectedAccountId;
    }

    private static StripeLiveTestEnvironment Disabled(string reason, StripeLiveTestEnvironmentSnapshot snapshot) => new(false, snapshot.TestApiKey, snapshot.ConnectedAccountId, false, reason);
    private static bool IsOne(string? value) => string.Equals(value?.Trim(), "1", StringComparison.Ordinal);
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
