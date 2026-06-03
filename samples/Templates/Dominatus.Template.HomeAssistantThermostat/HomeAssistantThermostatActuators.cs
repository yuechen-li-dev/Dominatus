using System.Net.Http.Headers;
using System.Text;
using Dominatus.Actuators.HomeAssistant;

namespace Dominatus.Template.HomeAssistantThermostat;

public interface IHomeAssistantThermostatActuator
{
    Task SendAsync(HomeAssistantThermostatCommand command, CancellationToken cancellationToken = default);
}

public sealed class FakeHomeAssistantThermostatActuator : IHomeAssistantThermostatActuator
{
    private readonly List<HomeAssistantThermostatCommand> _commands = [];

    public IReadOnlyList<HomeAssistantThermostatCommand> Commands => _commands;

    public Task SendAsync(HomeAssistantThermostatCommand command, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _commands.Add(command);
        return Task.CompletedTask;
    }
}

public sealed class DryRunHomeAssistantThermostatActuator : IHomeAssistantThermostatActuator
{
    public List<HomeAssistantThermostatCommand> Commands { get; } = [];

    public Task SendAsync(HomeAssistantThermostatCommand command, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Commands.Add(command);
        return Task.CompletedTask;
    }
}

public sealed class LiveHomeAssistantThermostatActuator(HttpClient httpClient, Uri baseUri, string token) : IHomeAssistantThermostatActuator
{
    private readonly HttpClient _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    private readonly Uri _baseUri = NormalizeBaseUri(baseUri);
    private readonly string _token = !string.IsNullOrWhiteSpace(token) ? token : throw new ArgumentException("Token is required.", nameof(token));

    public async Task SendAsync(HomeAssistantThermostatCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        // Keep the typed command aligned with the package actuator contract even though this template avoids host setup.
        var typedBoundary = new CallHomeAssistantServiceCommand("climate", "set_hvac_mode", command.ToJsonPayload());
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(_baseUri, "services/climate/set_hvac_mode"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Content = new StringContent(typedBoundary.JsonData, Encoding.UTF8, "application/json");

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    private static Uri NormalizeBaseUri(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        if (!uri.IsAbsoluteUri || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) || !string.IsNullOrEmpty(uri.UserInfo))
        {
            throw new ArgumentException("HOMEASSISTANT_URL must be an absolute http(s) URL without user info.", nameof(uri));
        }

        var builder = new UriBuilder(uri) { Fragment = string.Empty };
        var path = builder.Path ?? string.Empty;
        if (!path.EndsWith("/", StringComparison.Ordinal))
        {
            path += "/";
        }

        if (!path.Equals("/api/", StringComparison.OrdinalIgnoreCase) && !path.EndsWith("/api/", StringComparison.OrdinalIgnoreCase))
        {
            path += "api/";
        }

        builder.Path = path;
        return builder.Uri;
    }
}
