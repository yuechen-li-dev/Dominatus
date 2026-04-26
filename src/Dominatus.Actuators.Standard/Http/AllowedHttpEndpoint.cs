namespace Dominatus.Actuators.Standard;

public sealed record AllowedHttpEndpoint
{
    public string Name { get; }
    public Uri BaseUri { get; }

    public AllowedHttpEndpoint(string name, Uri baseUri)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Endpoint name is required.", nameof(name));

        ArgumentNullException.ThrowIfNull(baseUri);

        if (!baseUri.IsAbsoluteUri)
            throw new ArgumentException("Endpoint base URI must be absolute.", nameof(baseUri));

        if (!string.Equals(baseUri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(baseUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Endpoint base URI scheme must be http or https.", nameof(baseUri));

        if (!string.IsNullOrEmpty(baseUri.UserInfo))
            throw new ArgumentException("Endpoint base URI must not include user info.", nameof(baseUri));

        if (!string.IsNullOrEmpty(baseUri.Fragment))
            throw new ArgumentException("Endpoint base URI fragment must be empty.", nameof(baseUri));

        Name = name;
        BaseUri = EnsureTrailingSlash(baseUri);
    }

    private static Uri EnsureTrailingSlash(Uri uri)
    {
        var builder = new UriBuilder(uri)
        {
            Fragment = string.Empty
        };

        if (!builder.Path.EndsWith("/", StringComparison.Ordinal))
            builder.Path += "/";

        return builder.Uri;
    }
}
